using Microsoft.EntityFrameworkCore;
using TodoWerk.Application.Onboarding;
using TodoWerk.Domain.Onboarding;
using TodoWerk.Infrastructure.Persistence;

namespace TodoWerk.Infrastructure.Onboarding.Persistence;

internal sealed class TenantConsentStore(TodoWerkDbContext context, TimeProvider timeProvider) : ITenantConsentStore
{
    public Task<bool> IsGrantedAsync(string tenantId, CancellationToken cancellationToken) =>
        context.Set<TenantConsentGrant>()
            .AsNoTracking()
            .AnyAsync(grant => grant.TenantId == tenantId, cancellationToken);

    public async Task RecordGrantAsync(string tenantId, CancellationToken cancellationToken)
    {
        if (await IsGrantedAsync(tenantId, cancellationToken))
        {
            return;
        }

        var grant = TenantConsentGrant.Granted(tenantId, timeProvider.GetUtcNow());

        context.Add(grant);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (UniqueIndexViolation.CausedBy(exception))
        {
            // A grant arrived between the check and the insert. The unique index refused the second
            // one, which is the outcome wanted: the tenant is approved, once, at the first moment.
            // Narrowed to that one cause: a deadlock swallowed here would leave the tenant
            // unapproved while the screen said otherwise.
            context.Entry(grant).State = EntityState.Detached;
        }
    }
}
