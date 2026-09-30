using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TodoWerk.Application.Indexing;
using TodoWerk.Domain.Indexing;
using TodoWerk.Infrastructure.Indexing.Persistence;
using TodoWerk.Infrastructure.Persistence;
using TodoWerk.Domain.Hashtags;
using TodoWerk.Infrastructure.Graph;
using TodoWerk.Application.Abstractions.Authentication;
using TodoWerk.Domain.Failures;

namespace TodoWerk.Infrastructure.Indexing;

/// <summary>
/// Runs one claimed scan: reconciles the user's lists, then reads each one into the index.
/// <para>
/// Never called from a request. A first scan over a real mailbox is minutes of paging, and the
/// index it writes is the thing the Workbench reads — so it runs in the background, records what
/// it has done per list as it goes, renews its lease as proof of life, and leaves behind enough
/// state for a restarted process to carry on rather than start over.
/// </para>
/// </summary>
internal sealed class IndexScanRunner(
    TodoWerkDbContext context,
    IndexScanScheduler scheduler,
    ITaskListsReader taskListsReader,
    ITodoTaskReader taskReader,
    TimeProvider timeProvider,
    ILogger<IndexScanRunner> logger)
{
    /// <summary>
    /// How many rows one deletion statement names. Small enough that the parameter list stays a
    /// parameter list, large enough that a purge of a whole vanished list is a handful of round
    /// trips rather than thousands.
    /// </summary>
    private const int DeleteChunkSize = 500;

    /// <summary>
    /// What the user is told when the scan of a list ended in an exception. Everything this
    /// catch-all can catch has to fit the sentence, so it names no cause and says outright that it
    /// has none: a read that failed is already worded by <see cref="GraphErrors"/> further up,
    /// which leaves this branch the one where the cause is genuinely unknown. Claiming "this list
    /// could not be read" here would send somebody looking at Graph, at permissions and at the list
    /// itself for what may be a failed write. Worded like the scan-level catch-all in
    /// <see cref="IndexScanBackgroundService"/>, because it is the same admission one level down.
    /// </summary>
    private const string UnknownFailureReason =
        "TodoWerk hit an unexpected error while scanning this list. It will try again.";

    private enum ListOutcome
    {
        Completed,
        Failed,

        /// <summary>Graph expired the delta token; the list has to be read in full instead.</summary>
        ResyncRequired,

        /// <summary>The scan row belongs to another process now. Stop writing, complete nothing.</summary>
        LeaseLost,

        /// <summary>
        /// A Change is waiting for this user. The row has been handed back at a page boundary and
        /// will be claimed again once the Change has run; per-list progress means it lost nothing.
        /// </summary>
        Preempted,
    }

    public async Task RunAsync(ClaimedScan scan, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scan);

        var user = new IndexUser(scan.TenantId, scan.UserId);

        var lists = await taskListsReader.GetTaskListsAsync(user, cancellationToken);

        if (lists.IsFailure)
        {
            // Nothing to scan and nothing to record per list: the failure is the scan's own. The
            // scan row carries it, and because the row is recent the scheduler will not queue this
            // user again until the sync interval has passed.
            await scheduler.FailAsync(
                scan,
                lists.Error.Description,
                GraphErrors.CodeFor(lists.Error),
                cancellationToken);
            return;
        }

        // Graph answering "no lists at all" for an account that had them is not a user who
        // deleted everything — To Do always keeps a default list. Reconciling against it would
        // delete the whole index, and because the schedule works out who is due from the very
        // rows just deleted, nothing would ever scan this user again: they would have to notice
        // and press the button themselves.
        if (lists.Value.Count == 0 && await HasIndexedListsAsync(user, cancellationToken))
        {
            await scheduler.FailAsync(
                scan,
                "Microsoft To Do returned no task lists. TodoWerk kept the index it already had.",
                FailureCode.Unknown,
                cancellationToken);
            return;
        }

        var states = await ReconcileListsAsync(user, lists.Value, cancellationToken);

        var targets = scan.TaskListId is null
            ? states
            : [.. states.Where(state => string.Equals(state.TaskListId, scan.TaskListId, StringComparison.Ordinal))];

        foreach (var state in targets)
        {
            // A list that fails, fails alone. One throttled or deleted list must not cost the
            // user the other nine, and the failure is recorded where the UI can show it.
            try
            {
                var outcome = await ScanListAsync(user, state, scan, cancellationToken);

                if (outcome is ListOutcome.LeaseLost)
                {
                    logger.LogWarning(
                        "The scan for tenant {TenantId} lost its lease mid-run; another process owns it now.",
                        scan.TenantId);

                    return;
                }

                if (outcome is ListOutcome.Preempted)
                {
                    // The row is already back in the queue. Completing it here would tell the
                    // Workbench the index is current when most of it has not been read.
                    return;
                }
            }
            // Anything but this scan actually being cancelled: a list that fails, fails alone.
            // The cancellation shape matters — an HTTP timeout arrives as one while nobody asked
            // to stop, and treating that as "we are shutting down" would fail the whole scan and
            // cost the user their other nine lists.
            catch (Exception exception)
                when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                logger.LogError(exception, "Scanning list {TaskListId} failed.", state.TaskListId);

                // The save that threw leaves its change set pending; saving again would replay
                // the exact writes that just failed. Drop them, put the list states back, and
                // record only the failure.
                RecoverTracker(states);

                state.FailScan(UnknownFailureReason, FailureCode.Unknown, timeProvider.GetUtcNow());

                try
                {
                    await context.SaveChangesAsync(cancellationToken);
                }
                catch (Exception saveException) when (saveException is not OperationCanceledException)
                {
                    logger.LogError(
                        saveException,
                        "Recording the failure for list {TaskListId} failed as well.",
                        state.TaskListId);
                }
            }
        }

        if (!await scheduler.CompleteAsync(scan, cancellationToken))
        {
            logger.LogWarning(
                "The scan for tenant {TenantId} finished its work but had already lost its lease.",
                scan.TenantId);
        }
    }

    private Task<bool> HasIndexedListsAsync(IndexUser user, CancellationToken cancellationToken) =>
        context.Set<TaskListIndexState>()
            .AnyAsync(state => state.TenantId == user.TenantId && state.UserId == user.UserId, cancellationToken);

    /// <summary>
    /// Drops every pending write and re-attaches the list states, so the failure that follows is
    /// recorded on a clean tracker — including for the lists still waiting their turn.
    /// </summary>
    private void RecoverTracker(List<TaskListIndexState> states)
    {
        context.ChangeTracker.Clear();

        foreach (var state in states)
        {
            context.Attach(state);
        }
    }

    /// <summary>
    /// Brings the per-list state rows in line with what Graph says the user has: new lists get a
    /// row, renamed ones get their new name, and a list that is gone takes its indexed tasks with
    /// it rather than leaving Occurrences the inventory would still count.
    /// </summary>
    private async Task<List<TaskListIndexState>> ReconcileListsAsync(
        IndexUser user,
        IReadOnlyList<TaskListDto> lists,
        CancellationToken cancellationToken)
    {
        var states = await context.Set<TaskListIndexState>()
            .Where(state => state.TenantId == user.TenantId && state.UserId == user.UserId)
            .ToListAsync(cancellationToken);

        var byListId = states.ToDictionary(state => state.TaskListId, StringComparer.Ordinal);
        var current = new List<TaskListIndexState>(lists.Count);

        foreach (var list in lists)
        {
            if (byListId.TryGetValue(list.Id, out var state))
            {
                state.Rename(list.DisplayName);

                // Already in `current` if Graph repeated the list across pages — reconciled
                // once, not twice, or the second sighting would create a duplicate row.
                if (current.Contains(state))
                {
                    continue;
                }
            }
            else
            {
                state = TaskListIndexState.Create(user.TenantId, user.UserId, list.Id, list.DisplayName);
                context.Add(state);
                byListId[list.Id] = state;
            }

            current.Add(state);
        }

        var live = current.Select(state => state.TaskListId).ToHashSet(StringComparer.Ordinal);

        foreach (var vanished in states.Where(state => !live.Contains(state.TaskListId)))
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation(
                    "List {TaskListId} is gone from Graph; dropping it from the index.",
                    vanished.TaskListId);
            }

            context.Remove(vanished);
            await DeleteTasksAsync(user, vanished.TaskListId, cancellationToken);
        }

        await context.SaveChangesAsync(cancellationToken);

        return current;
    }

    private async Task<ListOutcome> ScanListAsync(
        IndexUser user,
        TaskListIndexState state,
        ClaimedScan scan,
        CancellationToken cancellationToken)
    {
        var incremental = scan.Mode is IndexScanMode.Delta && state.CanSyncIncrementally;

        var outcome = await ReadListAsync(user, state, scan, incremental, cancellationToken);

        // Graph expiring a delta token is routine, not an incident: drop the link and read the
        // list in full, without waiting for anyone to notice.
        if (outcome is ListOutcome.ResyncRequired)
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation(
                    "Graph expired the delta token for list {TaskListId}; falling back to a full scan.",
                    state.TaskListId);
            }

            state.RequireFullResync();
            outcome = await ReadListAsync(user, state, scan, incremental: false, cancellationToken);

            if (outcome is ListOutcome.ResyncRequired)
            {
                // The full read came back 410 as well. Record it — the alternative parks the
                // list on "Scanning" with no reason on it and a spinner that never stops. The
                // sentence lives in GraphErrors with every other thing Graph does to TodoWerk, so
                // that it arrives with a code the Workbench can switch on.
                state.FailScan(
                    GraphErrors.ResyncRequiredAgain.Description,
                    GraphErrors.CodeFor(GraphErrors.ResyncRequiredAgain),
                    timeProvider.GetUtcNow());
                await context.SaveChangesAsync(cancellationToken);

                return ListOutcome.Failed;
            }
        }

        return outcome;
    }

    private async Task<ListOutcome> ReadListAsync(
        IndexUser user,
        TaskListIndexState state,
        ClaimedScan scan,
        bool incremental,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();

        state.BeginScan(now);
        await context.SaveChangesAsync(cancellationToken);

        var link = incremental ? state.DeltaLink : null;
        var seen = incremental ? null : new HashSet<string>(StringComparer.Ordinal);
        var indexed = 0;
        string? deltaLink = null;

        while (true)
        {
            var page = await taskReader.ReadTasksAsync(user, state.TaskListId, link, cancellationToken);

            if (page.IsFailure)
            {
                if (page.Error == GraphErrors.ResyncRequired)
                {
                    return ListOutcome.ResyncRequired;
                }

                // A 404 here is not the gateway's ordinary one. Graph returned this list from
                // GET /me/todo/lists in this same pass — that listing is the only reason the
                // state row exists at all — so "no longer has this item" would send whoever reads
                // it looking for a list that is still there. The fact that makes the
                // right sentence right is known here and nowhere else, which is where CONTRIBUTING
                // section "What a failure is allowed to say" puts the wording.
                var error = page.Error == GraphErrors.NotFound ? GraphErrors.ListNotReadable : page.Error;

                state.FailScan(error.Description, GraphErrors.CodeFor(error), timeProvider.GetUtcNow());
                await context.SaveChangesAsync(cancellationToken);

                return ListOutcome.Failed;
            }

            indexed += await ApplyPageAsync(user, state.TaskListId, page.Value.Tasks, seen, cancellationToken);

            // Written every page rather than at the end: a first scan is long enough to be
            // interrupted by a deploy, and the UI has a number to show while it runs.
            state.RecordProgress(indexed);
            await context.SaveChangesAsync(cancellationToken);

            // The page is in the database; the tracker does not need to keep carrying it. Left
            // attached, a ten-thousand-task list would make every later save walk every earlier
            // page again.
            DetachIndexedEntities();

            // Proof of life. A pass over a large throttled mailbox can outlast the abandonment
            // timeout while making perfectly good progress; the renewed lease is what stops the
            // recovery from handing the scan to a second process mid-run.
            if (!await scheduler.RenewLeaseAsync(scan, cancellationToken))
            {
                return ListOutcome.LeaseLost;
            }

            deltaLink = page.Value.DeltaLink;

            if (deltaLink is not null || page.Value.NextLink is null)
            {
                break;
            }

            // A confirmed Change is meant to start immediately, and exclusivity alone would make
            // that false — a first scan is minutes long. So the scan stands aside here, at a point
            // where everything it has read is committed, its per-list progress is written, and
            // there is more of this list still to read. Only there: standing aside on the last page
            // would throw the whole pass away, because the list would never be completed, never get
            // its delta link, and — on a first scan — be excluded from the plan of the very Change
            // it made way for. Once only, too: a scan already preempted runs to completion instead,
            // or a user queueing Changes in a row would starve their own index (ADR-0006).
            if (!scan.WasPreempted && await scheduler.HasWaitingChangeAsync(scan, cancellationToken))
            {
                if (await scheduler.ReleaseForPreemptionAsync(scan, cancellationToken))
                {
                    if (logger.IsEnabled(LogLevel.Information))
                    {
                        logger.LogInformation(
                            "Standing the scan of list {TaskListId} aside for a queued change; it will resume after.",
                            state.TaskListId);
                    }

                    return ListOutcome.Preempted;
                }

                // The row was not ours to hand back, which means it is not ours to keep reading.
                return ListOutcome.LeaseLost;
            }

            link = page.Value.NextLink;
        }

        var completedAt = timeProvider.GetUtcNow();

        if (seen is not null)
        {
            // A full pass saw every task the list has, so anything left over was deleted while
            // TodoWerk was not looking. Deleting the task takes its Occurrences with it.
            await DeleteTasksMissingFromAsync(user, state.TaskListId, seen, cancellationToken);
            state.CompleteFullScan(deltaLink, completedAt);
        }
        else
        {
            state.CompleteDeltaSync(deltaLink, completedAt);
        }

        await context.SaveChangesAsync(cancellationToken);

        return ListOutcome.Completed;
    }

    /// <summary>Applies one page and returns how many live tasks it carried.</summary>
    private async Task<int> ApplyPageAsync(
        IndexUser user,
        string taskListId,
        IReadOnlyList<TodoTaskDto> tasks,
        HashSet<string>? seen,
        CancellationToken cancellationToken)
    {
        if (tasks.Count == 0)
        {
            return 0;
        }

        var ids = tasks.Select(task => task.Id).ToArray();

        // The `Contains` is evaluated by SQL and the dictionary is read in C#, so the two have to
        // mean the same thing by "same id". They do because the column is binary-collated —
        // under a case-insensitive one the query returns a row this lookup then misses, the task
        // is treated as new, and the insert dies on the unique index.
        var existing = await context.Set<IndexedTask>()
            .Where(task => task.TenantId == user.TenantId
                && task.UserId == user.UserId
                && ids.Contains(task.GraphTaskId))
            .ToDictionaryAsync(task => task.GraphTaskId, StringComparer.Ordinal, cancellationToken);

        // Occurrences this page has already written for a task, still only in the change tracker.
        // Graph does not promise a page mentions a task once, so a second sighting replaces what
        // the first one added — otherwise both sets are inserted and the Hashtag is counted twice
        // for one task, which is the number the Canonical Spelling is chosen by.
        var addedThisPage = new Dictionary<Guid, List<HashtagOccurrence>>();
        var restated = new HashSet<Guid>();
        var live = 0;

        foreach (var task in tasks)
        {
            if (task.IsDeleted)
            {
                // Only if the row still belongs to this list. A task moved from list A to list B
                // is A's tombstone and B's addition; when B's delta lands first, the row already
                // says B, and honouring A's tombstone then would delete a task that still exists.
                if (existing.TryGetValue(task.Id, out var removed)
                    && string.Equals(removed.TaskListId, taskListId, StringComparison.Ordinal))
                {
                    context.Remove(removed);
                    existing.Remove(task.Id);

                    // Removing an entity this page only just added detaches it rather than
                    // deleting it, so nothing cascades to the Occurrences added alongside it.
                    // Left behind they would be inserted pointing at a task row that never
                    // arrives, and the whole page's save would fail on the foreign key.
                    if (addedThisPage.Remove(removed.Id, out var orphaned))
                    {
                        foreach (var occurrence in orphaned)
                        {
                            context.Remove(occurrence);
                        }
                    }
                }

                continue;
            }

            live++;
            seen?.Add(task.Id);

            var title = Shorten(task.Title);

            if (!existing.TryGetValue(task.Id, out var indexed))
            {
                indexed = IndexedTask.Create(
                    user.TenantId,
                    user.UserId,
                    taskListId,
                    task.Id,
                    title,
                    task.LastModifiedAt);

                context.Add(indexed);

                // Registered so a repeat of the same task later in this page updates this entity
                // rather than creating a second row with the same Graph id.
                existing[task.Id] = indexed;
                addedThisPage[indexed.Id] = AddOccurrences(user, indexed, task.Title);
                continue;
            }

            var retitled = !string.Equals(indexed.Title, title, StringComparison.Ordinal);

            indexed.Update(taskListId, title, task.LastModifiedAt);

            if (!retitled)
            {
                continue;
            }

            if (addedThisPage.TryGetValue(indexed.Id, out var superseded))
            {
                // This page already wrote Occurrences for this task. Those are the ones to
                // replace; the stored rows, if any, are already on the list to be cleared.
                foreach (var occurrence in superseded)
                {
                    context.Remove(occurrence);
                }
            }
            else
            {
                restated.Add(indexed.Id);
            }

            addedThisPage[indexed.Id] = AddOccurrences(user, indexed, task.Title);
        }

        if (restated.Count > 0)
        {
            // The old Occurrences of a retitled task, cleared in one query rather than one per
            // task. The replacements were added above and are inserted by the same save.
            var stale = await context.Set<HashtagOccurrence>()
                .Where(occurrence => occurrence.TenantId == user.TenantId
                    && occurrence.UserId == user.UserId
                    && restated.Contains(occurrence.IndexedTaskId))
                .ToListAsync(cancellationToken);

            context.RemoveRange(stale);
        }

        return live;
    }

    private List<HashtagOccurrence> AddOccurrences(IndexUser user, IndexedTask task, string? title)
    {
        var added = new List<HashtagOccurrence>();

        foreach (var hashtag in HashtagExtractor.Extract(title))
        {
            var occurrence = HashtagOccurrence.Create(user.TenantId, user.UserId, task.Id, hashtag);
            context.Add(occurrence);
            added.Add(occurrence);
        }

        return added;
    }

    private void DetachIndexedEntities()
    {
        foreach (var entry in context.ChangeTracker.Entries<IndexedTask>().ToList())
        {
            entry.State = EntityState.Detached;
        }

        foreach (var entry in context.ChangeTracker.Entries<HashtagOccurrence>().ToList())
        {
            entry.State = EntityState.Detached;
        }
    }

    /// <summary>Returns how many rows went; the callers do not care, the analyzer does.</summary>
    private Task<int> DeleteTasksAsync(IndexUser user, string taskListId, CancellationToken cancellationToken) =>
        context.Set<IndexedTask>()
            .Where(task => task.TenantId == user.TenantId
                && task.UserId == user.UserId
                && task.TaskListId == taskListId)
            .ExecuteDeleteAsync(cancellationToken);

    /// <summary>
    /// Deletes what a full pass did not see. The stored ids are read and diffed here rather than
    /// shipping every id the pass saw into one giant <c>NOT IN</c> parameter — a large list would
    /// make that parameter megabytes the server must shred, to find the handful of rows that are
    /// usually the answer.
    /// </summary>
    private async Task<int> DeleteTasksMissingFromAsync(
        IndexUser user,
        string taskListId,
        HashSet<string> seen,
        CancellationToken cancellationToken)
    {
        var stored = await context.Set<IndexedTask>()
            .Where(task => task.TenantId == user.TenantId
                && task.UserId == user.UserId
                && task.TaskListId == taskListId)
            .Select(task => new { task.Id, task.GraphTaskId })
            .ToListAsync(cancellationToken);

        var vanished = stored
            .Where(task => !seen.Contains(task.GraphTaskId))
            .Select(task => task.Id)
            .ToList();

        var deleted = 0;

        foreach (var chunk in vanished.Chunk(DeleteChunkSize))
        {
            deleted += await context.Set<IndexedTask>()
                .Where(task => chunk.Contains(task.Id))
                .ExecuteDeleteAsync(cancellationToken);
        }

        return deleted;
    }

    /// <summary>
    /// Titles are stored to show and to compare, never to write back — M2's rename re-reads the
    /// task from Graph before touching it. A title longer than the column is therefore trimmed
    /// rather than allowed to fail the whole scan, and Hashtags are extracted from the full text
    /// first, so nothing beyond the cut is lost from the index. The cut itself backs off one
    /// character rather than split a surrogate pair — half an emoji renders as garbage.
    /// </summary>
    private static string Shorten(string? title)
    {
        if (title is null)
        {
            return string.Empty;
        }

        if (title.Length <= StorageConventions.DisplayTextLength)
        {
            return title;
        }

        var length = char.IsHighSurrogate(title[StorageConventions.DisplayTextLength - 1])
            ? StorageConventions.DisplayTextLength - 1
            : StorageConventions.DisplayTextLength;

        return title[..length];
    }
}
