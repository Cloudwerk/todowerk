using TodoWerk.Application.Abstractions.Messaging;

namespace TodoWerk.Application.Changes.CancelChange;

/// <summary>
/// Stop after the task the runner is on. There is no pause to resume from: a paused Change holding
/// a lease is a wedged row waiting to be discovered, and "cancel, then queue the rest again" is
/// the same thing without the trap (ADR-0006).
/// </summary>
public sealed record CancelChangeCommand(Guid ChangeId) : ICommand<ChangeDto>;
