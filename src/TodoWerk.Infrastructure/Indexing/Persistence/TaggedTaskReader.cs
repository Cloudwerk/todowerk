using Microsoft.EntityFrameworkCore;
using TodoWerk.Application.Abstractions.Authentication;
using TodoWerk.Application.Abstractions.Indexing;
using TodoWerk.Domain.Indexing;
using TodoWerk.Infrastructure.Persistence;

namespace TodoWerk.Infrastructure.Indexing.Persistence;

/// <summary>
/// Answers "which tasks carry these Hashtags" for a caller that is not the inventory — today, the
/// planner behind a Change. It lives in the Indexing module because these are the Indexing
/// module's tables; the caller sees only the shared port (ADR-0006).
/// <para>
/// Every comparison here rides on the binary collations the Hashtag columns carry. The key
/// comparison is what makes <c>#Straße</c> and <c>#Strasse</c> two Hashtags to the database as
/// well as to C#; the Spelling comparison is what makes "not already written this way" mean what
/// it says, which under a case-insensitive collation would answer "none of them" for every casing
/// clean-up there is.
/// </para>
/// </summary>
internal sealed class TaggedTaskReader(TodoWerkDbContext context) : ITaggedTaskReader
{
    private const string NeverScannedReason =
        "TodoWerk has not read this list yet.";

    private const string StillScanningReason =
        "TodoWerk is still reading this list for the first time.";

    private const string NotFinishedReason =
        "TodoWerk has not finished reading this list end to end.";

    public async Task<TaggedTaskSet> ReadTasksTaggedAsync(
        IndexUser user,
        IReadOnlyCollection<string> keys,
        string alreadySpelled,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(keys);

        var excluded = await ReadExcludedListsAsync(user, cancellationToken);

        if (keys.Count == 0 || limit <= 0)
        {
            return new TaggedTaskSet([], 0, excluded);
        }

        var wanted = keys.ToList();

        // A list that has never been read end to end contributes nothing: ADR-0003 forbids writing
        // against a half-scanned list, because its Occurrences are the ones a rename would miss.
        var matching =
            from task in context.Set<IndexedTask>().AsNoTracking()
            join state in context.Set<TaskListIndexState>().AsNoTracking()
                on new { task.TenantId, task.UserId, task.TaskListId }
                equals new { state.TenantId, state.UserId, state.TaskListId }
            where task.TenantId == user.TenantId
                && task.UserId == user.UserId
                && state.LastCompletedScanAt != null
                && context.Set<HashtagOccurrence>().Any(occurrence =>
                    occurrence.IndexedTaskId == task.Id
                    && wanted.Contains(occurrence.Key)
                    && occurrence.Spelling != alreadySpelled)
            orderby state.DisplayName, task.Title, task.GraphTaskId
            select new TaggedTask(task.TaskListId, state.DisplayName, task.GraphTaskId, task.Title);

        // Counted separately from the page, because the ceiling is measured against every matching
        // task and the caller must be able to say "1,412, which is too many" without reading 1,412
        // rows to find out.
        var total = await matching.CountAsync(cancellationToken);

        if (total > limit)
        {
            return new TaggedTaskSet([], total, excluded);
        }

        var tasks = await matching.Take(limit).ToListAsync(cancellationToken);

        return new TaggedTaskSet(tasks, total, excluded);
    }

    public async Task<TaggedTaskSet> ReadTasksTitledWithAnyAsync(
        IndexUser user,
        IReadOnlyCollection<string> containingAny,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(containingAny);

        var excluded = await ReadExcludedListsAsync(user, cancellationToken);

        var needles = containingAny.Where(needle => needle.Length > 0).ToList();

        if (needles.Count == 0 || limit <= 0)
        {
            return new TaggedTaskSet([], 0, excluded);
        }

        // No Occurrence join at all, unlike every other read here: the tasks this is looking for are
        // the ones whose Hashtag has gone, and a join on Occurrences is exactly the filter that
        // would hide them. The same list exclusion still applies — ADR-0003 forbids writing against
        // a half-scanned list, whichever direction the write goes in.
        //
        // LIKE under the binary collation, for the reason TaggedTitleReader gives: the title column
        // carries no collation of its own, and under the database's default one a supplementary
        // character has no weight, so every needle would match every row. EF's Contains adds an
        // "or the needle is empty" guard that is compared the same way and defeats it; a pattern
        // has no such guard, and a Marker cannot carry a wildcard because it is one emoji.
        var matching =
            from task in context.Set<IndexedTask>().AsNoTracking()
            join state in context.Set<TaskListIndexState>().AsNoTracking()
                on new { task.TenantId, task.UserId, task.TaskListId }
                equals new { state.TenantId, state.UserId, state.TaskListId }
            where task.TenantId == user.TenantId
                && task.UserId == user.UserId
                && state.LastCompletedScanAt != null
                && needles.Any(needle =>
                    EF.Functions.Like(
                        EF.Functions.Collate(task.Title, StorageConventions.KeyCollation),
                        "%" + needle + "%"))
            orderby state.DisplayName, task.Title, task.GraphTaskId
            select new TaggedTask(task.TaskListId, state.DisplayName, task.GraphTaskId, task.Title);

        var total = await matching.CountAsync(cancellationToken);

        if (total > limit)
        {
            return new TaggedTaskSet([], total, excluded);
        }

        var tasks = await matching.Take(limit).ToListAsync(cancellationToken);

        return new TaggedTaskSet(tasks, total, excluded);
    }

    public Task<bool> HashtagExistsAsync(IndexUser user, string key, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);

        return context.Set<HashtagOccurrence>()
            .AsNoTracking()
            .AnyAsync(
                occurrence => occurrence.TenantId == user.TenantId
                    && occurrence.UserId == user.UserId
                    && occurrence.Key == key,
                cancellationToken);
    }

    private async Task<IReadOnlyList<ExcludedTaskList>> ReadExcludedListsAsync(
        IndexUser user,
        CancellationToken cancellationToken)
    {
        var incomplete = await context.Set<TaskListIndexState>()
            .AsNoTracking()
            .Where(state => state.TenantId == user.TenantId
                && state.UserId == user.UserId
                && state.LastCompletedScanAt == null)
            .OrderBy(state => state.DisplayName)
            .Select(state => new { state.TaskListId, state.DisplayName, state.State, state.FailureReason })
            .ToListAsync(cancellationToken);

        return
        [
            .. incomplete.Select(state => new ExcludedTaskList(
                state.TaskListId,
                state.DisplayName,
                // The list's own failure if it has one — worded where the cause was known, and not
                // repeated in the client's words (CONTRIBUTING § What a failure is allowed to say).
                state.FailureReason ?? state.State switch
                {
                    ListScanState.NeverScanned => NeverScannedReason,
                    ListScanState.Scanning => StillScanningReason,
                    _ => NotFinishedReason,
                })),
        ];
    }
}
