using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TodoWerk.Application.Abstractions.Authentication;
using TodoWerk.Application.Abstractions.Licensing;
using TodoWerk.Application.Changes;
using TodoWerk.Domain.Changes;
using TodoWerk.Domain.Failures;
using TodoWerk.Infrastructure.Changes.Persistence;

namespace TodoWerk.Infrastructure.Changes;

/// <summary>
/// Drains the Change queue and sweeps it.
/// <para>
/// A worker of its own rather than another job on the scan worker's timer, which is the whole
/// point of the module boundary: it polls every few seconds because somebody is watching a Change
/// they just confirmed, and it purges after thirty days because its rows are the only record of
/// what a task used to be called.
/// </para>
/// </summary>
internal sealed class ChangeBackgroundService(
    IServiceScopeFactory scopeFactory,
    IOptions<ChangeOptions> options,
    TimeProvider timeProvider,
    ILogger<ChangeBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;

        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation(
                "Change worker started; polling every {PollSeconds}s.",
                settings.PollInterval.TotalSeconds);
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
                logger.LogInformation(exception, "The change worker stopped mid-tick.");
                break;
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                // The worker outlives any one failure. A Change that throws is already recorded
                // against its own row; letting the loop die would silently stop every future one.
                logger.LogError(exception, "The change worker hit an error and will try again.");
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
        var settings = options.Value;

        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            var purged = await scope.ServiceProvider.GetRequiredService<ChangeQueue>()
                .PurgeFinishedAsync(settings.ChangeRetention, cancellationToken);

            if (purged > 0 && logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("Purged {Count} change(s) past retention, with their journals.", purged);
            }
        }

        // Rows claimed this tick for somebody who is not licensed, held rather than run. See the
        // scan worker for why holding the lease is what lets the loop move past them; the Change
        // itself is left Pending, so a Licence that comes back finds it still queued.
        // Keyed on the row's id, for the reason the scan worker gives: a claim is a fresh object
        // every time the row is won.
        var deferred = new Dictionary<Guid, ClaimedChange>();

        try
        {
            await DrainAsync(settings, deferred, cancellationToken);
        }
        finally
        {
            foreach (var claimed in deferred.Values)
            {
                await ReleaseAsync(claimed);
            }
        }
    }

    private async Task DrainAsync(
        ChangeOptions settings,
        Dictionary<Guid, ClaimedChange> deferred,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await using var claimScope = scopeFactory.CreateAsyncScope();

            var claimed = await claimScope.ServiceProvider.GetRequiredService<ChangeQueue>()
                .ClaimNextAsync(settings.ChangeTimeout, cancellationToken);

            if (claimed is null)
            {
                return;
            }

            // The same question the scan claim asks, answered from the same cache. A Change
            // already running is not asked again and is never cut off mid-write: what a lapsed
            // Licence stops is the next Change starting, not the one somebody is watching.
            var gate = claimScope.ServiceProvider.GetRequiredService<ILicenceGate>();

            if (!await gate.IsLicensedAsync(new IndexUser(claimed.TenantId, claimed.UserId), cancellationToken))
            {
                // Seen before in this tick — see the scan worker for what that means and why the
                // answer is to stop rather than to defer it again.
                if (!deferred.TryAdd(claimed.Id, claimed))
                {
                    logger.LogWarning(
                        "The change drain loop has run long enough for a deferred row's lease to be "
                        + "reclaimable, so it is stopping early. The queue is drained on the next tick.");

                    return;
                }

                if (logger.IsEnabled(LogLevel.Information))
                {
                    logger.LogInformation(
                        "Not running change {ChangeId}: its owner is not licensed. It is left queued.",
                        claimed.Id);
                }

                continue;
            }

            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation(
                    "Running change {ChangeId} for one user in tenant {TenantId}.",
                    claimed.Id,
                    claimed.TenantId);
            }

            // A fresh scope per Change: the runner writes through its own DbContext, and one
            // user's change tracker has no business outliving their Change.
            await using var runScope = scopeFactory.CreateAsyncScope();
            var runner = runScope.ServiceProvider.GetRequiredService<ChangeRunner>();

            try
            {
                await runner.RunAsync(claimed, cancellationToken);
            }
            catch (Exception) when (cancellationToken.IsCancellationRequested)
            {
                // Shutting down mid-Change. Hand the row back so the next process picks it up
                // immediately rather than waiting out the abandonment timeout; the plan rows
                // already record which tasks were written, so it resumes rather than repeats.
                await ReleaseAsync(claimed);
                throw;
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                // Nothing inside the runner caught this, so its context cannot be trusted to
                // record anything. Mark the row failed through a fresh scope; the reason shown is
                // deliberately generic — exception text is for the log, not the UI.
                logger.LogError(
                    exception,
                    "Change {ChangeId} failed outside the runner's own handling.",
                    claimed.Id);

                await FailAsync(claimed, cancellationToken);
            }
        }
    }

    /// <summary>Best effort, on a fresh scope and no cancellation: this is the shutdown path.</summary>
    private async Task ReleaseAsync(ClaimedChange claimed)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();

            await scope.ServiceProvider.GetRequiredService<ChangeQueue>()
                .ReleaseAsync(claimed, CancellationToken.None);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The lease will time out and the row will be reclaimed; losing the early hand-back
            // costs a delay, not the Change.
            logger.LogWarning(exception, "Releasing a claimed change on shutdown failed; the lease will expire instead.");
        }
    }

    private async Task FailAsync(ClaimedChange claimed, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();

            await scope.ServiceProvider.GetRequiredService<ChangeQueue>()
                .FinishAsync(
                    claimed,
                    ChangeState.Failed,
                    FailureCode.Unknown,
                    "TodoWerk hit an unexpected error while changing your tasks. Anything it had "
                    + "already changed is listed below and can be undone.",
                    cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Recording a change failure failed; the lease will expire instead.");
        }
    }
}
