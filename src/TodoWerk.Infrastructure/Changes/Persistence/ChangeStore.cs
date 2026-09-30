using Microsoft.EntityFrameworkCore;
using TodoWerk.Application.Abstractions.Authentication;
using TodoWerk.Application.Changes;
using TodoWerk.Domain.Changes;
using TodoWerk.Infrastructure.Persistence;

namespace TodoWerk.Infrastructure.Changes.Persistence;

/// <summary>
/// The Change tables as the request path sees them. The worker's claim, lease and completion
/// writes live in <see cref="ChangeQueue"/> instead: those are conditional updates against a lease
/// and must not be reachable from a handler.
/// </summary>
internal sealed class ChangeStore(TodoWerkDbContext context, TimeProvider timeProvider) : IChangeStore
{
    /// <summary>
    /// How much history the Workbench is handed. Thirty days of Changes is usually a handful, but
    /// it is not bounded by anything except how often somebody presses the button — and the queue
    /// is polled while a Change runs.
    /// </summary>
    private const int HistoryLimit = 50;

    public Task<bool> HasUnfinishedChangeAsync(IndexUser user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);

        return context.Set<Change>()
            .AnyAsync(
                change => change.TenantId == user.TenantId
                    && change.UserId == user.UserId
                    && (change.State == ChangeState.Pending || change.State == ChangeState.Running),
                cancellationToken);
    }

    public async Task<Guid> AddAsync(IndexUser user, ConfirmedChangePlan plan, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(plan);

        // The two Marker shapes are built by their own factories rather than squeezed through the
        // Hashtag one: they carry no target Spelling and they do carry the Markers they write, and
        // a single factory taking both would let a Rename be created with Markers attached. They
        // are two factories rather than one for the same reason they are two Kinds — a Remove
        // records no scope keys at all, because its scope is the Markers themselves.
        var change = plan.Kind switch
        {
            ChangeKind.ApplyMarkers => Change.PlanMarkers(
                user.TenantId,
                user.UserId,
                plan.SourceKeys,
                plan.AppliedMarkers,
                plan.Tasks.Count,
                timeProvider.GetUtcNow()),
            ChangeKind.RemoveMarkers => Change.PlanRemoval(
                user.TenantId,
                user.UserId,
                plan.AppliedMarkers,
                plan.Tasks.Count,
                timeProvider.GetUtcNow()),
            _ => Change.Plan(
                user.TenantId,
                user.UserId,
                plan.SourceKeys,
                plan.TargetSpelling,
                plan.Kind,
                plan.AppliedMarkers,
                plan.Tasks.Count,
                timeProvider.GetUtcNow()),
        };

        context.Add(change);

        for (var index = 0; index < plan.Tasks.Count; index++)
        {
            var task = plan.Tasks[index];

            context.Add(ChangePlanItem.Plan(
                user.TenantId,
                user.UserId,
                change.Id,
                index,
                task.TaskListId,
                task.GraphTaskId,
                task.CurrentTitle,
                task.NewTitle));
        }

        // One save: a Change without its plan is a row the worker would claim and find nothing to
        // do with, and it would report that as a completed Change that wrote nothing.
        await context.SaveChangesAsync(cancellationToken);

        return change.Id;
    }

    public async Task<IReadOnlyList<ChangeRecord>> ReadQueueAsync(IndexUser user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);

        var changes = await context.Set<Change>()
            .AsNoTracking()
            .Where(change => change.TenantId == user.TenantId && change.UserId == user.UserId)
            .OrderByDescending(change => change.RequestedAt)
            .Take(HistoryLimit)
            .ToListAsync(cancellationToken);

        if (changes.Count == 0)
        {
            return [];
        }

        var ids = changes.ConvertAll(change => change.Id);

        return await ToRecordsAsync(user, changes, ids, cancellationToken);
    }

    public async Task<ChangeRecord?> FindAsync(IndexUser user, Guid changeId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);

        var change = await context.Set<Change>()
            .AsNoTracking()
            .FirstOrDefaultAsync(
                row => row.Id == changeId && row.TenantId == user.TenantId && row.UserId == user.UserId,
                cancellationToken);

        if (change is null)
        {
            return null;
        }

        var records = await ToRecordsAsync(user, [change], [changeId], cancellationToken);

        return records[0];
    }

    public async Task<bool> RequestCancelAsync(IndexUser user, Guid changeId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);

        // Set on the row rather than through the entity, so it lands whatever the runner is doing
        // to its own copy: the two are in different processes as often as not.
        var flagged = await context.Set<Change>()
            .Where(change => change.Id == changeId
                && change.TenantId == user.TenantId
                && change.UserId == user.UserId
                && (change.State == ChangeState.Pending || change.State == ChangeState.Running))
            .ExecuteUpdateAsync(
                set => set.SetProperty(change => change.CancelRequested, true),
                cancellationToken);

        return flagged == 1;
    }

    public async Task<Guid?> AddUndoAsync(IndexUser user, Guid changeId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);

        var original = await context.Set<Change>()
            .FirstOrDefaultAsync(
                change => change.Id == changeId
                    && change.TenantId == user.TenantId
                    && change.UserId == user.UserId,
                cancellationToken);

        if (original is null || original.IsUndo)
        {
            return null;
        }

        // Re-checked here rather than trusted from the caller: two tabs pressing Undo would both
        // pass the handler's check, and a second undo would restore titles the first already
        // restored — writing the old title over the older one.
        var alreadyUndone = await context.Set<Change>()
            .AnyAsync(change => change.UndoOfChangeId == changeId, cancellationToken);

        if (alreadyUndone)
        {
            return null;
        }

        var journal = await context.Set<ChangeJournalEntry>()
            .AsNoTracking()
            .Where(entry => entry.ChangeId == changeId)
            // Newest first: an undo unwinds in the order the writes were made, which is the only
            // order that is defensible without knowing whether two of them touched one task.
            .OrderByDescending(entry => entry.WrittenAt)
            .ThenByDescending(entry => entry.Id)
            .ToListAsync(cancellationToken);

        if (journal.Count == 0)
        {
            return null;
        }

        var undo = Change.Undo(original, journal.Count, timeProvider.GetUtcNow());

        context.Add(undo);

        for (var index = 0; index < journal.Count; index++)
        {
            var entry = journal[index];

            // The plan of an undo is exact rather than advisory: what TodoWerk wrote is what the
            // task must still say, and what it read is what goes back.
            context.Add(ChangePlanItem.Plan(
                user.TenantId,
                user.UserId,
                undo.Id,
                index,
                entry.TaskListId,
                entry.GraphTaskId,
                entry.TitleAfter,
                entry.TitleBefore));
        }

        await context.SaveChangesAsync(cancellationToken);

        return undo.Id;
    }

    /// <summary>
    /// Adds what the Change rows do not carry: how far the plan has got, and whether an undo of
    /// this Change already exists. Two queries for the whole page rather than two per row.
    /// </summary>
    private async Task<List<ChangeRecord>> ToRecordsAsync(
        IndexUser user,
        List<Change> changes,
        List<Guid> ids,
        CancellationToken cancellationToken)
    {
        var progress = await context.Set<ChangePlanItem>()
            .AsNoTracking()
            .Where(item => item.TenantId == user.TenantId
                && item.UserId == user.UserId
                && ids.Contains(item.ChangeId))
            .GroupBy(item => new { item.ChangeId, item.Status })
            .Select(group => new PlanProgress(group.Key.ChangeId, group.Key.Status, group.Count()))
            .ToListAsync(cancellationToken);

        var undone = await context.Set<Change>()
            .AsNoTracking()
            .Where(change => change.UndoOfChangeId != null && ids.Contains(change.UndoOfChangeId.Value))
            .Select(change => change.UndoOfChangeId!.Value)
            .ToListAsync(cancellationToken);

        var undoneIds = undone.ToHashSet();

        return
        [
            .. changes.Select(change => new ChangeRecord(
                change.Id,
                change.Kind,
                change.SourceKeys,
                change.TargetSpelling,
                change.AppliedMarkers,
                change.State,
                change.PlannedTaskCount,
                Count(progress, change.Id, ChangePlanItemStatus.Written),
                Count(progress, change.Id, ChangePlanItemStatus.Skipped),
                Count(progress, change.Id, ChangePlanItemStatus.Failed),
                change.CancelRequested,
                change.RequestedAt,
                change.CompletedAt,
                change.FailureCode,
                change.FailureReason,
                change.UndoOfChangeId,
                undoneIds.Contains(change.Id))),
        ];

        static int Count(List<PlanProgress> progress, Guid changeId, ChangePlanItemStatus status)
        {
            var total = 0;

            foreach (var row in progress)
            {
                if (row.ChangeId == changeId && row.Status == status)
                {
                    total += row.Count;
                }
            }

            return total;
        }
    }

    /// <summary>How many of one Change's planned tasks ended one way. Named so the grouping can be projected into it.</summary>
    private sealed record PlanProgress(Guid ChangeId, ChangePlanItemStatus Status, int Count);
}
