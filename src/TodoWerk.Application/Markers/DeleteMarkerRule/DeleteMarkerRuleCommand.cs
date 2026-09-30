using TodoWerk.Application.Abstractions.Messaging;

namespace TodoWerk.Application.Markers.DeleteMarkerRule;

/// <summary>
/// Forget a rule. It writes nothing: the Markers it put on tasks stay where they are until an
/// explicit Remove Markers takes them away (ADR-0014), because reconciling both ways would undo a
/// user's deliberate edit with a second one they did not ask for.
/// </summary>
public sealed record DeleteMarkerRuleCommand(Guid RuleId) : ICommand;
