using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TodoWerk.Application.Abstractions.Authentication;
using TodoWerk.Application.Abstractions.Licensing;
using TodoWerk.Infrastructure.Indexing.Persistence;
using TodoWerk.Domain.Failures;

namespace TodoWerk.Infrastructure.Indexing;

/// <summary>
/// Drains the scan queue and keeps the index from going stale on its own.
/// <para>
/// Three jobs on one timer: run whatever scans are queued, queue a delta sync for anyone whose
/// index has aged past the configured interval and who has signed in lately enough to be reading
/// it, and sweep out finished rows past their retention. The queue lives in SQL (ADR-0003), so a
/// scan interrupted by a deploy is handed back on shutdown — or, if the process died outright,
/// reclaimed when its lease times out — rather than lost with the process that held it.
/// </para>
/// </summary>
internal sealed class IndexScanBackgroundService(
    IServiceScopeFactory scopeFactory,
    IOptions<IndexingOptions> options,
    TimeProvider timeProvider,
    ILogger<IndexScanBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;

        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation(
                "Index scan worker started; polling every {PollSeconds}s and syncing lists older than "
                + "{SyncMinutes} minutes for people who signed in within the last {IdleDays} days.",
                settings.PollInterval.TotalSeconds,
                settings.SyncInterval.TotalMinutes,
                settings.IdleAfter.TotalDays);
        }

        using var timer = new PeriodicTimer(settings.PollInterval, timeProvider);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TickAsync(stoppingToken);
            }
            catch (Exception exception) when (stoppingToken.IsCancellationRequested)
            {
                // Shutting down. Not only cancellation arrives here: a command cut off mid-flight
                // surfaces as a provider exception, and letting that fault the hosted service
                // would turn an ordinary deploy into a crash in the logs.
                logger.LogInformation(exception, "The index scan worker stopped mid-tick.");
                break;
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                // The worker outlives any one failure. A scan that throws is already recorded
                // against its own row; letting the loop die would silently stop every future one.
                logger.LogError(exception, "The index scan worker hit an error and will try again.");
            }

            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken))
                {
                    break;
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task TickAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();

        var scheduler = scope.ServiceProvider.GetRequiredService<IndexScanScheduler>();

        var queued = await scheduler.ScheduleDueSyncsAsync(
            options.Value.SyncInterval,
            options.Value.IdleAfter,
            cancellationToken);

        if (queued > 0 && logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation("Queued {Count} scheduled delta sync(s).", queued);
        }

        var purged = await scheduler.PurgeFinishedAsync(options.Value.FinishedScanRetention, cancellationToken);

        if (purged > 0 && logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation("Purged {Count} finished scan row(s) past retention.", purged);
        }

        var gate = scope.ServiceProvider.GetRequiredService<ILicenceGate>();

        // Rows claimed this tick for somebody who is not licensed, held rather than run. Holding
        // the lease is what lets the drain loop move past them: both claim queries skip a user
        // with a row already running, so a held row takes its owner out of the running for this
        // tick without taking anybody else out with them. Handed back at the end of it, untouched
        // — a Licence that comes back finds the work still queued.
        // Keyed on the row's id rather than on the claim, which is a fresh object every time the
        // row is won — so a re-claim would slip past a set of claims and be deferred twice.
        var deferred = new Dictionary<Guid, ClaimedScan>();

        try
        {
            await DrainAsync(scheduler, gate, deferred, cancellationToken);
        }
        finally
        {
            foreach (var scan in deferred.Values)
            {
                await ReleaseAsync(scan);
            }
        }
    }

    private async Task DrainAsync(
        IndexScanScheduler scheduler,
        ILicenceGate gate,
        Dictionary<Guid, ClaimedScan> deferred,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var scan = await scheduler.ClaimNextAsync(options.Value.ScanTimeout, cancellationToken);

            if (scan is null)
            {
                return;
            }

            // Asked after the claim rather than before it, because until a row is claimed there is
            // nobody to ask about. The answer comes from the resolver's per-person cache, which
            // the request path filled minutes ago, so this is a dictionary read rather than a call
            // to the portal on every claim.
            if (!await gate.IsLicensedAsync(new IndexUser(scan.TenantId, scan.UserId), cancellationToken))
            {
                // Seen before in this tick, which can only mean the tick has outlived the claim
                // timeout and the abandonment predicate has handed a deferred row back to us. Stop:
                // continuing would defer it a second time and the loop would never end.
                if (!deferred.TryAdd(scan.Id, scan))
                {
                    logger.LogWarning(
                        "The scan drain loop has run long enough for a deferred row's lease to be "
                        + "reclaimable, so it is stopping early. The queue is drained on the next tick.");

                    return;
                }

                if (logger.IsEnabled(LogLevel.Information))
                {
                    logger.LogInformation(
                        "Not scanning for one user in tenant {TenantId}: they are not licensed. "
                        + "The queued scan is left where it is.",
                        scan.TenantId);
                }

                continue;
            }

            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation(
                    "Running a {Mode} scan for one user in tenant {TenantId}.",
                    scan.Mode,
                    scan.TenantId);
            }

            // A fresh scope per scan: the runner writes through its own DbContext, and one user's
            // change tracker has no business outliving their scan.
            await using var runScope = scopeFactory.CreateAsyncScope();
            var runner = runScope.ServiceProvider.GetRequiredService<IndexScanRunner>();

            try
            {
                await runner.RunAsync(scan, cancellationToken);
            }
            catch (Exception) when (cancellationToken.IsCancellationRequested)
            {
                // Shutting down mid-scan. Hand the row back so the next process to poll picks it
                // up immediately rather than waiting out the abandonment timeout — that wait is
                // exactly the window in which a manual re-scan request would be silently covered
                // by a frozen row. Every exception shape goes through here, not just cancellation:
                // a cut-off database command raises a provider exception, and that must not be
                // the one path that leaves the row wedged.
                await ReleaseAsync(scan);
                throw;
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                // Nothing inside the runner caught this, so its context cannot be trusted to
                // record anything. Mark the row failed through a fresh scope; the reason shown is
                // deliberately generic — exception text is for the log, not the UI.
                logger.LogError(
                    exception,
                    "The scan for tenant {TenantId} failed outside the runner's own handling.",
                    scan.TenantId);

                await FailAsync(scan, cancellationToken);
            }
        }
    }

    /// <summary>Best effort, on a fresh scope and no cancellation: this is the shutdown path.</summary>
    private async Task ReleaseAsync(ClaimedScan scan)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();

            await scope.ServiceProvider.GetRequiredService<IndexScanScheduler>()
                .ReleaseAsync(scan, CancellationToken.None);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The lease will time out and the row will be reclaimed; losing the early hand-back
            // costs a delay, not the scan.
            logger.LogWarning(exception, "Releasing a claimed scan on shutdown failed; the lease will expire instead.");
        }
    }

    private async Task FailAsync(ClaimedScan scan, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();

            await scope.ServiceProvider.GetRequiredService<IndexScanScheduler>()
                .FailAsync(
                    scan,
                    "The scan hit an unexpected error. TodoWerk will try again.",
                    FailureCode.Unknown,
                    cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Recording a scan failure failed; the lease will expire instead.");
        }
    }
}
