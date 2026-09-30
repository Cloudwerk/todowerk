namespace TodoWerk.Application.Markers;

/// <summary>
/// One Marker Rule as the Workbench shows it. The Marker crosses the wire as text, because that is
/// what a browser puts in front of somebody.
/// </summary>
/// <param name="Key">The folded Hashtag key, which is what a request names a rule's Hashtag by.</param>
/// <param name="Spelling">What to call the Hashtag, kept on the rule so it survives its Occurrences.</param>
/// <param name="RetiredMarker">
/// The Marker the next Apply will swap out of the blocks that carry it, or null. Kept whatever
/// <paramref name="RetiredTaskCount"/> says: the row is what lets a later Apply reach the emoji,
/// and only the person's own tasks decide when there is nothing left to reach.
/// </param>
/// <param name="TaggedTaskCount">Tasks carrying the Hashtag, as the last scan left them.</param>
/// <param name="MarkedTaskCount">
/// How many of those already carry the Marker in their block — "n of m tagged tasks carry 🍞".
/// </param>
/// <param name="StaleTaskCount">
/// Tasks that no longer carry the Hashtag and still carry this rule's Marker at the front, or the
/// one it retired. What a Remove Markers scoped to this rule would take away — and the count
/// ADR-0014 held back until there was an action to put beside it, because "a count without the
/// action only nags".
/// </param>
/// <param name="RetiredTaskCount">
/// How many tasks the pending swap still has to reach — tasks whose block carries
/// <paramref name="RetiredMarker"/> and which an Apply would rewrite. Nought means the swap has
/// arrived everywhere the index can see, and the rules view says nothing about a replacement that
/// would replace nothing.
/// <para>
/// A count rather than a cleared rule, deliberately. Clearing the retired Marker would be a claim
/// that the emoji is nowhere, and the index cannot tell "already right everywhere" from "every task
/// of this rule is in a list nobody has read end to end" — clearing in the second case orphans the
/// emoji for good. Saying nothing in the second case is a badge that reappears when that list is
/// read, which costs nothing.
/// </para>
/// </param>
public sealed record MarkerRuleDto(
    Guid Id,
    string Key,
    string Spelling,
    string Marker,
    string? RetiredMarker,
    int Position,
    int TaggedTaskCount,
    int MarkedTaskCount,
    int StaleTaskCount,
    int RetiredTaskCount);

/// <summary>
/// This person's rules, in the order that is the order of the block, and the Markers left behind by
/// rules they have deleted.
/// </summary>
/// <param name="Abandoned">
/// One entry per Marker of a deleted rule that is still at the front of at least one task. The only
/// place in the product where those emoji are visible at all: a deleted rule is listed nowhere, so
/// without this its Marker would sit in somebody's titles with nothing anywhere offering to take it
/// out. A Marker no task carries any more is absent rather than nought — there is nothing to say
/// about it, and its row is waiting to be dropped.
/// </param>
public sealed record MarkerRuleListDto(
    IReadOnlyList<MarkerRuleDto> Rules,
    IReadOnlyList<AbandonedMarkerDto> Abandoned);

/// <summary>
/// A Marker whose rule is gone, and how many tasks still carry it. It has no Hashtag to name,
/// because the rule that knew which Hashtag it was about is deleted — and by the time anybody sees
/// this, the only true thing left to say about the emoji is where it is.
/// </summary>
public sealed record AbandonedMarkerDto(string Marker, int StaleTaskCount);

/// <summary>
/// What the store hands back about one rule: facts only, with the Marker as its own text. The
/// domain's word for one emoji has done its work by the time a rule is stored — it decided what
/// could be written down — and everything above this reads the value rather than reasoning about
/// it, so carrying the type further would buy a wrapper and unwrap it at every use.
/// </summary>
public sealed record MarkerRuleRecord(
    Guid Id,
    string Key,
    string Spelling,
    string Marker,
    string? RetiredMarker,
    int Position);

/// <summary>
/// Which way a reorder moves one rule. Up and down rather than a target index: the client shows
/// two buttons on a row, nothing in the product depends on drag, and a posted index
/// would be a position computed against a list the server may have changed since.
/// </summary>
public enum MarkerRuleMove
{
    Up = 0,

    Down = 1,
}
