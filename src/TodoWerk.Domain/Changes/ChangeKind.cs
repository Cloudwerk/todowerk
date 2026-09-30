namespace TodoWerk.Domain.Changes;

/// <summary>
/// Which operation a Change turns out to be. The first three are derived from its shape, never
/// chosen: one mechanism, three names it reports back (ADR-0006). The fourth is stated rather than
/// derived, because nothing about a set of Hashtags and a Spelling could imply it (ADR-0014).
/// </summary>
public enum ChangeKind
{
    /// <summary>
    /// One source, and the target folds to that same Hashtag: <c>works</c> → <c>Works</c>. Acts
    /// within a single Hashtag and destroys no distinction.
    /// </summary>
    NormaliseCasing = 0,

    /// <summary>
    /// One source taking a name nothing else uses: <c>Prio1</c> → <c>Priority1</c>. The Hashtag
    /// keeps its identity as a thing; only its name changes.
    /// </summary>
    Rename = 1,

    /// <summary>
    /// Two or more sources, or one source landing on a Hashtag that already exists. The only one
    /// of the three that destroys a distinction the user made, which is why it asks twice.
    /// </summary>
    Merge = 2,

    /// <summary>
    /// Bringing titles into line with the user's Marker Rules: the block at the front of each task
    /// is rewritten to carry the Markers whose Hashtags the task holds, in rule order. The one
    /// Change that writes outside a Hashtag, and the one that carries no target Spelling — what it
    /// applies is a copy of the rules, taken at confirmation (ADR-0014).
    /// </summary>
    ApplyMarkers = 3,

    /// <summary>
    /// Taking stale Markers out of the blocks they are sitting in: an emoji whose Hashtag has left
    /// the task, or whose rule the person deleted. The only operation in the product that removes
    /// text, and the mirror of <see cref="ApplyMarkers"/> — same plan, journal, queue, cancel and
    /// undo, opposite direction (ADR-0014).
    /// <para>
    /// A Kind of its own rather than a flag on the fourth, although the mechanism is shared. Kind
    /// is the axis every reader names a Change by, and everything that reads "Apply Markers" reads
    /// it as the promise that applying never removes; a flag would make that reading wrong in every
    /// place at once. It is also the cheaper of the two to store — these are written as their
    /// names, so a fifth name is a value in a column that already exists, while a flag would be a
    /// column that does not.
    /// </para>
    /// </summary>
    RemoveMarkers = 4,
}
