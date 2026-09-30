using Microsoft.EntityFrameworkCore;
using TodoWerk.Application.Abstractions.Authentication;
using TodoWerk.Application.Abstractions.Markers;
using TodoWerk.Domain.Hashtags;
using TodoWerk.Domain.Markers;
using TodoWerk.Infrastructure.Persistence;

namespace TodoWerk.Infrastructure.Markers.Persistence;

/// <summary>
/// The shared port onto one person's rules, so that the Changes module can plan an Apply without
/// reaching into Markers (ADR-0014).
/// </summary>
internal sealed class MarkerRuleReader(TodoWerkDbContext context) : IMarkerRuleReader
{
    public async Task<IReadOnlyList<MarkerRuleSnapshot>> ReadRulesAsync(
        IndexUser user,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);

        var rules = await OwnedBy(user)
            .Where(rule => rule.DeletedAt == null)
            .OrderBy(rule => rule.Position)
            .ThenBy(rule => rule.CreatedAt)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return
        [
            .. rules.Select(rule => new MarkerRuleSnapshot(
                rule.Id,
                rule.Key,
                rule.Spelling,
                rule.Marker,
                rule.RetiredMarker,
                rule.Position)),
        ];
    }

    public Task<IReadOnlyList<AbandonedMarkerSnapshot>> ReadAbandonedMarkersAsync(IndexUser user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);

        return AbandonedMarkers.ReadAsync(OwnedBy(user), cancellationToken);
    }

    private IQueryable<MarkerRule> OwnedBy(IndexUser user) =>
        context.Set<MarkerRule>()
            .Where(rule => rule.TenantId == user.TenantId && rule.UserId == user.UserId);
}
