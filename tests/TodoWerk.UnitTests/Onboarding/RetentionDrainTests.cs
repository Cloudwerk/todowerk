using TodoWerk.Infrastructure.Onboarding;
using Xunit;

namespace TodoWerk.UnitTests.Onboarding;

/// <summary>
/// The drain's three decisions, pinned by scripting the batch results: keep going on a full batch,
/// stop on a partial one, stop at the cap. Continuing on <em>forgotten</em> instead would let one
/// permanently-unfinishable person stall the whole backlog behind them.
/// </summary>
public sealed class RetentionDrainTests
{
    private const int BatchSize = 50;

    private const int Cap = 20;

    [Fact]
    public async Task Drain_RunsBatchesWhileFullOnesKeepComing_AndStopsOnThePartialOne()
    {
        var batches = 0;

        await OnboardingBackgroundService.DrainAsync(
            _ =>
            {
                batches++;

                // Two full batches, then one that came up short: the backlog is drained.
                return Task.FromResult(new RetentionSweepResult(
                    Found: batches < 3 ? BatchSize : BatchSize - 1,
                    Forgotten: batches < 3 ? BatchSize : BatchSize - 1));
            },
            BatchSize,
            Cap,
            TestContext.Current.CancellationToken);

        Assert.Equal(3, batches);
    }

    /// <summary>
    /// The regression this file exists for. Every batch is full but one person in it cannot be
    /// finished — the shape one permanently-unfinishable person produces, sorted first by dormancy
    /// into every batch. A drain that watched <em>forgotten</em> stopped after one batch and
    /// stranded the backlog; watching <em>found</em>, it keeps draining and the cap bounds what the
    /// stuck person costs.
    /// </summary>
    [Fact]
    public async Task Drain_ContinuesOnAFullBatch_EvenWhenSomebodyInItCouldNotBeFinished()
    {
        var batches = 0;

        await OnboardingBackgroundService.DrainAsync(
            _ =>
            {
                batches++;

                return Task.FromResult(new RetentionSweepResult(BatchSize, Forgotten: BatchSize - 1));
            },
            BatchSize,
            Cap,
            TestContext.Current.CancellationToken);

        Assert.Equal(Cap, batches);
    }

    [Fact]
    public async Task Drain_StopsAtTheCap_HoweverMuchBacklogRemains()
    {
        var batches = 0;

        await OnboardingBackgroundService.DrainAsync(
            _ =>
            {
                batches++;

                return Task.FromResult(new RetentionSweepResult(BatchSize, BatchSize));
            },
            BatchSize,
            Cap,
            TestContext.Current.CancellationToken);

        Assert.Equal(Cap, batches);
    }

    [Fact]
    public async Task Drain_WithNothingDormant_RunsOneBatchAndStops()
    {
        var batches = 0;

        await OnboardingBackgroundService.DrainAsync(
            _ =>
            {
                batches++;

                return Task.FromResult(new RetentionSweepResult(0, 0));
            },
            BatchSize,
            Cap,
            TestContext.Current.CancellationToken);

        Assert.Equal(1, batches);
    }

    /// <summary>
    /// Cancellation lands between batches: the one in flight finishes — its own cancellation
    /// handling is the eraser's business — and no further batch starts.
    /// </summary>
    [Fact]
    public async Task Drain_WhenCancelledDuringABatch_StartsNoFurtherBatch()
    {
        using var cancellation = new CancellationTokenSource();
        var batches = 0;

        await OnboardingBackgroundService.DrainAsync(
            async _ =>
            {
                batches++;
                await cancellation.CancelAsync();

                return new RetentionSweepResult(BatchSize, BatchSize);
            },
            BatchSize,
            Cap,
            cancellation.Token);

        Assert.Equal(1, batches);
    }
}
