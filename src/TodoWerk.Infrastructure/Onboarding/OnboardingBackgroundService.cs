using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TodoWerk.Application.Onboarding;

namespace TodoWerk.Infrastructure.Onboarding;

/// <summary>
/// Forgets people who have stopped coming back, down the same path somebody takes when they ask for
/// it themselves.
/// <para>
/// A worker of its own rather than another job on the scan or Change worker's timer, for the reason
/// the Change worker has one: those live inside their modules, and this one has to be reachable from
/// the module that owns the Tenant Member record. Its cadence differs accordingly — the deadline it
/// keeps is a year long, so it looks hourly rather than every few seconds.
/// </para>
/// <para>
/// It is also what an administrator revoking TodoWerk in Entra ID amounts to. Nothing tells TodoWerk
/// the revocation happened, but nobody can sign in afterwards, so everybody in that tenant goes
/// dormant and this clears them. There is no separate organisation-wide action and none is wanted.
/// </para>
/// </summary>
internal sealed class OnboardingBackgroundService(
    IServiceScopeFactory scopeFactory,
    IOptions<OnboardingOptions> options,
    TimeProvider timeProvider,
    ILogger<OnboardingBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;

        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation(
                "Retention sweep started; looking every {SweepMinutes} minute(s) for people who have "
                + "not signed in for {DormancyDays} day(s).",
                settings.SweepInterval.TotalMinutes,
                settings.DormancyWindow.TotalDays);
        }

        using var timer = new PeriodicTimer(settings.SweepInterval, timeProvider);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepAsync(stoppingToken);
            }
            catch (Exception exception) when (stoppingToken.IsCancellationRequested)
            {
                // Shutting down. Not only cancellation arrives here: a command cut off mid-flight
                // surfaces as a provider exception, and letting that fault the hosted service would
                // turn an ordinary deploy into a crash in the logs.
                logger.LogInformation(exception, "The retention sweep stopped mid-tick.");
                break;
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                // The worker outlives any one failure. Whoever was mid-erasure still holds an
                // identifiable membership row, so the next tick finds them again and finishes.
                logger.LogError(exception, "The retention sweep hit an error and will try again.");
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

    /// <summary>
    /// The most batches one tick will run before waiting out the interval, however much backlog
    /// remains. The drain and the bound pull against each other and both are owed something: one
    /// batch per hourly tick keeps a retention promise months late on any real backlog, while an
    /// unbounded drain hands the database a whole backlog's deletes back to back the moment
    /// somebody shortens the dormancy window. Twenty batches of fifty is a thousand people an
    /// hour — a deployment down for a year clears in a day, without any single tick monopolising
    /// the server the requests and the other two workers are on.
    /// </summary>
    private const int MaxBatchesPerTick = 20;

    /// <summary>One tick: the drain below, with each batch running in a scope of its own.</summary>
    private Task SweepAsync(CancellationToken cancellationToken) =>
        DrainAsync(
            async token =>
            {
                await using var scope = scopeFactory.CreateAsyncScope();

                return await scope.ServiceProvider.GetRequiredService<RetentionSweep>()
                    .SweepAsync(token);
            },
            options.Value.SweepBatchSize,
            MaxBatchesPerTick,
            cancellationToken);

    /// <summary>
    /// Drains batches while full ones keep coming, up to <paramref name="maxBatchesPerTick"/>.
    /// <para>
    /// The loop continues on <em>found</em>, not on <em>forgotten</em>: a full batch means there may
    /// be more behind it, whether or not everybody in it could be finished. Continuing on forgotten
    /// would let one permanently-unfinishable person, who sorts first by dormancy, make every batch
    /// come up one short and stall the drain for the whole backlog behind them. The cap is what
    /// bounds the retries such a person costs within one tick.
    /// </para>
    /// <para>
    /// A seam rather than a private loop, for the reason <see cref="RetentionSweep"/> itself is one:
    /// its three decisions — keep going on a full batch, stop on a partial one, stop at the cap —
    /// are pinned by unit tests that script the batch results.
    /// </para>
    /// </summary>
    internal static async Task DrainAsync(
        Func<CancellationToken, Task<RetentionSweepResult>> runBatch,
        int batchSize,
        int maxBatchesPerTick,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(runBatch);

        for (var batch = 0; batch < maxBatchesPerTick && !cancellationToken.IsCancellationRequested; batch++)
        {
            var result = await runBatch(cancellationToken);

            if (result.Found < batchSize)
            {
                return;
            }
        }
    }
}
