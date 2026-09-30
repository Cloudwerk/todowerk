using Microsoft.EntityFrameworkCore;
using TodoWerk.Application.Abstractions.Authentication;
using TodoWerk.Application.Abstractions.Erasure;
using TodoWerk.Domain.Markers;
using TodoWerk.Infrastructure.Persistence;

namespace TodoWerk.Infrastructure.Markers.Persistence;

/// <summary>
/// Destroys one person's Marker Rules. Rules are the first thing TodoWerk stores that a person
/// chose rather than observed — an emoji and a Hashtag each — so they join what ADR-0009 lists and
/// go with everything else when somebody asks to be forgotten.
/// </summary>
internal sealed class MarkerPersonalDataPurge(TodoWerkDbContext context) : IPersonalDataPurge
{
    /// <summary>
    /// Same bound and same reason as the other purges, although nothing writes a rule in the
    /// background: reaching a bound is reported rather than mistaken for finishing, and the shape
    /// stays the same across every implementation so no reader has to check which one is different.
    /// </summary>
    private const int MaximumPasses = 3;

    public string Describes => "the marker rules";

    public async Task<PurgeOutcome> PurgeAsync(IndexUser user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);

        var removed = 0;

        for (var pass = 0; pass < MaximumPasses; pass++)
        {
            var found = await context.Set<MarkerRule>()
                .Where(rule => rule.TenantId == user.TenantId && rule.UserId == user.UserId)
                .ExecuteDeleteAsync(cancellationToken);

            removed += found;

            if (found == 0)
            {
                return PurgeOutcome.Complete(removed);
            }
        }

        return PurgeOutcome.Unfinished(removed);
    }
}
