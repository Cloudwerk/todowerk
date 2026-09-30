using TodoWerk.Application.Abstractions.Authentication;
using TodoWerk.Domain.Hashtags;

namespace TodoWerk.Application.Abstractions.Markers;

/// <summary>
/// The three things a Change does to Marker Rules, none of which writes a title.
/// <para>
/// Separate from <see cref="IMarkerRuleReader"/> because reading and moving are asked for by
/// different things at different moments: a preview reads, and only a Change that reached a
/// terminal state moves. A port that did both would let a preview quietly acquire the ability to
/// delete somebody's rules.
/// </para>
/// </summary>
public interface IMarkerRuleMover
{
    /// <summary>
    /// Carries the rule on <paramref name="fromKey"/> over to <paramref name="toKey"/>, keeping its
    /// Marker and its position — what a completed Rename does (ADR-0014). Does nothing when there is
    /// no rule to carry, or when the target already has one, which is the case a Merge settles by
    /// deleting the losers instead.
    /// <para>
    /// <paramref name="expected"/> is the Marker the Change copied at confirmation, and it is what
    /// says whether the rule now standing on <paramref name="fromKey"/> is the one this Change is
    /// about. A Marker is unique across a person's standing rules, so a rule already carried holds
    /// it on the target and nothing else can be wearing it on the source: a rule found there with
    /// another Marker is somebody's new rule at a freed name and none of this Change's business.
    /// </para>
    /// </summary>
    /// <returns>Which of those three happened. The caller has to tell them apart; see <see cref="MarkerRuleCarry"/>.</returns>
    Task<MarkerRuleCarry> CarryRuleAsync(
        IndexUser user,
        string fromKey,
        string toKey,
        string toSpelling,
        Marker expected,
        CancellationToken cancellationToken);

    /// <summary>
    /// Deletes the rules on <paramref name="keys"/>. The losing side of a Merge, which writes
    /// nothing to anybody's tasks.
    /// </summary>
    Task<int> DeleteRulesAsync(
        IndexUser user,
        IReadOnlyCollection<string> keys,
        CancellationToken cancellationToken);

    /// <summary>
    /// Tells each rule what an Apply — or the undo of one — has just left in the blocks of the
    /// tasks it covers. A rule whose Marker that is has nothing left to retire; a rule whose Marker
    /// it is not has that to retire, whatever it thought before. Answered from the titles' side
    /// rather than cleared, because a rule edited mid-run and an undo both leave the titles
    /// carrying something the rule no longer remembers (ADR-0014).
    /// </summary>
    /// <returns>How many rules were found and told. A rule deleted in the meantime is simply absent.</returns>
    Task<int> RecordWrittenMarkersAsync(
        IndexUser user,
        IReadOnlyCollection<WrittenMarker> written,
        CancellationToken cancellationToken);

    /// <summary>
    /// Drops the rows kept only to keep <paramref name="removed"/> known, now that a Remove Markers
    /// has taken those Markers out of every block that carried one (ADR-0014).
    /// <para>
    /// This is what finally empties the rows a deleted rule leaves behind. They exist for one
    /// reason — the emoji is at the front of titles and the block reader has to recognise it — and
    /// when it is out of those titles the reason is gone. A row that was only <em>retiring</em> a
    /// removed Marker keeps its own and forgets the retirement, because deleting it would orphan
    /// the Marker it still holds.
    /// </para>
    /// </summary>
    /// <returns>How many rows were dropped or emptied.</returns>
    Task<int> ForgetRemovedMarkersAsync(
        IndexUser user,
        IReadOnlyCollection<Marker> removed,
        CancellationToken cancellationToken);

    /// <summary>
    /// Puts back a row for each of <paramref name="restored"/>, which the undo of a Remove Markers
    /// has just written back into the fronts of titles.
    /// <para>
    /// The counterpart of <see cref="ForgetRemovedMarkersAsync"/>, and what makes dropping those
    /// rows safe to do at all: undo is offered for thirty days, so a Marker the product had
    /// forgotten can reappear in real titles, and one the block reader does not recognise ends the
    /// block early — the next Apply would then write a second block in front of the first, which is
    /// the artefact the kept rows exist to prevent.
    /// </para>
    /// <para>
    /// Idempotent by Marker: a row already keeping one known is left alone rather than doubled.
    /// </para>
    /// </summary>
    /// <returns>How many rows were written.</returns>
    Task<int> RememberRemovedMarkersAsync(
        IndexUser user,
        IReadOnlyCollection<AbandonedMarkerSnapshot> restored,
        CancellationToken cancellationToken);
}

/// <summary>
/// How a carry ended. Three outcomes rather than a bool, because two of the ways it can fail want
/// opposite things from the caller and a bool made them one.
/// <para>
/// A Merge deletes the rules that lost, and the rule being carried is not one of them. Reading
/// "did not move" as "the rule is still on the old key and lost, so delete it" is right for
/// <see cref="TargetWins"/> and wrong for <see cref="NothingToCarry"/>. The runner carries rules
/// before it finishes the Change, on purpose, so that a process replaced between the two redoes
/// the tail rather than losing it — and a redone tail finds the rule already on the target. If the
/// person has created a rule at the freed name in that window, reading "did not move" as "lost"
/// would delete it.
/// </para>
/// <para>
/// What tells the two apart is the Marker the Change copied at confirmation, not the shape of the
/// rules table: the table looks the same either way, which is what made this an inference. A
/// Marker is unique across a person's standing rules, so the rule this Change is about is the only
/// one that can be wearing it.
/// </para>
/// </summary>
public enum MarkerRuleCarry
{
    /// <summary>
    /// The source key holds nothing of this Change's: empty, already carried, deleted by the
    /// person, or holding a rule created since that wears a different Marker. Nothing here is this
    /// Change's to delete.
    /// </summary>
    NothingToCarry,

    /// <summary>The rule moved to the target key, keeping its Marker and its position.</summary>
    Carried,

    /// <summary>The target already had a rule, which wins. The source's rule is a loser of the Merge.</summary>
    TargetWins,
}

/// <summary>What one rule's tasks now carry, by the rule's Hashtag key.</summary>
public sealed record WrittenMarker(string Key, Marker Marker);
