using Microsoft.Extensions.Options;
using TodoWerk.Application.Abstractions.Authentication;
using TodoWerk.Application.Abstractions.Indexing;
using TodoWerk.Application.Abstractions.Messaging;
using TodoWerk.SharedKernel;

namespace TodoWerk.Application.Onboarding.GetTenantOverview;

internal sealed class GetTenantOverviewQueryHandler(
    ICurrentUser currentUser,
    ITenantOverviewReader members,
    ITenantConsentStore consent,
    IOccurrenceCounter occurrences,
    IOptions<OnboardingOptions> options)
    : IQueryHandler<GetTenantOverviewQuery, TenantOverviewDto>
{
    /// <summary>
    /// The two short windows. The third is the retention window from configuration, so the longest
    /// thing this screen reports and the promise the sweep keeps are one number.
    /// </summary>
    private static readonly TimeSpan[] ShortWindows =
    [
        TimeSpan.FromDays(30),
        TimeSpan.FromDays(90),
    ];

    public async Task<Result<TenantOverviewDto>> HandleAsync(
        GetTenantOverviewQuery query,
        CancellationToken cancellationToken)
    {
        var user = IndexUser.From(currentUser);

        if (user is null)
        {
            return Result.Failure<TenantOverviewDto>(OnboardingErrors.NotSignedIn);
        }

        var settings = options.Value;

        // Read first, whatever the floor turns out to be: the consent panel has no floor, and the
        // member count is what decides whether the statistics do.
        var granted = await consent.IsGrantedAsync(user.TenantId, cancellationToken);

        var windows = Windows(settings.DormancyWindow);
        var counts = await members.ReadAsync(user.TenantId, windows, cancellationToken);

        // The floor is measured against the people the figures still describe — the rows that name
        // somebody — never against the cumulative count. An anonymised row holds no Occurrences and
        // no activity, so in a tenant of six where five have been forgotten the cumulative count
        // passes any sensible floor while every figure on the screen is about one identifiable
        // colleague: precisely the inference the floor exists to prevent.
        if (counts.IdentifiableMemberCount < settings.StatisticsFloor
            || counts.FirstSignedInAt is not { } firstSignedInAt)
        {
            // No statistics object at all. Not a zeroed one and not an empty state: nothing should
            // invite a reader to read a deliberate promise as a defect.
            return Result.Success(new TenantOverviewDto(Statistics: null, granted));
        }

        var total = await occurrences.CountForTenantAsync(user.TenantId, cancellationToken);

        return Result.Success(new TenantOverviewDto(
            new TenantStatisticsDto(
                counts.MemberCount,
                [.. windows.Select((window, index) => new TenantActivityWindowDto(
                    (int)window.TotalDays,
                    counts.ActiveCounts[index]))],
                firstSignedInAt,
                total,
                // Divided by the identifiable count, because those are the people who hold the
                // Occurrences — erasure deletes somebody's rows, so dividing by the cumulative count
                // would drift the average below any real person's figure as people are forgotten.
                // IdentifiableMemberCount is at least the floor here, which the options validation
                // keeps above zero, so the division needs no guard.
                Math.Round((double)total / counts.IdentifiableMemberCount, 1)),
            granted));
    }

    private static IReadOnlyList<TimeSpan> Windows(TimeSpan dormancyWindow) =>
    [
        .. ShortWindows.Where(window => window < dormancyWindow),
        dormancyWindow,
    ];
}
