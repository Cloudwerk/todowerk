using TodoWerk.Application.Abstractions.Authentication;
using TodoWerk.Application.Abstractions.Messaging;
using TodoWerk.SharedKernel;

namespace TodoWerk.Application.Changes.PreviewChange;

internal sealed class PreviewChangeCommandHandler(
    ICurrentUser currentUser,
    IChangeStore changes,
    ChangePlanner planner)
    : ICommandHandler<PreviewChangeCommand, ChangePreviewDto>
{
    public async Task<Result<ChangePreviewDto>> HandleAsync(
        PreviewChangeCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var user = IndexUser.From(currentUser);

        if (user is null)
        {
            return Result.Failure<ChangePreviewDto>(ChangeErrors.NotSignedIn);
        }

        // Refused here as well as at confirmation, so somebody with a Change already running is
        // told before they read a thousand pairs and press the button.
        if (await changes.HasUnfinishedChangeAsync(user, cancellationToken))
        {
            return Result.Failure<ChangePreviewDto>(ChangeErrors.ChangeAlreadyInFlight);
        }

        var planned = command switch
        {
            { RemoveMarkers: true } => await planner.PlanRemovalAsync(user, command.Marker, cancellationToken),
            { ApplyMarkers: true } => await planner.PlanMarkersAsync(user, command.MarkerRuleKey, cancellationToken),
            _ => await planner.PlanAsync(user, command.SourceKeys, command.TargetSpelling, cancellationToken),
        };

        return planned.IsSuccess
            ? Result.Success(planned.Value.ToPreview())
            : Result.Failure<ChangePreviewDto>(planned.Error);
    }
}
