namespace TodoWerk.Domain.Licensing;

/// <summary>
/// What ManagementPortal last said about one person, in TodoWerk's own words. Not a Licence
/// record: TodoWerk holds no Licence and no licence key, only the most recent answer for one
/// person in one tenant ([ADR-0001](../../../docs/adr/0001-single-entitlement-authority.md)).
/// <para>
/// The purchase URL is deliberately not here. It says where <em>the portal</em> sells TodoWerk,
/// which is the same address for everybody and is delivered on a refusal too — where there is no
/// Licence at all — so it belongs to the answer rather than to what was found. It is on
/// <see cref="LicenceResolution"/>.
/// </para>
/// </summary>
/// <param name="Kind">Who the Licence covers.</param>
/// <param name="EndsAt">
/// When the term runs out, as an instant. Null when the portal named none, which is why every
/// reader here treats "no end date" as "not ending soon" rather than as zero.
/// </param>
/// <param name="LicenceId">
/// The portal's own id for the Licence, which seat usage is reported against. Server-side only:
/// it identifies a customer's purchase and has no business in a browser.
/// </param>
/// <param name="UsageSecret">
/// The per-Licence secret a usage token is keyed on. The reason TodoWerk never needs a licence
/// key, and the reason one person cannot be correlated across two Licences. Server-side only, and
/// deliberately never a field on anything the client can read.
/// </param>
public sealed record Licence(
    LicenceKind Kind,
    DateTimeOffset? EndsAt,
    string LicenceId,
    string UsageSecret)
{
    /// <summary>
    /// Whether TodoWerk may invite somebody to grant Tenant Consent. True under a Tenant Licence
    /// and during a Trial, false under a Personal Licence: paying for one seat is not standing to
    /// approve TodoWerk for an organisation, and whether to buy for the organisation is part of
    /// what a Trial is trying (ADR-0012).
    /// </summary>
    public bool MayOfferTenantConsent => Kind is LicenceKind.Tenant or LicenceKind.Trial;
}
