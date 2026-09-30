using TodoWerk.Domain.Failures;
using TodoWerk.SharedKernel;

namespace TodoWerk.Domain.Indexing;

/// <summary>
/// A request to bring one user's index up to date, queued in the database and drained by a
/// background worker.
/// <para>
/// A row rather than a queue in memory, for the reason ADR-0003 gives for keeping the job queue in
/// SQL: a first scan takes minutes, the process can be replaced mid-flight by a deploy, and the
/// work has to be picked up again afterwards rather than lost. It is also what makes a manual
/// re-scan idempotent — asking twice finds the first request still pending.
/// </para>
/// </summary>
public sealed class IndexScan : Entity<Guid>
{
    private IndexScan(
        Guid id,
        string tenantId,
        string userId,
        IndexScanMode mode,
        string? taskListId,
        DateTimeOffset requestedAt)
        : base(id)
    {
        TenantId = tenantId;
        UserId = userId;
        Mode = mode;
        TaskListId = taskListId;
        RequestedAt = requestedAt;
    }

    public string TenantId { get; private set; }

    /// <summary>
    /// The Entra ID object id. With the tenant id it forms the account identifier MSAL keys the
    /// token cache on, which is how a worker with no HTTP request in sight still gets a token for
    /// this user (ADR-0002, and the durable token cache).
    /// </summary>
    public string UserId { get; private set; }

    public IndexScanMode Mode { get; private set; }

    /// <summary>The one list to scan, or null for every list the user has.</summary>
    public string? TaskListId { get; private set; }

    public IndexScanState State { get; private set; } = IndexScanState.Pending;

    public DateTimeOffset RequestedAt { get; private set; }

    /// <summary>
    /// Set by the worker's claim rather than by a method here. Claiming has to be one conditional
    /// update — read, decide, write would let two processes claim the same scan and index one
    /// mailbox twice — so the transition to <see cref="IndexScanState.Running"/> is the only one
    /// this entity does not own.
    /// </summary>
    public DateTimeOffset? StartedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>Why the whole scan failed — not why one list did, which lives on the list's own state.</summary>
    public string? FailureReason { get; private set; }

    /// <summary>The same failure as a value the client can switch on.</summary>
    public FailureCode FailureCode { get; private set; } = FailureCode.None;

    /// <summary>
    /// This scan has already stood aside once for a queued Change, and will not do so again
    /// (ADR-0006). Without it a user confirming Changes back to back would preempt their own index
    /// forever and it would never finish a pass.
    /// </summary>
    public bool WasPreempted { get; private set; }

    public static IndexScan Request(
        string tenantId,
        string userId,
        IndexScanMode mode,
        string? taskListId,
        DateTimeOffset requestedAt) =>
        new(Guid.CreateVersion7(), tenantId, userId, mode, taskListId, requestedAt);

    public void Complete(DateTimeOffset completedAt)
    {
        State = IndexScanState.Completed;
        CompletedAt = completedAt;
    }

    /// <summary>
    /// The scan itself could not run — no token, no list of lists. Individual lists failing does
    /// not come through here; they are recorded per list and the scan still completes.
    /// </summary>
    public void Fail(string reason, FailureCode code, DateTimeOffset failedAt)
    {
        State = IndexScanState.Failed;
        FailureReason = reason;
        FailureCode = code;
        CompletedAt = failedAt;
    }
}
