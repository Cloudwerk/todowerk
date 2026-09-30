using Microsoft.Extensions.Options;
using TodoWerk.Application.Abstractions.Authentication;
using TodoWerk.Application.Abstractions.Messaging;
using TodoWerk.SharedKernel;

namespace TodoWerk.Application.Changes.CancelChange;

internal sealed class CancelChangeCommandHandler(
    ICurrentUser currentUser,
    IChangeStore changes,
    IOptions<ChangeOptions> options,
    TimeProvider timeProvider)
    : ICommandHandler<CancelChangeCommand, ChangeDto>
{
    public async Task<Result<ChangeDto>> HandleAsync(
        CancelChangeCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var user = IndexUser.From(currentUser);

        if (user is null)
        {
            return Result.Failure<ChangeDto>(ChangeErrors.NotSignedIn);
        }

        // Cancelling something that has already finished is not an error worth raising — the user
        // asked for it to stop and it has. Only a Change that is not theirs, or not there at all,
        // is a 404.
        await changes.RequestCancelAsync(user, command.ChangeId, cancellationToken);

        var stored = await changes.FindAsync(user, command.ChangeId, cancellationToken);

        return stored is null
            ? Result.Failure<ChangeDto>(ChangeErrors.ChangeNotFound)
            : Result.Success(ChangeMapper.ToDto(stored, timeProvider.GetUtcNow(), options.Value.ChangeRetention));
    }
}
