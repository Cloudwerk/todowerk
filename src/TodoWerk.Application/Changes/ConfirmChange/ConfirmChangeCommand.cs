using TodoWerk.Application.Abstractions.Messaging;

namespace TodoWerk.Application.Changes.ConfirmChange;

/// <summary>
/// Queue the Change. What is confirmed is the instruction, not the list of tasks — the server
/// plans again and persists what it computed, because a plan posted back by the browser would be
/// a list of task ids somebody could edit.
/// </summary>
/// <param name="ConfirmMerge">
/// The second confirmation a Merge asks for. Ignored for the other two operations, which destroy
/// nothing.
/// </param>
/// <param name="ApplyMarkers">
/// The fourth shape (ADR-0014). The Markers it applies are copied out of the rules table here, at
/// confirmation, and never read again — which is what makes a rule edited mid-run wait for the next
/// Apply.
/// </param>
/// <param name="MarkerRuleKey">One rule's Hashtag key, or null for all of them.</param>
/// <param name="SurvivingMarker">
/// Which Marker the folded-together Hashtag keeps, where several sources hold a rule and the target
/// holds none. The one question a Merge cannot answer for itself (ADR-0014); ignored otherwise.
/// </param>
/// <param name="RemoveMarkers">
/// The fifth shape (ADR-0014). Its scope is copied out of the rules table here, at confirmation,
/// and never read again — so a rule edited while it is queued does not widen or narrow what was
/// agreed to.
/// </param>
/// <param name="Marker">The one Marker to remove, as text, or null for every stale one.</param>
public sealed record ConfirmChangeCommand(
    IReadOnlyList<string>? SourceKeys,
    string? TargetSpelling,
    bool ConfirmMerge,
    bool ApplyMarkers,
    string? MarkerRuleKey,
    string? SurvivingMarker,
    bool RemoveMarkers = false,
    string? Marker = null) : ICommand<ChangeDto>;
