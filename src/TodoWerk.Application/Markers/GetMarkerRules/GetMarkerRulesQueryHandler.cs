using TodoWerk.Application.Abstractions.Authentication;
using TodoWerk.Application.Abstractions.Messaging;
using TodoWerk.SharedKernel;

namespace TodoWerk.Application.Markers.GetMarkerRules;

internal sealed class GetMarkerRulesQueryHandler(
    ICurrentUser currentUser,
    IMarkerRuleStore rules,
    MarkerCoverage coverage)
    : IQueryHandler<GetMarkerRulesQuery, MarkerRuleListDto>
{
    public async Task<Result<MarkerRuleListDto>> HandleAsync(
        GetMarkerRulesQuery query,
        CancellationToken cancellationToken)
    {
        var user = IndexUser.From(currentUser);

        if (user is null)
        {
            return Result.Failure<MarkerRuleListDto>(MarkerErrors.NotSignedIn);
        }

        var stored = await rules.ListAsync(user, cancellationToken);
        var abandoned = await rules.ListAbandonedMarkersAsync(user, cancellationToken);
        var measured = await coverage.MeasureAsync(user, stored, abandoned, cancellationToken);

        return Result.Success(new MarkerRuleListDto(
            [
                .. stored.Select(rule => MarkerRuleMapper.ToDto(
                    rule,
                    measured.Coverage.TryGetValue(rule.Id, out var counts)
                        ? counts
                        : new MarkerCoverageCounts(0, 0, 0, 0))),
            ],
            [.. measured.Abandoned.Select(marker => new AbandonedMarkerDto(marker.Marker.Text, marker.Stale))]));
    }
}
