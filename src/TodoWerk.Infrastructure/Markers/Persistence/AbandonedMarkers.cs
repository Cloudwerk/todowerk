using Microsoft.EntityFrameworkCore;
using TodoWerk.Application.Abstractions.Markers;
using TodoWerk.Domain.Hashtags;
using TodoWerk.Domain.Markers;

namespace TodoWerk.Infrastructure.Markers.Persistence;

/// <summary>
/// The Markers of the rules a person has deleted, retired ones included, read the same way by the
/// store and by the shared port — one query rather than two copies of it.
/// </summary>
internal static class AbandonedMarkers
{
    public static async Task<IReadOnlyList<AbandonedMarkerSnapshot>> ReadAsync(
        IQueryable<MarkerRule> ownedBy,
        CancellationToken cancellationToken)
    {
        var deleted = await ownedBy
            .Where(rule => rule.DeletedAt != null)
            .OrderBy(rule => rule.Position)
            .Select(rule => new { rule.Key, rule.Spelling, rule.Marker, rule.RetiredMarker })
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        // Keyed by the Marker, because that is what makes two of these the same thing: the block
        // reader recognises an emoji, not a row. The Hashtag the first row to mention it named is
        // carried along — nothing lists it, and it is there so that a row can be written again
        // under a name that meant something if an undone Remove ever needs one back.
        var markers = new Dictionary<Marker, AbandonedMarkerSnapshot>();

        foreach (var rule in deleted)
        {
            markers.TryAdd(rule.Marker, new AbandonedMarkerSnapshot(rule.Key, rule.Spelling, rule.Marker));

            if (rule.RetiredMarker is { } retired)
            {
                markers.TryAdd(retired, new AbandonedMarkerSnapshot(rule.Key, rule.Spelling, retired));
            }
        }

        return [.. markers.Values];
    }

    /// <summary>
    /// Drops the rows kept only to keep <paramref name="removed"/> known, now that a Remove Markers
    /// has taken those Markers out of every block that carried one.
    /// <para>
    /// The mirror of <see cref="ForgetCoveredAsync"/>, and the same two shapes for the same reason:
    /// a row every Marker of which is now out of the titles goes entirely, and one that was only
    /// <em>retiring</em> a removed Marker keeps its own and forgets the retirement. Deleting that
    /// second row instead would orphan the Marker it still holds — nothing else remembers it, and
    /// the next block reader would stop short of an emoji sitting in real titles.
    /// </para>
    /// </summary>
    public static async Task<int> ForgetRemovedAsync(
        IQueryable<MarkerRule> ownedBy,
        IReadOnlyCollection<Marker> removed,
        CancellationToken cancellationToken)
    {
        if (removed.Count == 0)
        {
            return 0;
        }

        var gone = removed as IReadOnlySet<Marker> ?? new HashSet<Marker>(removed);

        var deleted = await ownedBy
            .Where(rule => rule.DeletedAt != null)
            .AsNoTracking()
            .Select(rule => new { rule.Id, rule.Marker, rule.RetiredMarker })
            .ToListAsync(cancellationToken);

        var covered = deleted
            .Where(rule => gone.Contains(rule.Marker)
                && (rule.RetiredMarker is null || gone.Contains(rule.RetiredMarker.Value)))
            .Select(rule => rule.Id)
            .ToList();

        var retiring = deleted
            .Where(rule => rule.RetiredMarker is { } retired
                && gone.Contains(retired)
                && !covered.Contains(rule.Id))
            .Select(rule => rule.Id)
            .ToList();

        var forgotten = 0;

        if (covered.Count > 0)
        {
            forgotten += await ownedBy
                .Where(rule => covered.Contains(rule.Id))
                .ExecuteDeleteAsync(cancellationToken);
        }

        if (retiring.Count > 0)
        {
            forgotten += await ownedBy
                .Where(rule => retiring.Contains(rule.Id))
                .ExecuteUpdateAsync(
                    set => set.SetProperty(rule => rule.RetiredMarker, (Marker?)null),
                    cancellationToken);
        }

        return forgotten;
    }

    /// <summary>
    /// Forgets what a standing rule now keeps known anyway, so the set of abandoned Markers is
    /// bounded by the emoji a person has abandoned and not taken up again rather than by every rule
    /// they ever deleted. Called when a Marker is given to a rule.
    /// <para>
    /// Two shapes, because a deleted row can be keeping two Markers known and a standing rule can
    /// take up either of them. A row every Marker of which is now covered goes entirely; a row that
    /// was only <em>retiring</em> the taken Marker keeps its own and forgets the retirement.
    /// Deleting that second row instead would orphan the Marker it still holds — nothing else
    /// remembers it, and the next block reader would stop short of an emoji sitting in real titles.
    /// </para>
    /// </summary>
    public static async Task<int> ForgetCoveredAsync(
        IQueryable<MarkerRule> ownedBy,
        Marker taken,
        CancellationToken cancellationToken)
    {
        var deleted = await ownedBy
            .Where(rule => rule.DeletedAt != null)
            .AsNoTracking()
            .Select(rule => new { rule.Id, rule.Marker, rule.RetiredMarker })
            .ToListAsync(cancellationToken);

        var covered = deleted
            .Where(rule => rule.Marker == taken && (rule.RetiredMarker is null || rule.RetiredMarker == taken))
            .Select(rule => rule.Id)
            .ToList();

        // Everything not going entirely, whose retirement the standing rule now covers.
        var retiring = deleted
            .Where(rule => rule.RetiredMarker == taken && !covered.Contains(rule.Id))
            .Select(rule => rule.Id)
            .ToList();

        var forgotten = 0;

        if (covered.Count > 0)
        {
            forgotten += await ownedBy
                .Where(rule => covered.Contains(rule.Id))
                .ExecuteDeleteAsync(cancellationToken);
        }

        if (retiring.Count > 0)
        {
            forgotten += await ownedBy
                .Where(rule => retiring.Contains(rule.Id))
                .ExecuteUpdateAsync(
                    set => set.SetProperty(rule => rule.RetiredMarker, (Marker?)null),
                    cancellationToken);
        }

        return forgotten;
    }
}
