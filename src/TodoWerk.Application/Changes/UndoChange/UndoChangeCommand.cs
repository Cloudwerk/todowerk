using TodoWerk.Application.Abstractions.Messaging;

namespace TodoWerk.Application.Changes.UndoChange;

/// <summary>
/// Put back what a Change wrote. Runs as a Change in the other direction — queued, exclusive per
/// user, sequential, journaled like any other — and the answer is that new Change, not the old one.
/// </summary>
public sealed record UndoChangeCommand(Guid ChangeId) : ICommand<ChangeDto>;
