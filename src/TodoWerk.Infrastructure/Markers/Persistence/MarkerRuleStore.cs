using Microsoft.EntityFrameworkCore;
using TodoWerk.Application.Abstractions.Authentication;
using TodoWerk.Application.Abstractions.Markers;
using TodoWerk.Application.Markers;
using TodoWerk.Domain.Hashtags;
using TodoWerk.Domain.Markers;
using TodoWerk.Infrastructure.Persistence;
using TodoWerk.SharedKernel;

namespace TodoWerk.Infrastructure.Markers.Persistence;

/// <summary>
/// The Marker Rule table, from the request path's point of view. Every query is filtered by tenant
/// and user before anything else, because a rule is one person's and there is no surface in
/// TodoWerk that shows another's.
/// </summary>
internal sealed class MarkerRuleStore(TodoWerkDbContext context, TimeProvider timeProvider) : IMarkerRuleStore
{
    /// <summary>
    /// Positions are handed out in steps, so a reorder can swap two numbers without renumbering
    /// the list. Nothing reads the number itself — only the order it produces.
    /// </summary>
    private const int PositionStep = 10;

    public async Task<IReadOnlyList<MarkerRuleRecord>> ListAsync(
        IndexUser user,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);

        var rules = await Standing(user)
            .OrderBy(rule => rule.Position)
            .ThenBy(rule => rule.CreatedAt)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return [.. rules.Select(ToRecord)];
    }

    public Task<IReadOnlyList<AbandonedMarkerSnapshot>> ListAbandonedMarkersAsync(IndexUser user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);

        return AbandonedMarkers.ReadAsync(OwnedBy(user), cancellationToken);
    }

    public async Task<Result<MarkerRuleRecord>> CreateAsync(
        IndexUser user,
        string key,
        string spelling,
        Marker marker,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);

        var existing = await Standing(user).AsNoTracking().ToListAsync(cancellationToken);

        // A bound, not an invariant: two tabs can both pass this and make one rule too many, and no
        // index refuses the second. That is deliberate — the two uniqueness rules below are about
        // what a block means and are enforced by the database, while this is about how long a block
        // is allowed to get, and one rule past it is no different from one rule under it.
        if (existing.Count >= MarkerLimits.MaxRulesPerUser)
        {
            return Result.Failure<MarkerRuleRecord>(MarkerErrors.TooManyRules(MarkerLimits.MaxRulesPerUser));
        }

        var clash = Clash(existing, key, marker, ruleId: null);

        if (clash is not null)
        {
            return Result.Failure<MarkerRuleRecord>(clash);
        }

        var position = existing.Count == 0 ? PositionStep : existing.Max(rule => rule.Position) + PositionStep;

        var rule = MarkerRule.Create(
            user.TenantId,
            user.UserId,
            key,
            spelling,
            marker,
            position,
            timeProvider.GetUtcNow());

        context.Add(rule);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (UniqueIndexViolation.CausedBy(exception))
        {
            // The check above lost a race with another tab. Re-read rather than guess which index
            // it was: the answer names the Hashtag holding the Marker, and that is worth a query.
            context.Entry(rule).State = EntityState.Detached;

            var now = await Standing(user).AsNoTracking().ToListAsync(cancellationToken);

            return Result.Failure<MarkerRuleRecord>(
                Clash(now, key, marker, ruleId: null) ?? MarkerErrors.RuleAlreadyExists(spelling));
        }

        // A standing rule keeps this Marker known from now on, so the deleted rules that were
        // keeping it known can go.
        await AbandonedMarkers.ForgetCoveredAsync(OwnedBy(user), marker, cancellationToken);

        return Result.Success(ToRecord(rule));
    }

    public async Task<Result<MarkerRuleRecord>> ChangeMarkerAsync(
        IndexUser user,
        Guid ruleId,
        Marker marker,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);

        var rule = await Standing(user).FirstOrDefaultAsync(row => row.Id == ruleId, cancellationToken);

        if (rule is null)
        {
            return Result.Failure<MarkerRuleRecord>(MarkerErrors.RuleNotFound);
        }

        var others = await Standing(user)
            .Where(row => row.Id != ruleId)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var clash = Clash(others, key: null, marker, ruleId);

        if (clash is not null)
        {
            return Result.Failure<MarkerRuleRecord>(clash);
        }

        rule.ChangeMarker(marker);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (UniqueIndexViolation.CausedBy(exception))
        {
            // Lost a race with another tab for the Marker. Re-read to name the Hashtag that took
            // it, because "that emoji is taken" without saying by what is a dead end.
            await context.Entry(rule).ReloadAsync(cancellationToken);

            var now = await Standing(user)
                .Where(row => row.Id != ruleId)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            return Result.Failure<MarkerRuleRecord>(
                Clash(now, key: null, marker, ruleId) ?? MarkerErrors.MarkerInUse(rule.Spelling));
        }

        await AbandonedMarkers.ForgetCoveredAsync(OwnedBy(user), marker, cancellationToken);

        return Result.Success(ToRecord(rule));
    }

    public async Task<Result<MarkerRuleRecord>> MoveAsync(
        IndexUser user,
        Guid ruleId,
        MarkerRuleMove move,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);

        var ordered = await Standing(user)
            .OrderBy(rule => rule.Position)
            .ThenBy(rule => rule.CreatedAt)
            .ToListAsync(cancellationToken);

        var index = ordered.FindIndex(rule => rule.Id == ruleId);

        if (index < 0)
        {
            return Result.Failure<MarkerRuleRecord>(MarkerErrors.RuleNotFound);
        }

        var neighbour = move is MarkerRuleMove.Up ? index - 1 : index + 1;

        if (neighbour < 0 || neighbour >= ordered.Count)
        {
            return Result.Failure<MarkerRuleRecord>(MarkerErrors.RuleCannotMove);
        }

        // Swap the two positions rather than renumbering — unless the numbers are not an order:
        // two rules that ended up sharing one would swap into the same tie they started in, so the
        // list is renumbered first in that one case and left alone otherwise.
        if (!StrictlyAscending(ordered))
        {
            Renumber(ordered);
        }

        var moving = ordered[index];
        var other = ordered[neighbour];
        var movingPosition = moving.Position;

        moving.MoveTo(other.Position);
        other.MoveTo(movingPosition);

        await context.SaveChangesAsync(cancellationToken);

        return Result.Success(ToRecord(moving));
    }

    public async Task<bool> DeleteAsync(IndexUser user, Guid ruleId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);

        // Marked rather than removed: the Marker is still at the front of every title this rule
        // was applied to, and has to go on being read as part of a block. Set on the row so it
        // lands whatever else is happening to it.
        var now = timeProvider.GetUtcNow();

        var deleted = await Standing(user)
            .Where(rule => rule.Id == ruleId)
            .ExecuteUpdateAsync(set => set.SetProperty(rule => rule.DeletedAt, now), cancellationToken);

        return deleted > 0;
    }

    /// <summary>
    /// Whichever of the two uniqueness rules this would break, worded so the person can act on it:
    /// which Hashtag already has a rule, or which Hashtag holds the Marker.
    /// </summary>
    private static Error? Clash(IReadOnlyList<MarkerRule> existing, string? key, Marker marker, Guid? ruleId)
    {
        foreach (var rule in existing)
        {
            if (rule.Id == ruleId)
            {
                continue;
            }

            if (key is not null && string.Equals(rule.Key, key, StringComparison.Ordinal))
            {
                return MarkerErrors.RuleAlreadyExists(rule.Spelling);
            }

            if (rule.Marker == marker)
            {
                return MarkerErrors.MarkerInUse(rule.Spelling);
            }
        }

        return null;
    }

    private static bool StrictlyAscending(List<MarkerRule> ordered)
    {
        for (var index = 1; index < ordered.Count; index++)
        {
            if (ordered[index].Position <= ordered[index - 1].Position)
            {
                return false;
            }
        }

        return true;
    }

    private static void Renumber(List<MarkerRule> ordered)
    {
        for (var index = 0; index < ordered.Count; index++)
        {
            ordered[index].MoveTo((index + 1) * PositionStep);
        }
    }

    private IQueryable<MarkerRule> OwnedBy(IndexUser user) =>
        context.Set<MarkerRule>()
            .Where(rule => rule.TenantId == user.TenantId && rule.UserId == user.UserId);

    /// <summary>The rules that are rules: this person's, and not deleted.</summary>
    private IQueryable<MarkerRule> Standing(IndexUser user) => OwnedBy(user).Where(rule => rule.DeletedAt == null);

    private static MarkerRuleRecord ToRecord(MarkerRule rule) =>
        new(rule.Id, rule.Key, rule.Spelling, rule.Marker.Text, rule.RetiredMarker?.Text, rule.Position);
}
