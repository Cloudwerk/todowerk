using TodoWerk.Application.Abstractions.Authentication;
using TodoWerk.Application.Abstractions.Messaging;
using TodoWerk.Domain.Hashtags;
using TodoWerk.SharedKernel;

namespace TodoWerk.Application.Markers.UpdateMarkerRule;

internal sealed class UpdateMarkerRuleCommandHandler(ICurrentUser currentUser, IMarkerRuleStore rules)
    : ICommandHandler<UpdateMarkerRuleCommand, MarkerRuleDto>
{
    public async Task<Result<MarkerRuleDto>> HandleAsync(
        UpdateMarkerRuleCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var user = IndexUser.From(currentUser);

        if (user is null)
        {
            return Result.Failure<MarkerRuleDto>(MarkerErrors.NotSignedIn);
        }

        if (command.Marker is null && command.Move is null)
        {
            return Result.Failure<MarkerRuleDto>(MarkerErrors.NothingToUpdate);
        }

        // One or the other. Both would be two writes, and a request whose second write failed
        // would be answered as a failure that had nonetheless changed the rule.
        if (command.Marker is not null && command.Move is not null)
        {
            return Result.Failure<MarkerRuleDto>(MarkerErrors.OneChangeAtATime);
        }

        MarkerRuleRecord? updated = null;

        if (command.Marker is not null)
        {
            if (!Marker.TryCreate(command.Marker, out var marker))
            {
                return Result.Failure<MarkerRuleDto>(MarkerErrors.ForRefusedMarker(command.Marker));
            }

            var changed = await rules.ChangeMarkerAsync(user, command.RuleId, marker, cancellationToken);

            if (changed.IsFailure)
            {
                return Result.Failure<MarkerRuleDto>(changed.Error);
            }

            updated = changed.Value;
        }

        if (command.Move is not null)
        {
            var moved = await rules.MoveAsync(user, command.RuleId, command.Move.Value, cancellationToken);

            if (moved.IsFailure)
            {
                return Result.Failure<MarkerRuleDto>(moved.Error);
            }

            updated = moved.Value;
        }

        return Result.Success(MarkerRuleMapper.ToDto(updated!));
    }
}
