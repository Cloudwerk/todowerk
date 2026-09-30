using TodoWerk.Application.Abstractions.Authentication;

namespace TodoWerk.Application.Changes;

/// <summary>
/// Where Changes are kept, from the request path's point of view. The worker's own claim, lease
/// and completion writes are not here: those have to be conditional updates against a lease and
/// belong beside the queue they guard, not behind a port a handler could call.
/// </summary>
public interface IChangeStore
{
    /// <summary>
    /// Whether this user already has a Change waiting or running. One at a time (ADR-0006) — a
    /// second one would be planned against an index the first is about to change.
    /// </summary>
    Task<bool> HasUnfinishedChangeAsync(IndexUser user, CancellationToken cancellationToken);

    /// <summary>
    /// Persists a confirmed Change and every row of its plan in one save, which is what fixes the
    /// scope to what the user saw. Returns the new Change's id.
    /// </summary>
    Task<Guid> AddAsync(IndexUser user, ConfirmedChangePlan plan, CancellationToken cancellationToken);

    /// <summary>The Change in flight and the ones still inside retention, newest first.</summary>
    Task<IReadOnlyList<ChangeRecord>> ReadQueueAsync(IndexUser user, CancellationToken cancellationToken);

    /// <summary>One Change of this user's, or null.</summary>
    Task<ChangeRecord?> FindAsync(IndexUser user, Guid changeId, CancellationToken cancellationToken);

    /// <summary>
    /// Asks the runner to stop after the task it is on. False when there is no such unfinished
    /// Change of this user's. Idempotent.
    /// </summary>
    Task<bool> RequestCancelAsync(IndexUser user, Guid changeId, CancellationToken cancellationToken);

    /// <summary>
    /// Queues the reverse of a Change: a Change like any other, whose plan comes from the journal
    /// rather than from the index, because only the journal knows what was actually written.
    /// Returns the new Change's id, or null when the journal turned out to hold nothing.
    /// </summary>
    Task<Guid?> AddUndoAsync(IndexUser user, Guid changeId, CancellationToken cancellationToken);
}
