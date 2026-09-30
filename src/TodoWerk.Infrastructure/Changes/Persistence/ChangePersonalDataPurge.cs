using Microsoft.EntityFrameworkCore;
using TodoWerk.Application.Abstractions.Authentication;
using TodoWerk.Application.Abstractions.Erasure;
using TodoWerk.Domain.Changes;
using TodoWerk.Infrastructure.Persistence;

namespace TodoWerk.Infrastructure.Changes.Persistence;

/// <summary>
/// Destroys one person's Changes, with the plan rows and journal entries hanging off them.
/// <para>
/// The journals are the second sensitive thing TodoWerk holds: every entry carries a task title
/// twice, before and after. Losing them costs the ability to undo, which is exactly what somebody
/// asking to be forgotten is asking for.
/// </para>
/// </summary>
internal sealed class ChangePersonalDataPurge(TodoWerkDbContext context) : IPersonalDataPurge
{
    /// <summary>
    /// Same bound and same reason as the index purge: a Change running for this person stops when
    /// the row it claimed disappears, but it may journal one more task first. Reaching the bound is
    /// reported rather than mistaken for finishing.
    /// </summary>
    private const int MaximumPasses = 3;

    public string Describes => "the change history and its journals";

    public async Task<PurgeOutcome> PurgeAsync(IndexUser user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);

        var removed = 0;

        for (var pass = 0; pass < MaximumPasses; pass++)
        {
            // The children first, then their Change. Both cascade off it, so the last statement
            // would take them anyway — but a plan row or a journal entry left behind by a
            // half-applied migration is precisely the kind of residue erasure must not leave.
            var journal = await context.Set<ChangeJournalEntry>()
                .Where(entry => entry.TenantId == user.TenantId && entry.UserId == user.UserId)
                .ExecuteDeleteAsync(cancellationToken);

            var plan = await context.Set<ChangePlanItem>()
                .Where(item => item.TenantId == user.TenantId && item.UserId == user.UserId)
                .ExecuteDeleteAsync(cancellationToken);

            var changes = await context.Set<Change>()
                .Where(change => change.TenantId == user.TenantId && change.UserId == user.UserId)
                .ExecuteDeleteAsync(cancellationToken);

            var found = journal + plan + changes;
            removed += found;

            if (found == 0)
            {
                return PurgeOutcome.Complete(removed);
            }
        }

        return PurgeOutcome.Unfinished(removed);
    }
}
