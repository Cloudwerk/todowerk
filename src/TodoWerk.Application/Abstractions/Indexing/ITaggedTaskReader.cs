using TodoWerk.Application.Abstractions.Authentication;

namespace TodoWerk.Application.Abstractions.Indexing;

/// <summary>
/// Reads the index for the sake of something that is not the index. A Change is planned from
/// Occurrences (ADR-0006) and the Changes module must not reach into Indexing for them, so the two
/// meet here — the same place <see cref="IIndexScanScheduler"/> exists for, in the other
/// direction.
/// </summary>
public interface ITaggedTaskReader
{
    /// <summary>
    /// Every indexed task carrying any of <paramref name="keys"/> in a Spelling other than
    /// <paramref name="alreadySpelled"/>, from lists that have been read end to end at least once.
    /// </summary>
    /// <param name="keys">Folded Hashtag keys, compared as bytes (ADR-0005).</param>
    /// <param name="alreadySpelled">
    /// The target Spelling. A task whose every matching Occurrence is already written this way
    /// would be rewritten to itself, so it is not in the plan at all — and, more to the point, not
    /// in the count that the ceiling is measured against. Normalising one casing slip out of
    /// twelve hundred tasks must not be refused as a twelve-hundred-task Change.
    /// </param>
    /// <param name="limit">
    /// At most this many tasks come back, but <see cref="TaggedTaskSet.TotalCount"/> counts them
    /// all — the caller has to be able to say "1,412 tasks, which is past the ceiling" without
    /// loading 1,412 rows to find out.
    /// </param>
    Task<TaggedTaskSet> ReadTasksTaggedAsync(
        IndexUser user,
        IReadOnlyCollection<string> keys,
        string alreadySpelled,
        int limit,
        CancellationToken cancellationToken);

    /// <summary>
    /// Every indexed task whose title contains any of <paramref name="containingAny"/>, from lists
    /// that have been read end to end at least once — whatever Hashtags it carries, or none.
    /// <para>
    /// The sibling of <see cref="ReadTasksTaggedAsync"/> for the one Change that cannot use it. A
    /// Remove Markers goes looking for tasks whose Hashtag has <em>gone</em>, which is what makes
    /// the Marker on them stale, so there is no key to select them by; and a Marker a deleted rule
    /// left behind has no key at all. Selecting on the title is the only thing left.
    /// </para>
    /// <para>
    /// A narrowing rather than an answer, the same seam <see cref="ITaggedTitleReader"/> has: the
    /// caller says which strings narrow the rows, and this module narrows them without being told
    /// what they mean. Whether a match is in a block — and so whether it is a Marker at all — is
    /// decided by the grammar on the Markers side, per task.
    /// </para>
    /// </summary>
    /// <param name="containingAny">
    /// Strings any of which brings a task back. Compared as bytes, because under the database's
    /// default collation every emoji is equal to every other and one of these would match every
    /// title there is.
    /// </param>
    Task<TaggedTaskSet> ReadTasksTitledWithAnyAsync(
        IndexUser user,
        IReadOnlyCollection<string> containingAny,
        int limit,
        CancellationToken cancellationToken);

    /// <summary>
    /// Whether this user's index already holds a Hashtag with that folded key. What turns a
    /// rename into a Merge, and therefore what makes the UI ask a second time.
    /// </summary>
    Task<bool> HashtagExistsAsync(IndexUser user, string key, CancellationToken cancellationToken);
}

/// <param name="Tasks">Up to the caller's limit, in a stable order.</param>
/// <param name="TotalCount">Every matching task, whether or not it fitted in the limit.</param>
/// <param name="ExcludedLists">
/// Lists left out because they have never been read end to end. ADR-0003 forbids writing against a
/// half-scanned list, so their tasks are not in the plan — and naming them is what stops the plan
/// being silently short.
/// </param>
public sealed record TaggedTaskSet(
    IReadOnlyList<TaggedTask> Tasks,
    int TotalCount,
    IReadOnlyList<ExcludedTaskList> ExcludedLists);

/// <param name="Title">As the index last saw it, truncated to the display column. Advisory: the write re-reads.</param>
public sealed record TaggedTask(
    string TaskListId,
    string ListDisplayName,
    string GraphTaskId,
    string Title);

/// <param name="Reason">Why it contributes nothing, worded where the cause is known.</param>
public sealed record ExcludedTaskList(string TaskListId, string DisplayName, string Reason);
