using TodoWerk.Domain.Indexing;

namespace TodoWerk.Infrastructure.Indexing.Persistence;

/// <summary>
/// A scan the worker has won: the row's identity, what it asks for, and the lease under which
/// this process may write it.
/// </summary>
/// <remarks>
/// The lease is the <c>StartedAt</c> value the claim wrote. Every later write to the row — a
/// renewal, the completion, a failure — is conditional on it still being there, which is what
/// lets a process discover that it lost the row to the abandoned-scan recovery instead of
/// silently finishing work that now belongs to somebody else. Renewal moves it, so it is the one
/// mutable thing here.
/// </remarks>
internal sealed class ClaimedScan(
    Guid id,
    string tenantId,
    string userId,
    IndexScanMode mode,
    string? taskListId,
    bool wasPreempted,
    DateTimeOffset lease)
{
    public Guid Id { get; } = id;

    public string TenantId { get; } = tenantId;

    public string UserId { get; } = userId;

    public IndexScanMode Mode { get; } = mode;

    public string? TaskListId { get; } = taskListId;

    /// <summary>
    /// This scan already stood aside once for a queued Change and will run to completion this
    /// time, so a run of Changes cannot starve somebody's index (ADR-0006).
    /// </summary>
    public bool WasPreempted { get; } = wasPreempted;

    /// <summary>The <c>StartedAt</c> this process last wrote. Updated by each successful renewal.</summary>
    public DateTimeOffset Lease { get; set; } = lease;
}
