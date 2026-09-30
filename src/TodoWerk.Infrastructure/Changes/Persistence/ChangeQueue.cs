using Microsoft.EntityFrameworkCore;
using TodoWerk.Domain.Changes;
using TodoWerk.Domain.Failures;
using TodoWerk.Domain.Indexing;
using TodoWerk.Infrastructure.Persistence;

namespace TodoWerk.Infrastructure.Changes.Persistence;

/// <summary>
/// The Change queue, which is a table — and this class is the only thing that writes its rows'
/// lifecycle.
/// <para>
/// Deliberately the same shape as <c>IndexScanScheduler</c>: claim by conditional update, a lease
/// renewed as the work proceeds, a hand-back on shutdown, a retention purge. Repeated rather than
/// generalised into something both must fit, because the two queues agree on almost nothing else —
/// one drains in fifteen-second polls and purges after a week, the other starts in three seconds
/// and is an audit trail for thirty days.
/// </para>
/// </summary>
internal sealed class ChangeQueue(TodoWerkDbContext context, TimeProvider timeProvider)
{
    /// <summary>
    /// Takes the oldest waiting Change, if the claim wins — and only if this user has neither
    /// another Change running nor a scan running.
    /// <para>
    /// Both exclusions are in the claim's own <c>WHERE</c> rather than in a method around it, so
    /// there is no window between deciding and writing. That is also why this class
    /// names <see cref="IndexScan"/> at all: a port cannot be evaluated inside a conditional
    /// <c>UPDATE</c>, and the two queues share one database precisely so that this question can be
    /// asked in one statement.
    /// </para>
    /// </summary>
    internal async Task<ClaimedChange?> ClaimNextAsync(TimeSpan changeTimeout, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var abandonedBefore = now - changeTimeout;

        var candidate = await context.Set<Change>()
            .AsNoTracking()
            .Where(change => change.State == ChangeState.Pending
                || (change.State == ChangeState.Running && change.StartedAt < abandonedBefore))
            // A user whose Change is genuinely running is skipped here rather than blocked on:
            // their other rows would otherwise hold up every other user's Change behind one lease.
            .Where(change => !context.Set<Change>().Any(other => other.Id != change.Id
                && other.TenantId == change.TenantId
                && other.UserId == change.UserId
                && other.State == ChangeState.Running
                && other.StartedAt >= abandonedBefore))
            .Where(change => !context.Set<IndexScan>().Any(scan => scan.TenantId == change.TenantId
                && scan.UserId == change.UserId
                && scan.State == IndexScanState.Running
                && scan.StartedAt >= abandonedBefore))
            .OrderBy(change => change.RequestedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (candidate is null)
        {
            return null;
        }

        var claimed = await context.Set<Change>()
            .Where(change => change.Id == candidate.Id
                && change.State == candidate.State
                && change.StartedAt == candidate.StartedAt
                && !context.Set<Change>().Any(other => other.Id != candidate.Id
                    && other.TenantId == candidate.TenantId
                    && other.UserId == candidate.UserId
                    && other.State == ChangeState.Running
                    && other.StartedAt >= abandonedBefore)
                && !context.Set<IndexScan>().Any(scan => scan.TenantId == candidate.TenantId
                    && scan.UserId == candidate.UserId
                    && scan.State == IndexScanState.Running
                    && scan.StartedAt >= abandonedBefore))
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(change => change.State, ChangeState.Running)
                    .SetProperty(change => change.StartedAt, now),
                cancellationToken);

        return claimed == 1
            ? new ClaimedChange(candidate.Id, candidate.TenantId, candidate.UserId, now)
            : null;
    }

    /// <summary>
    /// Moves the lease forward, proving to the abandoned-Change recovery that this process is
    /// still here. False means the row is no longer this process's to write — stop writing tasks.
    /// </summary>
    internal async Task<bool> RenewLeaseAsync(ClaimedChange change, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);

        var now = timeProvider.GetUtcNow();

        var renewed = await OwnedRow(change)
            .ExecuteUpdateAsync(
                set => set.SetProperty(row => row.StartedAt, now),
                cancellationToken);

        if (renewed == 1)
        {
            change.Lease = now;
            return true;
        }

        return false;
    }

    /// <summary>Whether somebody has asked this run to stop. Read from the row, not from a cached entity.</summary>
    internal Task<bool> IsCancelRequestedAsync(ClaimedChange change, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);

        return context.Set<Change>()
            .AsNoTracking()
            .Where(row => row.Id == change.Id)
            .Select(row => row.CancelRequested)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>Ends the run. False means the lease was lost and somebody else owns the row now.</summary>
    internal async Task<bool> FinishAsync(
        ClaimedChange change,
        ChangeState state,
        FailureCode failureCode,
        string? failureReason,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);

        var completedAt = timeProvider.GetUtcNow();
        var stored = failureReason is { Length: > StorageConventions.FailureReasonLength }
            ? failureReason[..StorageConventions.FailureReasonLength]
            : failureReason;

        var finished = await OwnedRow(change)
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(row => row.State, state)
                    .SetProperty(row => row.FailureCode, failureCode)
                    .SetProperty(row => row.FailureReason, stored)
                    .SetProperty(row => row.CompletedAt, completedAt),
                cancellationToken);

        return finished == 1;
    }

    /// <summary>
    /// Hands a claimed Change back to the queue untouched — the shutdown path. Its plan rows
    /// already say what was written, so the next worker to claim it carries on from there rather
    /// than starting over.
    /// </summary>
    internal async Task<bool> ReleaseAsync(ClaimedChange change, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);

        var released = await OwnedRow(change)
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(row => row.State, ChangeState.Pending)
                    .SetProperty(row => row.StartedAt, (DateTimeOffset?)null),
                cancellationToken);

        return released == 1;
    }

    /// <summary>
    /// Deletes finished Changes past their retention, taking their plan rows and journals with
    /// them by cascade. Thirty days rather than the scan queue's seven, because this is the only
    /// record of a task's previous title — and once it is gone, so is undo.
    /// </summary>
    internal Task<int> PurgeFinishedAsync(TimeSpan retention, CancellationToken cancellationToken)
    {
        var cutoff = timeProvider.GetUtcNow() - retention;

        return context.Set<Change>()
            .Where(change => change.State != ChangeState.Pending
                && change.State != ChangeState.Running
                && change.RequestedAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken);
    }

    /// <summary>The row, if this process still holds its lease.</summary>
    private IQueryable<Change> OwnedRow(ClaimedChange change) =>
        context.Set<Change>()
            .Where(row => row.Id == change.Id
                && row.State == ChangeState.Running
                && row.StartedAt == change.Lease);
}
