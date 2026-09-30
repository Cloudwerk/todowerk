using Microsoft.EntityFrameworkCore;
using TodoWerk.Application.Abstractions.Authentication;
using TodoWerk.Application.Onboarding;
using TodoWerk.Domain.Onboarding;
using TodoWerk.Infrastructure.Persistence;

namespace TodoWerk.Infrastructure.Onboarding.Persistence;

/// <summary>
/// The Tenant Member table, written at sign-in and read by the overview. Both roles on one class
/// because they are one table and there is one question to keep consistent between them: what
/// counts as somebody being here.
/// </summary>
internal sealed class TenantMemberStore(TodoWerkDbContext context, TimeProvider timeProvider)
    : ITenantMemberStore, ITenantOverviewReader
{
    public async Task RecordSignInAsync(IndexUser user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);

        var now = timeProvider.GetUtcNow();

        // Move the moment on first, so the overwhelmingly common case — somebody who has signed in
        // before — is one statement and touches nothing else. It also settles the ordering question
        // the other way round would raise: read-then-decide leaves a window in which two sign-ins
        // both decide to insert.
        var moved = await Identified(user)
            .ExecuteUpdateAsync(
                set => set.SetProperty(member => member.LastSignedInAt, (DateTimeOffset?)now),
                cancellationToken);

        if (moved > 0)
        {
            return;
        }

        var member = TenantMember.FirstSignIn(user.TenantId, user.UserId, now);

        context.Add(member);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (UniqueIndexViolation.CausedBy(exception))
        {
            // Two first sign-ins at once — two tabs, or a retried callback. The unique index picked
            // a winner; this is the loser, and what it wanted has already happened bar the moment.
            // Narrowed to that one cause on purpose: a deadlock or a timeout caught here would be
            // reported as a recorded sign-in that never happened.
            context.Entry(member).State = EntityState.Detached;

            await Identified(user)
                .ExecuteUpdateAsync(
                    set => set.SetProperty(row => row.LastSignedInAt, (DateTimeOffset?)now),
                    cancellationToken);
        }
    }

    public async Task<bool> AnonymiseAsync(IndexUser user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);

        // Loaded and asked, rather than updated in place: what anonymising means is the entity's
        // own rule, and a `SetProperty` pair here would be a second definition of it that could
        // drift. Nothing else contends for this row — the person is being erased.
        var member = await Identified(user).FirstOrDefaultAsync(cancellationToken);

        if (member is null)
        {
            return false;
        }

        member.Anonymise();

        await context.SaveChangesAsync(cancellationToken);

        return true;
    }

    public async Task<IReadOnlyList<IndexUser>> FindDormantAsync(
        TimeSpan dormancyWindow,
        int batchSize,
        CancellationToken cancellationToken)
    {
        var dormantBefore = timeProvider.GetUtcNow() - dormancyWindow;

        var dormant = await context.Set<TenantMember>()
            .AsNoTracking()
            .Where(member => member.UserId != null
                && member.LastSignedInAt != null
                && member.LastSignedInAt < dormantBefore)
            // Longest gone first, so a backlog is worked in the order the promise came due.
            .OrderBy(member => member.LastSignedInAt)
            .Take(batchSize)
            .Select(member => new { member.TenantId, member.UserId })
            .ToListAsync(cancellationToken);

        return [.. dormant.Select(row => new IndexUser(row.TenantId, row.UserId!))];
    }

    public async Task<TenantMemberCounts> ReadAsync(
        string tenantId,
        IReadOnlyList<TimeSpan> windows,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(windows);

        var now = timeProvider.GetUtcNow();

        var tenant = context.Set<TenantMember>()
            .AsNoTracking()
            .Where(member => member.TenantId == tenantId);

        var totals = await tenant
            .GroupBy(_ => 1)
            .Select(group => new
            {
                MemberCount = group.Count(),
                IdentifiableMemberCount = group.Count(member => member.UserId != null),
                // Nullable on purpose, and not because the column is: MIN over no rows is NULL, and
                // whether an empty tenant comes back as zero rows or as one row of NULL aggregates
                // depends on how the provider translates the constant grouping. Projected as
                // non-nullable, the second shape fails materialisation — a 500 for the one tenant
                // whose membership write failed — where this reads back as null under either.
                FirstSignedInAt = group.Min(member => (DateTimeOffset?)member.FirstSignedInAt),
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (totals is null || totals.MemberCount == 0)
        {
            return new TenantMemberCounts(0, 0, [.. windows.Select(_ => 0)], FirstSignedInAt: null);
        }

        var active = new List<int>(windows.Count);

        // One indexed count per window rather than one query with a conditional sum per window: the
        // set is one row per person in one tenant, so each of these is cheap, and a projection
        // written for exactly three windows would stop the windows being a list.
        foreach (var window in windows)
        {
            var since = now - window;

            active.Add(await tenant.CountAsync(
                // An anonymised row has no last moment, so somebody who has been forgotten counts
                // once in the total and never as present.
                member => member.LastSignedInAt != null && member.LastSignedInAt >= since,
                cancellationToken));
        }

        return new TenantMemberCounts(
            totals.MemberCount,
            totals.IdentifiableMemberCount,
            active,
            totals.FirstSignedInAt);
    }

    /// <summary>The one row that still names this person, if there is one.</summary>
    private IQueryable<TenantMember> Identified(IndexUser user) =>
        context.Set<TenantMember>()
            .Where(member => member.TenantId == user.TenantId && member.UserId == user.UserId);
}
