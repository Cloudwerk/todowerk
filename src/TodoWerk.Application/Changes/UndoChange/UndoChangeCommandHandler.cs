using Microsoft.Extensions.Options;
using TodoWerk.Application.Abstractions.Authentication;
using TodoWerk.Application.Abstractions.Messaging;
using TodoWerk.SharedKernel;

namespace TodoWerk.Application.Changes.UndoChange;

internal sealed class UndoChangeCommandHandler(
    ICurrentUser currentUser,
    IChangeStore changes,
    IOptions<ChangeOptions> options,
    TimeProvider timeProvider)
    : ICommandHandler<UndoChangeCommand, ChangeDto>
{
    public async Task<Result<ChangeDto>> HandleAsync(
        UndoChangeCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var user = IndexUser.From(currentUser);

        if (user is null)
        {
            return Result.Failure<ChangeDto>(ChangeErrors.NotSignedIn);
        }

        var now = timeProvider.GetUtcNow();
        var retention = options.Value.ChangeRetention;

        var original = await changes.FindAsync(user, command.ChangeId, cancellationToken);

        if (original is null)
        {
            return Result.Failure<ChangeDto>(ChangeErrors.ChangeNotFound);
        }

        // The same rule that decided whether the button was drawn, asked again at the moment it
        // was pressed: the retention window can pass while the page is open, and somebody with two
        // tabs can press it twice.
        if (!ChangeUndoPolicy.CanUndo(original, now, retention))
        {
            return Result.Failure<ChangeDto>(ChangeErrors.UndoNotAvailable);
        }

        // Checked after the undo rules, so "this change cannot be undone" is not reported as "you
        // already have one running" when both are true.
        if (await changes.HasUnfinishedChangeAsync(user, cancellationToken))
        {
            return Result.Failure<ChangeDto>(ChangeErrors.ChangeAlreadyInFlight);
        }

        var undoId = await changes.AddUndoAsync(user, command.ChangeId, cancellationToken);

        if (undoId is null)
        {
            // The journal was swept, or the row lost its race with another tab pressing Undo.
            return Result.Failure<ChangeDto>(ChangeErrors.UndoNotAvailable);
        }

        var stored = await changes.FindAsync(user, undoId.Value, cancellationToken);

        return stored is null
            ? Result.Failure<ChangeDto>(ChangeErrors.ChangeNotFound)
            : Result.Success(ChangeMapper.ToDto(stored, now, retention));
    }
}
