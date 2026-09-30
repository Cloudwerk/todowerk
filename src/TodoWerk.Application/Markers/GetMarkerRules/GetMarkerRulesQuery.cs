using TodoWerk.Application.Abstractions.Messaging;

namespace TodoWerk.Application.Markers.GetMarkerRules;

/// <summary>This person's Marker Rules, in the order that is the order of the block.</summary>
public sealed record GetMarkerRulesQuery : IQuery<MarkerRuleListDto>;
