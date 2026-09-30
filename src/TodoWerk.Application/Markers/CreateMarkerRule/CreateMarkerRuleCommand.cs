using TodoWerk.Application.Abstractions.Messaging;

namespace TodoWerk.Application.Markers.CreateMarkerRule;

/// <summary>
/// Tell TodoWerk that a Hashtag carries a Marker. It writes no title: applying the rule is a
/// separate, previewed Change (ADR-0014).
/// </summary>
/// <param name="Spelling">The Hashtag's name, without the leading marker. Folded to its key here.</param>
/// <param name="Marker">One emoji, as text.</param>
public sealed record CreateMarkerRuleCommand(string Spelling, string Marker) : ICommand<MarkerRuleDto>;
