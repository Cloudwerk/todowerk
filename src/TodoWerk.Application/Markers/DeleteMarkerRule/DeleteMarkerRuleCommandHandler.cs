using TodoWerk.Application.Abstractions.Authentication;
using TodoWerk.Application.Abstractions.Messaging;
using TodoWerk.SharedKernel;

namespace TodoWerk.Application.Markers.DeleteMarkerRule;

internal sealed class DeleteMarkerRuleCommandHandler(ICurrentUser currentUser, IMarkerRuleStore rules)
    : ICommandHandler<DeleteMarkerRuleCommand>
{
    public async Task<Result> HandleAsync(DeleteMarkerRuleCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var user = IndexUser.From(currentUser);

        if (user is null)
        {
            return Result.Failure(MarkerErrors.NotSignedIn);
        }

        return await rules.DeleteAsync(user, command.RuleId, cancellationToken)
            ? Result.Success()
            : Result.Failure(MarkerErrors.RuleNotFound);
    }
}
