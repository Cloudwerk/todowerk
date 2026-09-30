using TodoWerk.Domain.Indexing;
using Xunit;
using TodoWerk.Domain.Failures;

namespace TodoWerk.UnitTests.Indexing;

/// <summary>
/// The per-list freshness contract. Two of these are load-bearing beyond M1: the write gate M2's
/// rename checks before touching a list, and the delta link whose absence is the only signal a
/// resync needs.
/// </summary>
public sealed class TaskListIndexStateTests
{
    private static readonly DateTimeOffset Noon = new(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ANewList_IsNeitherScannedNorWritable()
    {
        var state = CreateState();

        Assert.Equal(ListScanState.NeverScanned, state.State);
        Assert.False(state.IsFullyIndexed);
        Assert.False(state.CanSyncIncrementally);
        Assert.Null(state.LastSuccessfulSyncAt);
    }

    /// <summary>ADR-0003: a write must not run against a list that is only half read.</summary>
    [Fact]
    public void AListBeingScanned_IsNotYetWritable()
    {
        var state = CreateState();

        state.BeginScan(Noon);

        Assert.Equal(ListScanState.Scanning, state.State);
        Assert.False(state.IsFullyIndexed);
    }

    [Fact]
    public void AFullScan_OpensTheWriteGateAndRecordsFreshness()
    {
        var state = CreateState();

        state.BeginScan(Noon);
        state.CompleteFullScan("delta-token", Noon.AddMinutes(4));

        Assert.Equal(ListScanState.Indexed, state.State);
        Assert.True(state.IsFullyIndexed);
        Assert.Equal(Noon.AddMinutes(4), state.LastCompletedScanAt);
        Assert.Equal(Noon.AddMinutes(4), state.LastSuccessfulSyncAt);
        Assert.True(state.CanSyncIncrementally);
    }

    /// <summary>
    /// A delta pass says the index is current, not that the list was read end to end — it only
    /// ever saw what Graph chose to mention. Moving the full-scan timestamp here would open the
    /// write gate on a list that was never fully read.
    /// </summary>
    [Fact]
    public void ADeltaSync_MovesFreshnessButNotTheFullScanTimestamp()
    {
        var state = CreateState();
        state.BeginScan(Noon);
        state.CompleteFullScan("delta-token", Noon.AddMinutes(4));

        state.CompleteDeltaSync("newer-delta-token", Noon.AddHours(1));

        Assert.Equal(Noon.AddHours(1), state.LastSuccessfulSyncAt);
        Assert.Equal(Noon.AddMinutes(4), state.LastCompletedScanAt);
    }

    /// <summary>
    /// A delta sync of a list that was never fully scanned must not claim the list is writable.
    /// Nothing schedules that today; this pins that the flag cannot be reached by that route.
    /// </summary>
    [Fact]
    public void ADeltaSync_DoesNotOpenTheWriteGateOnItsOwn()
    {
        var state = CreateState();

        state.CompleteDeltaSync("delta-token", Noon);

        Assert.False(state.IsFullyIndexed);
    }

    [Fact]
    public void AFailedList_KeepsWhatItIndexedAndSaysWhy()
    {
        var state = CreateState();
        state.BeginScan(Noon);
        state.RecordProgress(120);

        state.FailScan("Microsoft Graph is throttling this list.", FailureCode.Throttled, Noon.AddMinutes(2));

        Assert.Equal(ListScanState.Failed, state.State);
        Assert.Equal("Microsoft Graph is throttling this list.", state.FailureReason);
        Assert.Equal(120, state.TasksIndexed);
        Assert.False(state.IsFullyIndexed);
    }

    [Fact]
    public void ASuccessfulPass_ClearsAnEarlierFailure()
    {
        var state = CreateState();
        state.FailScan("Microsoft Graph is throttling this list.", FailureCode.Throttled, Noon);

        state.BeginScan(Noon.AddHours(1));
        state.CompleteFullScan("delta-token", Noon.AddHours(1).AddMinutes(3));

        Assert.Null(state.FailureReason);
        Assert.Equal(ListScanState.Indexed, state.State);
    }

    /// <summary>
    /// The resync-required signal, in full. Graph expires delta tokens; dropping the link is the
    /// entire remedy, because a pass with no link reads the whole list.
    /// </summary>
    [Fact]
    public void RequireFullResync_LeavesTheListWithoutADeltaLink()
    {
        var state = CreateState();
        state.CompleteFullScan("delta-token", Noon);

        state.RequireFullResync();

        Assert.False(state.CanSyncIncrementally);
        Assert.True(state.IsFullyIndexed);
    }

    [Fact]
    public void BeginScan_RestartsTheProgressCount()
    {
        var state = CreateState();
        state.BeginScan(Noon);
        state.RecordProgress(120);

        state.BeginScan(Noon.AddHours(1));

        Assert.Equal(0, state.TasksIndexed);
        Assert.Equal(Noon.AddHours(1), state.LastAttemptAt);
    }

    private static TaskListIndexState CreateState() =>
        TaskListIndexState.Create("tenant", "user", "list-1", "Einkaufen");
}
