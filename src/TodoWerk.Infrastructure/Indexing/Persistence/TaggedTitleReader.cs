using Microsoft.EntityFrameworkCore;
using TodoWerk.Application.Abstractions.Authentication;
using TodoWerk.Application.Abstractions.Indexing;
using TodoWerk.Domain.Indexing;
using TodoWerk.Infrastructure.Persistence;

namespace TodoWerk.Infrastructure.Indexing.Persistence;

/// <summary>
/// Answers "how many tasks carry this Hashtag, and which of them open with an emoji" for the
/// Markers module's coverage and stale figures. These are the Indexing module's tables; the caller sees
/// only the shared port.
/// <para>
/// Every comparison against a title here is made under the binary collation, spelled out on the
/// query because the title column does not carry it. Under the database's default collation a
/// supplementary character has no weight at all, so <c>N'🍞' = N'☕'</c> is true and
/// <c>LIKE N'%🍞%'</c> matches every row there is (measured against SQL Server, not inferred) —
/// which would turn the narrowing this reader exists for into a read of every title.
/// </para>
/// </summary>
internal sealed class TaggedTitleReader(TodoWerkDbContext context) : ITaggedTitleReader
{
    public async Task<IReadOnlyDictionary<string, int>> CountTasksPerKeyAsync(
        IndexUser user,
        IReadOnlyCollection<string> keys,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(keys);

        if (keys.Count == 0)
        {
            return new Dictionary<string, int>(StringComparer.Ordinal);
        }

        var wanted = keys.ToList();

        // Distinct tasks, not Occurrences: a Hashtag written twice in one title is one task
        // carrying it, and "n of m tagged tasks" is a sentence about tasks. Only tasks a Change
        // could actually write, which means lists that have been read end to end at least once —
        // the same rule the planner's reader follows (ADR-0003). Counting the rest would put tasks
        // in the denominator that no Apply will ever reach, so "n of m" could never arrive at m.
        var counts = await (
                from occurrence in context.Set<HashtagOccurrence>().AsNoTracking()
                join task in context.Set<IndexedTask>().AsNoTracking()
                    on occurrence.IndexedTaskId equals task.Id
                join state in context.Set<TaskListIndexState>().AsNoTracking()
                    on new { task.TenantId, task.UserId, task.TaskListId }
                    equals new { state.TenantId, state.UserId, state.TaskListId }
                where occurrence.TenantId == user.TenantId
                    && occurrence.UserId == user.UserId
                    && wanted.Contains(occurrence.Key)
                    && state.LastCompletedScanAt != null
                group occurrence by occurrence.Key into byKey
                select new { Key = byKey.Key, Count = byKey.Select(o => o.IndexedTaskId).Distinct().Count() })
            .ToListAsync(cancellationToken);

        return counts.ToDictionary(row => row.Key, row => row.Count, StringComparer.Ordinal);
    }

    public async Task<IReadOnlyList<MarkedTitle>> ReadMarkedTasksAsync(
        IndexUser user,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);

        // No needles and no titles. The run the title opens with is a column, so the
        // filter is a comparison the database can make on its own — and the filtered index over
        // exactly this predicate means it walks the marked tasks rather than all of them.
        //
        // The comparison is still binary, because the column is: under the database's default
        // collation an emoji weighs nothing and every run would equal the empty string, so this
        // test would exclude every row rather than the unmarked ones.
        //
        // The same list exclusion as the count above, so both figures are drawn from the same set
        // of tasks: a list nobody has read end to end contributes nothing an Apply could reach
        // (ADR-0003).
        //
        // No Occurrence join: a stale Marker sits on a task whose Hashtag has gone, and a join
        // would drop exactly those rows, so the Hashtags hang off each row instead.
        var rows = await (
                from task in context.Set<IndexedTask>().AsNoTracking()
                join state in context.Set<TaskListIndexState>().AsNoTracking()
                    on new { task.TenantId, task.UserId, task.TaskListId }
                    equals new { state.TenantId, state.UserId, state.TaskListId }
                where task.TenantId == user.TenantId
                    && task.UserId == user.UserId
                    && state.LastCompletedScanAt != null
                    && task.LeadingEmoji != string.Empty
                select new
                {
                    task.Id,
                    task.LeadingEmoji,

                    // Distinct because a Hashtag written twice in one title is two Occurrence rows
                    // and one Hashtag on one task.
                    Keys = context.Set<HashtagOccurrence>()
                        .Where(occurrence => occurrence.IndexedTaskId == task.Id)
                        .Select(occurrence => occurrence.Key)
                        .Distinct()
                        .ToList(),
                })
            .ToListAsync(cancellationToken);

        return [.. rows.Select(row => new MarkedTitle(row.Id, row.LeadingEmoji, row.Keys))];
    }
}
