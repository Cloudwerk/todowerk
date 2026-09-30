using TodoWerk.Domain.Licensing;

namespace TodoWerk.Application.Licensing;

/// <summary>
/// Everything the screens need to know about the signed-in person's Licence, read once a session
/// from one endpoint. One shape and one source, so the banner, the Tenant Overview panel and the
/// consent invitation cannot come to different conclusions about the same person.
/// <para>
/// What is deliberately not here: the licence id, the usage secret, the channel, and any count of
/// people. The first two are server-side facts about a customer's purchase; the last is exactly
/// the figure the statistics floor exists to withhold, and a panel that respected the floor for
/// one number and not another would be wrong in one direction or the other.
/// </para>
/// </summary>
/// <param name="Kind">
/// Which of the three kinds licenses this person, or null on a Self-Host — where there is no
/// Licence and the panel is absent rather than rendered empty.
/// </param>
/// <param name="EndsAt">When the term runs out, or null when the portal named no end.</param>
/// <param name="Banner">Which sentence the Workbench shows, if any.</param>
/// <param name="MayOfferTenantConsent">
/// Whether the invitation to grant Tenant Consent may be shown. True under a Tenant Licence and a
/// Trial, false under a Personal Licence, and true on a Self-Host — where the invitation's
/// behaviour is unchanged, because there is no Licence to make it conditional on.
/// </param>
/// <param name="PurchaseUrl">
/// Where the browser's purchase links point, when ManagementPortal delivered one. Null means the
/// banner carries no link at all rather than a dead one, and it is the ordinary state rather than
/// a fault: the portal sends no address where it sells nothing for TodoWerk. A denied person
/// never reads this endpoint — their link travels on the refusal itself, as a problem extension.
/// </param>
public sealed record LicenceDto(
    LicenceKind? Kind,
    DateTimeOffset? EndsAt,
    LicenceBannerState Banner,
    bool MayOfferTenantConsent,
    Uri? PurchaseUrl);
