using TodoWerk.Application.Abstractions.Authentication;
using TodoWerk.Domain.Hashtags;

namespace TodoWerk.Application.Abstractions.Markers;

/// <summary>
/// Reads one person's Marker Rules for the sake of something that is not the Markers module. The
/// Changes module plans an Apply from them (ADR-0014) and must not reach into Markers for them, so
/// the two meet here — the same place <see cref="Indexing.IIndexScanScheduler"/> exists for, in the
/// other direction.
/// </summary>
public interface IMarkerRuleReader
{
    /// <summary>This person's rules, in the order that is the order of the block.</summary>
    Task<IReadOnlyList<MarkerRuleSnapshot>> ReadRulesAsync(IndexUser user, CancellationToken cancellationToken);

    /// <summary>
    /// The Markers of the rules this person has deleted, retired ones included. Still at the front
    /// of every title those rules were applied to — deleting writes nothing (ADR-0014) — so a block
    /// reader has to keep recognising them, or the next Apply would put a second block in front of
    /// the first. Recognised and nothing more: never wanted, never in scope, never swapped.
    /// </summary>
    Task<IReadOnlyList<AbandonedMarkerSnapshot>> ReadAbandonedMarkersAsync(
        IndexUser user,
        CancellationToken cancellationToken);
}

/// <summary>
/// One rule as everything outside the Markers module sees it. A snapshot on purpose: a Change
/// copies these at confirmation and never reads the live table again, so a rule edited mid-run
/// waits for the next Apply (ADR-0014).
/// </summary>
/// <param name="Key">The folded Hashtag key, per <see cref="HashtagKey"/>.</param>
/// <param name="Spelling">What to call the Hashtag in a sentence, even when it has no Occurrences.</param>
/// <param name="RetiredMarker">The Marker the next Apply swaps out, or null.</param>
public sealed record MarkerRuleSnapshot(
    Guid Id,
    string Key,
    string Spelling,
    Marker Marker,
    Marker? RetiredMarker,
    int Position);

/// <summary>
/// One Marker a deleted rule left behind, as everything outside the Markers module sees it.
/// <para>
/// The Hashtag on it is vestigial: a deleted rule is listed nowhere and applied nowhere, and this
/// name is carried only so that a row can be written again under a name that meant something —
/// which is what the undo of a Remove Markers needs, because the row it puts back has to say which
/// Hashtag the emoji was once about.
/// </para>
/// </summary>
public sealed record AbandonedMarkerSnapshot(string Key, string Spelling, Marker Marker);
