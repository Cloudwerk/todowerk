using TodoWerk.Domain.Failures;
using TodoWerk.SharedKernel;

namespace TodoWerk.Domain.Indexing;

/// <summary>
/// How current the index is for one To Do list, and what it is doing about it. Persisted rather
/// than held in memory for two reasons: a first scan over a real mailbox takes minutes and has to
/// survive a restart, and ADR-0003 makes freshness something the Workbench shows rather than
/// something an operator reads out of a log.
/// </summary>
public sealed class TaskListIndexState : Entity<Guid>
{
    private TaskListIndexState(
        Guid id,
        string tenantId,
        string userId,
        string taskListId,
        string displayName)
        : base(id)
    {
        TenantId = tenantId;
        UserId = userId;
        TaskListId = taskListId;
        DisplayName = displayName;
    }

    public string TenantId { get; private set; }

    public string UserId { get; private set; }

    public string TaskListId { get; private set; }

    /// <summary>Copied from Graph so the Workbench can name a list without a second round trip.</summary>
    public string DisplayName { get; private set; }

    public ListScanState State { get; private set; } = ListScanState.NeverScanned;

    /// <summary>
    /// Graph's delta link for the next round. Null means the next pass has to read the whole list:
    /// either it never ran, or Graph refused the token and asked for a resync.
    /// </summary>
    public string? DeltaLink { get; private set; }

    /// <summary>Tasks read in the pass that is running, or in the last one that finished.</summary>
    public int TasksIndexed { get; private set; }

    /// <summary>
    /// When a pass — full or delta — last brought the list up to date. This is the freshness the
    /// UI shows.
    /// </summary>
    public DateTimeOffset? LastSuccessfulSyncAt { get; private set; }

    /// <summary>
    /// When the list was last read end to end. M2's rename gates on this: a half-scanned list has
    /// Occurrences the rename would miss, so a write against it would leave the tag behind in
    /// tasks TodoWerk has not seen (ADR-0003).
    /// </summary>
    public DateTimeOffset? LastCompletedScanAt { get; private set; }

    public DateTimeOffset? LastAttemptAt { get; private set; }

    /// <summary>Why the last pass gave up, in the words the UI will show. Cleared by the next success.</summary>
    public string? FailureReason { get; private set; }

    /// <summary>
    /// The same failure as a value the client can switch on. Stored alongside the
    /// sentence rather than instead of it: the sentence is written where the cause is known, and
    /// this is what decides whether a sign-in button is offered beside it, so nothing has to look
    /// for a phrase in the sentence.
    /// </summary>
    public FailureCode FailureCode { get; private set; } = FailureCode.None;

    /// <summary>Whether the whole list has been read at least once, which is what a write requires.</summary>
    public bool IsFullyIndexed => LastCompletedScanAt is not null;

    /// <summary>A delta pass is only possible while Graph still honours the token from the last one.</summary>
    public bool CanSyncIncrementally => DeltaLink is not null;

    public static TaskListIndexState Create(
        string tenantId,
        string userId,
        string taskListId,
        string displayName) =>
        new(Guid.CreateVersion7(), tenantId, userId, taskListId, displayName);

    /// <summary>Graph is the authority on what a list is called; a rename there is not our event.</summary>
    public void Rename(string displayName) => DisplayName = displayName;

    /// <summary>
    /// A pass over this list has started. The previous failure is cleared with it: it described
    /// an attempt that is over, and leaving it would let the UI show a list as current and give
    /// a reason it is not in the same breath.
    /// </summary>
    public void BeginScan(DateTimeOffset startedAt)
    {
        State = ListScanState.Scanning;
        LastAttemptAt = startedAt;
        TasksIndexed = 0;
        FailureReason = null;
        FailureCode = FailureCode.None;
    }

    /// <summary>
    /// Progress within the running pass. Written as the pages come in, so a restart mid-scan
    /// leaves a number the UI can show rather than a silent gap.
    /// </summary>
    public void RecordProgress(int tasksIndexed) => TasksIndexed = tasksIndexed;

    /// <summary>The list was read end to end. Both timestamps move, and the write gate opens.</summary>
    public void CompleteFullScan(string? deltaLink, DateTimeOffset completedAt)
    {
        State = ListScanState.Indexed;
        DeltaLink = deltaLink;
        LastSuccessfulSyncAt = completedAt;
        LastCompletedScanAt = completedAt;
        FailureReason = null;
        FailureCode = FailureCode.None;
    }

    /// <summary>
    /// A delta page was applied. Freshness moves; the full-scan timestamp does not, because an
    /// incremental pass says nothing about the parts of the list Graph did not mention.
    /// </summary>
    public void CompleteDeltaSync(string? deltaLink, DateTimeOffset completedAt)
    {
        State = ListScanState.Indexed;
        DeltaLink = deltaLink;
        LastSuccessfulSyncAt = completedAt;
        FailureReason = null;
        FailureCode = FailureCode.None;
    }

    /// <summary>
    /// Graph rejected the delta token and asked for a full resync. Dropping the link is all that
    /// is needed: the next pass finds none and reads the whole list, without an operator.
    /// </summary>
    public void RequireFullResync() => DeltaLink = null;

    /// <summary>
    /// This list gave up. Whatever it already indexed stays — a partial list is more useful than
    /// an empty one, and <see cref="LastCompletedScanAt"/> still says the write gate is shut.
    /// </summary>
    public void FailScan(string reason, FailureCode code, DateTimeOffset failedAt)
    {
        State = ListScanState.Failed;
        FailureReason = reason;
        FailureCode = code;
        LastAttemptAt = failedAt;
    }
}
