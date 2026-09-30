using TodoWerk.Application.Abstractions.Messaging;

namespace TodoWerk.Application.Markers.UpdateMarkerRule;

/// <summary>
/// Change what a rule carries, or where it sits. One command for both, because they are the two
/// halves of the same PATCH and a request that says neither is a request that means nothing.
/// </summary>
/// <param name="Marker">A new Marker, as text, or null to leave it alone.</param>
/// <param name="Move">A step up or down the list, or null to leave the order alone.</param>
public sealed record UpdateMarkerRuleCommand(Guid RuleId, string? Marker, MarkerRuleMove? Move)
    : ICommand<MarkerRuleDto>;
