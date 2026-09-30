using Microsoft.EntityFrameworkCore;
using TodoWerk.Application.Indexing;
using TodoWerk.Domain.Indexing;
using TodoWerk.Infrastructure.Persistence;
using TodoWerk.Application.Abstractions.Authentication;
using TodoWerk.Application.Abstractions.Indexing;
using TodoWerk.Domain.Changes;
using TodoWerk.Domain.Failures;
using TodoWerk.Domain.Onboarding;

namespace TodoWerk.Infrastructure.Indexing.Persistence;

/// <summary>
/// The scan queue, which is a table — and this class is the only thing that writes its rows'
/// lifecycle. Asking twice while one is in flight is a no-op rather than a second scan; claiming
/// is one conditional update so two processes get one winner; and every write a claimant makes
/// afterwards is conditional on the lease it claimed under, so a process that lost a row to the
/// abandoned-scan recovery finds out instead of finishing someone else's work.
/// </summary>
internal sealed class IndexScanScheduler(TodoWerkDbContext context, TimeProvider timeProvider) : IIndexScanScheduler
{
    public async Task<bool> RequestScanAsync(
        IndexUser user,
        IndexScanMode mode,
        string? taskListId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);

        // Only a scan that already covers what is being asked for makes this a no-op. Covering
        // means at least as much reading — a full pass covers a delta, and a whole-index pass
        // covers one list — because the alternative silently swallows the request: the scheduled
        // delta sync runs every half hour, and a user pressing "read everything again" inside that
        // window would be told it was queued and then never get it.
        var covered = await context.Set<IndexScan>()
            .AnyAsync(
                scan => scan.TenantId == user.TenantId
                    && scan.UserId == user.UserId
                    && (scan.State == IndexScanState.Pending || scan.State == IndexScanState.Running)
                    && (scan.Mode == IndexScanMode.Full || mode == IndexScanMode.Delta)
                    && (scan.TaskListId == null || scan.TaskListId == taskListId),
                cancellationToken);

        if (covered)
        {
            return false;
        }

        context.Add(IndexScan.Request(user.TenantId, user.UserId, mode, taskListId, timeProvider.GetUtcNow()));

        await context.SaveChangesAsync(cancellationToken);

        return true;
    }

    /// <summary>
    /// Queues a delta sync for everyone whose index has aged past <paramref name="syncInterval"/>,
    /// who has not been tried lately, and who has signed in inside <paramref name="idleAfter"/>.
    /// This is the bound on how stale the inventory can be for somebody who is present — an owned
    /// index is stale by definition (ADR-0003), and this says by how much.
    /// <para>
    /// "Tried lately" is read off the scan rows themselves, not only off the per-list states: a
    /// scan can fail before it touches a single list — no token, no list of lists — and going by
    /// the list timestamps alone would make that user due again the instant the worker next
    /// looks. One broken account would then be scanned every poll interval rather than every
    /// sync interval: a retry loop against Graph, dressed as a schedule.
    /// </para>
    /// <para>
    /// "Present" is read off the Tenant Member record, which only a human sign-in writes. Without
    /// that bound, everybody who ever signed in is synced every half hour forever, and under the
    /// Hosted Service every such sync asks the entitlement authority (ADR-0001) about them first —
    /// one <c>periodic</c> resolve per idle person per interval, for a tenant nobody is in, and a
    /// lapsed tenant that never goes quiet on the portal's side. Only this schedule is gated: a
    /// scan somebody asks for, and the follow-up scan a Change queues, are human-initiated and
    /// queue exactly as before.
    /// </para>
    /// </summary>
    /// <returns>How many scans were queued.</returns>
    internal async Task<int> ScheduleDueSyncsAsync(
        TimeSpan syncInterval,
        TimeSpan idleAfter,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var dueBefore = now - syncInterval;
        var presentSince = now - idleAfter;

        // One statement: the per-list dueness, the per-user exclusions and the presence check
        // travel to SQL together rather than as table reads differenced in memory.
        var due = await context.Set<TaskListIndexState>()
            .Where(state =>
                (state.LastSuccessfulSyncAt == null || state.LastSuccessfulSyncAt < dueBefore)
                && (state.LastAttemptAt == null || state.LastAttemptAt < dueBefore))
            .Where(state => !context.Set<IndexScan>().Any(scan =>
                scan.TenantId == state.TenantId
                && scan.UserId == state.UserId
                && (scan.State == IndexScanState.Pending
                    || scan.State == IndexScanState.Running
                    || scan.RequestedAt > dueBefore)))
            // Somebody is present if the row that still names them carries a sign-in inside the
            // window. An anonymised member (`UserId` null, ADR-0009) and a state with no member
            // row at all — index rows predating the Tenant Member table, with no sign-in since —
            // are idle by construction. Nothing is needed to bring somebody back: the sign-in
            // stamp lands in `OnTokenValidated` before their first page renders, so the next poll
            // tick queues the overdue sync, and the freshness indicator (ADR-0003) covers the
            // seconds in between. The Onboarding module is named here rather than reached for
            // through a port for the reason the claim below names `Change`: one database, and one
            // record that answers whether anybody is using what this schedule would keep fresh.
            .Where(state => context.Set<TenantMember>().Any(member =>
                member.TenantId == state.TenantId
                && member.UserId == state.UserId
                && member.LastSignedInAt >= presentSince))
            .Select(state => new { state.TenantId, state.UserId })
            .Distinct()
            .ToListAsync(cancellationToken);

        if (due.Count == 0)
        {
            return 0;
        }

        foreach (var user in due)
        {
            context.Add(IndexScan.Request(user.TenantId, user.UserId, IndexScanMode.Delta, taskListId: null, now));
        }

        await context.SaveChangesAsync(cancellationToken);

        return due.Count;
    }

    /// <summary>
    /// Takes the oldest waiting scan, if the claim wins. The update is conditional on the state
    /// the candidate was read in, so two processes racing for the same row produce one winner and
    /// one no-op rather than the same mailbox scanned twice. It also refuses to claim while
    /// another of the same user's rows is live — two queued rows for one user must run in turn,
    /// never at once, because two runners would double-count every Occurrence they both write.
    /// <para>
    /// A scan left <c>Running</c> longer than <paramref name="scanTimeout"/> without a lease
    /// renewal is treated as abandoned — the process that claimed it is gone — and is claimable
    /// again.
    /// </para>
    /// <para>
    /// It also refuses while that user has a Change running, which is the same exclusion the
    /// Change queue makes in the other direction (ADR-0006). Only a Change that is <em>running</em>
    /// blocks a scan: one merely waiting would otherwise hold the index still until somebody
    /// noticed, and a queued Change already gets its turn by preempting the scan at a page
    /// boundary.
    /// </para>
    /// </summary>
    internal async Task<ClaimedScan?> ClaimNextAsync(TimeSpan scanTimeout, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var abandonedBefore = now - scanTimeout;

        var candidate = await context.Set<IndexScan>()
            .AsNoTracking()
            .Where(scan => scan.State == IndexScanState.Pending
                || (scan.State == IndexScanState.Running && scan.StartedAt < abandonedBefore))
            // A user with a scan genuinely running is skipped here rather than blocking at the
            // claim: their other rows are the oldest in the queue while that scan runs, and
            // stopping on one would hold up every other user's scan behind one live lease.
            .Where(scan => !context.Set<IndexScan>().Any(other => other.Id != scan.Id
                && other.TenantId == scan.TenantId
                && other.UserId == scan.UserId
                && other.State == IndexScanState.Running
                && other.StartedAt >= abandonedBefore))
            .Where(scan => !context.Set<Change>().Any(change => change.TenantId == scan.TenantId
                && change.UserId == scan.UserId
                && change.State == ChangeState.Running
                && change.StartedAt >= abandonedBefore))
            .OrderBy(scan => scan.RequestedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (candidate is null)
        {
            return null;
        }

        var claimed = await context.Set<IndexScan>()
            .Where(scan => scan.Id == candidate.Id
                && scan.State == candidate.State
                && scan.StartedAt == candidate.StartedAt
                && !context.Set<IndexScan>().Any(other => other.Id != candidate.Id
                    && other.TenantId == candidate.TenantId
                    && other.UserId == candidate.UserId
                    && other.State == IndexScanState.Running
                    && other.StartedAt >= abandonedBefore)
                && !context.Set<Change>().Any(change => change.TenantId == candidate.TenantId
                    && change.UserId == candidate.UserId
                    && change.State == ChangeState.Running
                    && change.StartedAt >= abandonedBefore))
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(scan => scan.State, IndexScanState.Running)
                    .SetProperty(scan => scan.StartedAt, now),
                cancellationToken);

        return claimed == 1
            ? new ClaimedScan(
                candidate.Id,
                candidate.TenantId,
                candidate.UserId,
                candidate.Mode,
                candidate.TaskListId,
                candidate.WasPreempted,
                now)
            : null;
    }

    /// <summary>
    /// Whether this user has confirmed a Change that is waiting for the scan to get out of its way.
    /// <para>
    /// Asked at a page boundary rather than enforced in a claim, because it does not have to be
    /// atomic: a Change that arrives a page later simply waits a page longer. The Changes module
    /// is named here rather than reached for through a port for the same reason its own claim
    /// names <see cref="IndexScan"/> — one database, two queues, and one user who must not have
    /// both running at once (ADR-0006).
    /// </para>
    /// </summary>
    internal Task<bool> HasWaitingChangeAsync(ClaimedScan scan, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scan);

        return context.Set<Change>()
            .AnyAsync(
                change => change.TenantId == scan.TenantId
                    && change.UserId == scan.UserId
                    && change.State == ChangeState.Pending,
                cancellationToken);
    }

    /// <summary>
    /// Hands the row back so a waiting Change can run, and marks it so it will not stand aside a
    /// second time — otherwise somebody confirming Changes back to back would preempt their own
    /// index forever and it would never finish a pass (ADR-0006). Its per-list progress means it
    /// resumes having lost nothing.
    /// </summary>
    internal async Task<bool> ReleaseForPreemptionAsync(ClaimedScan scan, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scan);

        var released = await OwnedRow(scan)
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(row => row.State, IndexScanState.Pending)
                    .SetProperty(row => row.StartedAt, (DateTimeOffset?)null)
                    .SetProperty(row => row.WasPreempted, true),
                cancellationToken);

        return released == 1;
    }

    /// <summary>
    /// Moves the lease forward, proving to the abandoned-scan recovery that this process is still
    /// here. False means the row is no longer this process's to write — stop scanning.
    /// </summary>
    internal async Task<bool> RenewLeaseAsync(ClaimedScan scan, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scan);

        var now = timeProvider.GetUtcNow();

        var renewed = await OwnedRow(scan)
            .ExecuteUpdateAsync(
                set => set.SetProperty(row => row.StartedAt, now),
                cancellationToken);

        if (renewed == 1)
        {
            scan.Lease = now;
            return true;
        }

        return false;
    }

    /// <summary>False means the lease was lost and somebody else owns the row now.</summary>
    internal async Task<bool> CompleteAsync(ClaimedScan scan, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scan);

        var completedAt = timeProvider.GetUtcNow();

        var completed = await OwnedRow(scan)
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(row => row.State, IndexScanState.Completed)
                    .SetProperty(row => row.CompletedAt, completedAt),
                cancellationToken);

        return completed == 1;
    }

    /// <summary>False means the lease was lost and somebody else owns the row now.</summary>
    internal async Task<bool> FailAsync(
        ClaimedScan scan,
        string reason,
        FailureCode code,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scan);
        ArgumentNullException.ThrowIfNull(reason);

        var failedAt = timeProvider.GetUtcNow();
        var stored = reason.Length <= StorageConventions.FailureReasonLength
            ? reason
            : reason[..StorageConventions.FailureReasonLength];

        var failed = await OwnedRow(scan)
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(row => row.State, IndexScanState.Failed)
                    .SetProperty(row => row.FailureReason, stored)
                    .SetProperty(row => row.FailureCode, code)
                    .SetProperty(row => row.CompletedAt, failedAt),
                cancellationToken);

        return failed == 1;
    }

    /// <summary>
    /// Hands a claimed scan back to the queue untouched — the shutdown path. The next worker to
    /// poll picks it up immediately instead of waiting out the abandonment timeout.
    /// </summary>
    internal async Task<bool> ReleaseAsync(ClaimedScan scan, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scan);

        var released = await OwnedRow(scan)
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(row => row.State, IndexScanState.Pending)
                    .SetProperty(row => row.StartedAt, (DateTimeOffset?)null),
                cancellationToken);

        return released == 1;
    }

    /// <summary>
    /// Deletes finished rows past their retention. The queue is a queue, not an audit log; left
    /// alone it grows by every scheduled sync forever.
    /// </summary>
    internal Task<int> PurgeFinishedAsync(TimeSpan retention, CancellationToken cancellationToken)
    {
        var cutoff = timeProvider.GetUtcNow() - retention;

        return context.Set<IndexScan>()
            .Where(scan => (scan.State == IndexScanState.Completed || scan.State == IndexScanState.Failed)
                && scan.RequestedAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken);
    }

    /// <summary>The row, if this process still holds its lease.</summary>
    private IQueryable<IndexScan> OwnedRow(ClaimedScan scan) =>
        context.Set<IndexScan>()
            .Where(row => row.Id == scan.Id
                && row.State == IndexScanState.Running
                && row.StartedAt == scan.Lease);
}
