using TodoWerk.Application.Abstractions.Authentication;

namespace TodoWerk.Application.Abstractions.Indexing;

/// <summary>
/// The index's stored titles, for a caller that has a question about them the index does not
/// understand. Today: how far a Marker Rule has been applied.
/// <para>
/// A shared port for the reason the others here are: the Indexing module owns the tasks and the
/// Markers module owns the rules, and neither may depend on the other. What crosses the boundary is
/// a count and some titles — never the grammar of a block, which stays entirely on the Markers side.
/// The <c>containingAny</c> argument is the seam: the caller says which strings narrow the rows,
/// and this module narrows them without being told what the strings mean.
/// </para>
/// <para>
/// As fresh as the last scan, like every inventory figure, and the Workbench's freshness indicator
/// already says so. Both reads cover lists that have been read end to end at least once and no
/// others — the same rule a Change is planned under (ADR-0003) — so that "n of m" is a fraction of
/// tasks an Apply could actually reach.
/// </para>
/// </summary>
public interface ITaggedTitleReader
{
    /// <summary>
    /// How many tasks carry each of <paramref name="keys"/>. A key with no tasks is absent rather
    /// than zero.
    /// </summary>
    Task<IReadOnlyDictionary<string, int>> CountTasksPerKeyAsync(
        IndexUser user,
        IReadOnlyCollection<string> keys,
        CancellationToken cancellationToken);

    /// <summary>
    /// Every task of this person's that opens with an emoji: the run it opens with, and the folded
    /// Hashtag keys it carries.
    /// <para>
    /// The one read behind both marker figures. It takes no needles and reads no titles: handing
    /// over one string per emoji the person had ever used would hit SQL Server's parameter ceiling
    /// and bring back the whole 512-character title of every task any of them matched. The index
    /// stores the run itself, so the question is a column test the database can answer on its own.
    /// </para>
    /// <para>
    /// Deliberately not filtered by Hashtag. The caller counts two things off these rows — how far
    /// each rule has been applied, and how much of it is stale — and the second is a question about
    /// tasks whose Hashtag has <em>gone</em>. A join on Occurrences is exactly the filter that would
    /// hide the population being measured, so the Hashtags come back as a property of the row
    /// instead of as a condition on it.
    /// </para>
    /// <para>
    /// The keys are read from the Occurrences rather than re-extracted from the title, because the
    /// Occurrences are what the index already decided and what every other count here is drawn from.
    /// Re-running the grammar would be a second opinion on a settled question, and a Hashtag the two
    /// readings disagreed about is exactly what makes a Marker look stale.
    /// </para>
    /// <para>
    /// Still a narrowing rather than an answer: which of these runs is a block, and whose, is the
    /// grammar the Markers module owns and this module is not told.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<MarkedTitle>> ReadMarkedTasksAsync(IndexUser user, CancellationToken cancellationToken);
}

/// <param name="TaskId">
/// The index's own id for the task — not Graph's, which stays on the Indexing side of the port.
/// Here so that two tasks with the same title are two rows and not one.
/// </param>
/// <param name="LeadingEmoji">
/// The run of emoji the title opens with, as the index last saw it. Never empty — a task whose
/// title does not open with one is not in this answer at all.
/// </param>
/// <param name="Keys">
/// The folded Hashtag keys this task carries, from the index rather than from the title. Empty is
/// meaningful and is the interesting case: a task with a Marker at the front and no Hashtag at all
/// is the plainest stale Marker there is.
/// </param>
public sealed record MarkedTitle(Guid TaskId, string LeadingEmoji, IReadOnlyList<string> Keys);
