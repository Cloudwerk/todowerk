using Microsoft.EntityFrameworkCore;
using TodoWerk.Application.Abstractions.Authentication;
using TodoWerk.Application.Abstractions.Markers;
using TodoWerk.Domain.Hashtags;
using TodoWerk.Domain.Markers;
using TodoWerk.Infrastructure.Persistence;

namespace TodoWerk.Infrastructure.Markers.Persistence;

/// <summary>
/// What a finished Change does to Marker Rules — carry one to a new name, delete the losers of a
/// Merge, forget a retired Marker that has been written out. None of it touches a task.
/// </summary>
internal sealed class MarkerRuleMover(TodoWerkDbContext context, TimeProvider timeProvider) : IMarkerRuleMover
{
    public async Task<MarkerRuleCarry> CarryRuleAsync(
        IndexUser user,
        string fromKey,
        string toKey,
        string toSpelling,
        Marker expected,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);

        if (string.Equals(fromKey, toKey, StringComparison.Ordinal))
        {
            // Normalise Casing moves nothing, because the key does not change (ADR-0014). The rule
            // is already where it is going, so there is nothing on the source key to answer for.
            return MarkerRuleCarry.NothingToCarry;
        }

        var rules = await Standing(user)
            .Where(rule => rule.Key == fromKey || rule.Key == toKey)
            .ToListAsync(cancellationToken);

        var source = rules.Find(rule => string.Equals(rule.Key, fromKey, StringComparison.Ordinal));

        // Asked before the target, and by Marker rather than by presence. A resumed run finds the
        // rule already on the target; what is left on the source key is either nothing or a rule
        // the person has since made there, and a Marker is unique across their standing rules, so
        // the one this Change is about is the only one that can be wearing this Marker.
        if (source is null || source.Marker != expected)
        {
            return MarkerRuleCarry.NothingToCarry;
        }

        // The target's own rule wins. A Merge deletes the losers separately, and a Rename onto a
        // name that already has a rule is a Merge.
        if (rules.Exists(rule => string.Equals(rule.Key, toKey, StringComparison.Ordinal)))
        {
            return MarkerRuleCarry.TargetWins;
        }

        source.CarryTo(toKey, toSpelling);

        await context.SaveChangesAsync(cancellationToken);

        return MarkerRuleCarry.Carried;
    }

    public async Task<int> DeleteRulesAsync(
        IndexUser user,
        IReadOnlyCollection<string> keys,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(keys);

        if (keys.Count == 0)
        {
            return 0;
        }

        var wanted = keys.ToList();
        var now = timeProvider.GetUtcNow();

        // Marked rather than removed, as a person's own delete is: the losing Marker is still in
        // every block it was applied to.
        return await Standing(user)
            .Where(rule => wanted.Contains(rule.Key))
            .ExecuteUpdateAsync(set => set.SetProperty(rule => rule.DeletedAt, now), cancellationToken);
    }

    public async Task<int> RecordWrittenMarkersAsync(
        IndexUser user,
        IReadOnlyCollection<WrittenMarker> written,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(written);

        if (written.Count == 0)
        {
            return 0;
        }

        var byKey = written.ToDictionary(marker => marker.Key, marker => marker.Marker, StringComparer.Ordinal);
        var keys = byKey.Keys.ToList();

        var rules = await Standing(user)
            .Where(rule => keys.Contains(rule.Key))
            .ToListAsync(cancellationToken);

        var now = timeProvider.GetUtcNow();
        var remembered = new List<Marker>();

        foreach (var rule in rules)
        {
            var before = rule.RetiredMarker;

            rule.Wrote(byKey[rule.Key]);

            if (rule.RetiredMarker is { } again)
            {
                // The rule remembers a Marker again — which is what an undo does. Any row keeping
                // that Marker known on its behalf is now not only redundant but harmful: an
                // abandoned Marker is deliberately never swapped, so leaving it would stop the
                // swap the rule has just re-armed.
                remembered.Add(again);
            }
            else if (before is { } forgotten && forgotten != rule.Marker)
            {
                // What the rule stops remembering is still at the front of every title that lost
                // the Hashtag before the plan was drawn — never planned, never reached, never
                // swapped. It stays known the way a deleted rule's Marker does: through a row
                // listed nowhere and applied nowhere, so no later Apply reads it as the start of a
                // second block.
                var tombstone = MarkerRule.Create(
                    rule.TenantId,
                    rule.UserId,
                    rule.Key,
                    rule.Spelling,
                    forgotten,
                    rule.Position,
                    now);

                tombstone.Delete(now);
                context.Add(tombstone);
            }
        }

        foreach (var marker in remembered)
        {
            await AbandonedMarkers.ForgetCoveredAsync(OwnedBy(user), marker, cancellationToken);
        }

        // Idempotent by construction: telling a rule twice what its titles carry says the same
        // thing twice, which is what lets a run resumed after a crash repeat this without harm.
        if (rules.Count > 0)
        {
            await context.SaveChangesAsync(cancellationToken);
        }

        return rules.Count;
    }

    public async Task<int> ForgetRemovedMarkersAsync(
        IndexUser user,
        IReadOnlyCollection<Marker> removed,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(removed);

        return await AbandonedMarkers.ForgetRemovedAsync(OwnedBy(user), removed, cancellationToken);
    }

    public async Task<int> RememberRemovedMarkersAsync(
        IndexUser user,
        IReadOnlyCollection<AbandonedMarkerSnapshot> restored,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(restored);

        if (restored.Count == 0)
        {
            return 0;
        }

        // What is already known, standing rules included: a Marker a standing rule wears needs no
        // row of its own, and one a row is already keeping known needs no second.
        var already = await OwnedBy(user)
            .AsNoTracking()
            .Select(rule => new { rule.Marker, rule.RetiredMarker })
            .ToListAsync(cancellationToken);

        var known = new HashSet<Marker>();

        foreach (var rule in already)
        {
            known.Add(rule.Marker);

            if (rule.RetiredMarker is { } retired)
            {
                known.Add(retired);
            }
        }

        var now = timeProvider.GetUtcNow();
        var written = 0;

        foreach (var marker in restored)
        {
            if (!known.Add(marker.Marker))
            {
                continue;
            }

            // Position at the end: nothing reads a deleted rule's place, and the list it would sit
            // in does not include it.
            var tombstone = MarkerRule.Create(
                user.TenantId,
                user.UserId,
                marker.Key,
                marker.Spelling,
                marker.Marker,
                int.MaxValue,
                now);

            tombstone.Delete(now);
            context.Add(tombstone);
            written++;
        }

        if (written > 0)
        {
            await context.SaveChangesAsync(cancellationToken);
        }

        return written;
    }

    private IQueryable<MarkerRule> OwnedBy(IndexUser user) =>
        context.Set<MarkerRule>()
            .Where(rule => rule.TenantId == user.TenantId && rule.UserId == user.UserId);

    private IQueryable<MarkerRule> Standing(IndexUser user) => OwnedBy(user).Where(rule => rule.DeletedAt == null);
}
