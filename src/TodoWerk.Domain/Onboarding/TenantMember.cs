using TodoWerk.SharedKernel;

namespace TodoWerk.Domain.Onboarding;

/// <summary>
/// The record that a person has used TodoWerk: their tenant, their Entra ID object id, when they
/// first signed in and when they last did. Nothing else — no name, no address, no user agent, and
/// no history between those two moments.
/// <para>
/// Written only when a human signs in. A background scan or delta sync running on a timer is not
/// somebody using the product, and that distinction is the entire meaning of the activity counts
/// the Tenant Overview reports — so it is enforced by where the write lives (the sign-in event)
/// rather than by a flag a caller could set wrongly.
/// </para>
/// <para>
/// The tenant is the directory tenant from the sign-in's <c>tid</c> claim, never a domain derived
/// from a UPN: one tenant commonly holds several verified domains, and a guest's UPN names a
/// different organisation entirely.
/// </para>
/// </summary>
public sealed class TenantMember : Entity<Guid>
{
    private TenantMember(
        Guid id,
        string tenantId,
        string userId,
        DateTimeOffset firstSignedInAt)
        : base(id)
    {
        TenantId = tenantId;
        UserId = userId;
        FirstSignedInAt = firstSignedInAt;
        LastSignedInAt = firstSignedInAt;
    }

    public string TenantId { get; private set; }

    /// <summary>
    /// The Entra ID object id — a pseudonymous directory identifier, and the only thing here that
    /// names anybody. Null once the row has been anonymised: erasure keeps the tenant's cumulative
    /// count without keeping anything that could point back at a person.
    /// </summary>
    public string? UserId { get; private set; }

    public DateTimeOffset FirstSignedInAt { get; private set; }

    /// <summary>
    /// The most recent interactive sign-in, and the only thing the activity windows are computed
    /// from. Null once anonymised, which is what keeps somebody who has left out of "active
    /// recently" while their first sign-in still counts toward the total.
    /// </summary>
    public DateTimeOffset? LastSignedInAt { get; private set; }

    public static TenantMember FirstSignIn(string tenantId, string userId, DateTimeOffset signedInAt) =>
        new(Guid.CreateVersion7(), tenantId, userId, signedInAt);

    /// <summary>
    /// Moves the last moment on and leaves the first alone. There is deliberately no third
    /// timestamp and no row per visit: two moments answer every trailing window the product
    /// reports, and a log of sign-ins would be a record of somebody's working patterns.
    /// </summary>
    public void SignedInAgain(DateTimeOffset signedInAt)
    {
        LastSignedInAt = signedInAt;
    }

    /// <summary>
    /// Forgets who this was, in place. The tenant and the first moment stay, so the count of
    /// people who have ever signed in is not eroded by somebody exercising erasure; the object id
    /// and the last moment go, so nothing here identifies anybody and nothing counts them as
    /// present. Nothing records that it happened: no screen anywhere shows that somebody left.
    /// </summary>
    public void Anonymise()
    {
        UserId = null;
        LastSignedInAt = null;
    }
}
