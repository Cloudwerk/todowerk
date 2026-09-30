using TodoWerk.SharedKernel;

namespace TodoWerk.Domain.Onboarding;

/// <summary>
/// That an administrator approved TodoWerk's delegated permission grant for a whole tenant,
/// through TodoWerk's own admin-consent flow.
/// <para>
/// It records only that the approval happened here. TodoWerk cannot see a grant made in the Entra
/// portal — seeing one costs a directory permission to answer a boolean, which is the trade
/// ADR-0007 declined in the other direction — so the absence of this row means "not approved
/// through TodoWerk", never "not approved". Every surface that reads it has to say so.
/// </para>
/// <para>
/// A row per tenant and not per person: nobody is named here, and there is deliberately no record
/// of which administrator clicked. It carries no user id for that reason — the one persisted
/// entity that does not, declared as such in <c>PersistenceConventionTests</c>.
/// </para>
/// </summary>
public sealed class TenantConsentGrant : Entity<Guid>
{
    private TenantConsentGrant(Guid id, string tenantId, DateTimeOffset grantedAt)
        : base(id)
    {
        TenantId = tenantId;
        GrantedAt = grantedAt;
    }

    public string TenantId { get; private set; }

    public DateTimeOffset GrantedAt { get; private set; }

    public static TenantConsentGrant Granted(string tenantId, DateTimeOffset grantedAt) =>
        new(Guid.CreateVersion7(), tenantId, grantedAt);
}
