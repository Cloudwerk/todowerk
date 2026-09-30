using TodoWerk.Application.Abstractions.Messaging;

namespace TodoWerk.Application.Changes.PreviewChange;

/// <summary>
/// Ask what a Change would do, without doing it.
/// <para>
/// A command rather than a query, and a POST rather than a GET, although it writes nothing: the
/// sources are a list of Hashtag keys up to 255 characters each, which is a request body and not a
/// query string, and the antiforgery gate that comes with a POST costs nothing here.
/// </para>
/// </summary>
/// <param name="SourceKeys">The Hashtags a Hashtag Change would rewrite. Ignored by an Apply.</param>
/// <param name="TargetSpelling">The Spelling they would take. Ignored by an Apply.</param>
/// <param name="ApplyMarkers">
/// This is the fourth shape (ADR-0014), which carries no Hashtags and no target. Stated rather than
/// derived: nothing about the other two fields could imply it, and a request that meant an Apply and
/// was read as a Rename would write the wrong thing entirely.
/// </param>
/// <param name="MarkerRuleKey">
/// The one rule to apply, by its Hashtag's folded key — or null for all of them. Either way the
/// write is the whole block; this decides which tasks are covered, not what is written into them.
/// </param>
/// <param name="RemoveMarkers">
/// This is the fifth shape (ADR-0014): taking stale Markers out rather than putting Markers in.
/// Stated for the same reason <paramref name="ApplyMarkers"/> is, and separately from it, because a
/// request that meant one and was read as the other would write the opposite of what was asked.
/// </param>
/// <param name="Marker">
/// The one Marker to remove, as text — or null for every stale Marker the person has. A Marker
/// rather than a Hashtag key, because a stale Marker's Hashtag has by definition left the task, and
/// one a deleted rule left behind has no key at all.
/// </param>
public sealed record PreviewChangeCommand(
    IReadOnlyList<string>? SourceKeys,
    string? TargetSpelling,
    bool ApplyMarkers,
    string? MarkerRuleKey,
    bool RemoveMarkers = false,
    string? Marker = null) : ICommand<ChangePreviewDto>;
