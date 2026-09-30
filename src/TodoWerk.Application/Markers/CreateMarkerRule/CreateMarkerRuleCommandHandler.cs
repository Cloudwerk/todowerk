using TodoWerk.Application.Abstractions.Authentication;
using TodoWerk.Application.Abstractions.Messaging;
using TodoWerk.Domain.Hashtags;
using TodoWerk.SharedKernel;

namespace TodoWerk.Application.Markers.CreateMarkerRule;

internal sealed class CreateMarkerRuleCommandHandler(ICurrentUser currentUser, IMarkerRuleStore rules)
    : ICommandHandler<CreateMarkerRuleCommand, MarkerRuleDto>
{
    public async Task<Result<MarkerRuleDto>> HandleAsync(
        CreateMarkerRuleCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var user = IndexUser.From(currentUser);

        if (user is null)
        {
            return Result.Failure<MarkerRuleDto>(MarkerErrors.NotSignedIn);
        }

        // The same round trip a Change's target has to survive: written into a title and read back
        // as one Hashtag spelled exactly this way. A rule about a name the extractor would not
        // recognise is a rule that can never match a task.
        if (!HashtagName.RoundTrips(command.Spelling))
        {
            return Result.Failure<MarkerRuleDto>(MarkerErrors.InvalidHashtag);
        }

        if (!Marker.TryCreate(command.Marker, out var marker))
        {
            return Result.Failure<MarkerRuleDto>(MarkerErrors.ForRefusedMarker(command.Marker));
        }

        var created = await rules.CreateAsync(
            user,
            HashtagKey.Fold(command.Spelling),
            command.Spelling,
            marker,
            cancellationToken);

        return created.IsSuccess
            ? Result.Success(MarkerRuleMapper.ToDto(created.Value))
            : Result.Failure<MarkerRuleDto>(created.Error);
    }
}
