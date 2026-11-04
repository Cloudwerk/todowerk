using System.Globalization;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace TodoWerk.Infrastructure.Onboarding;

/// <summary>
/// The address somebody whose own approval was refused can hand to an administrator: the start of
/// the Tenant Consent round trip, carrying the tenant it is for
/// ([ADR-0015](../../../docs/adr/0015-an-approval-link-carries-its-tenant.md)).
/// <para>
/// It exists because the administrator who can approve is very often not the person who was
/// refused, and arrives without a TodoWerk session — by design, when the organisation keeps
/// administration on a separate account. Without a session the round trip binds to no tenant and
/// can record nothing (<see cref="TenantConsentFlow.Start"/>), so an approval made that way leaves
/// the invitation on screen for everybody. The link carries what the session would have: the
/// tenant, read off a Teams single sign-on token this deployment had already validated, in a
/// payload only this deployment can read or write.
/// </para>
/// <para>
/// Stateless on purpose. A token in the address rather than an invitation row means nothing to
/// store, sweep or erase, and nothing in the database that names a tenant before anybody in it has
/// approved anything. What that costs is revocation: a link cannot be withdrawn, only outlived,
/// which is why it lasts a week rather than indefinitely.
/// </para>
/// <para>
/// The callback's rule is untouched: a grant is recorded only when the state matches and Microsoft
/// names the tenant the flow began for. What is accepted is narrower than it looks — anybody
/// holding the link can start a flow bound to that tenant and then answer the callback themselves,
/// exactly as a signed-in member of the tenant already can (ADR-0008). Somebody outside the tenant
/// only ever holds a link because somebody inside handed it over, and what a forged answer buys is
/// the same as it always did: the invitation hidden for that one tenant, and nothing else.
/// </para>
/// </summary>
public sealed class ApprovalLink(IDataProtectionProvider dataProtection, TimeProvider timeProvider)
{
    /// <summary>The query parameter on <see cref="TenantConsentFlow.StartPath"/> that carries the token.</summary>
    public const string Parameter = "approval";

    /// <summary>
    /// A week: long enough for a request to reach an administrator who is away for a few days and
    /// back again, and short enough that a link forwarded around an organisation stops meaning
    /// anything before it is forgotten about. An expired link still leads to Microsoft's approval;
    /// it just cannot say whose it was.
    /// </summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);

    /// <summary>
    /// Its own purpose, so no other payload this deployment protects — the consent state cookie
    /// above all, which also holds a tenant id — can be presented as an Approval Link, and the
    /// other way round.
    /// </summary>
    private readonly IDataProtector _protector = dataProtection.CreateProtector("TodoWerk.ApprovalLink.v1");

    /// <summary>The Approval Link for one tenant, on this deployment.</summary>
    /// <param name="tenantId">
    /// The tenant to approve for. Only ever one read off an identity this deployment has already
    /// validated — never a value a caller supplied — because the link is believed on the strength
    /// of having been minted here, and a link minted for a tenant somebody typed would let them
    /// start a bound flow for an organisation they are not in.
    /// </param>
    /// <param name="origin">This deployment's own scheme and host, as the request reached it.</param>
    public Uri For(string tenantId, Uri origin)
    {
        ArgumentException.ThrowIfNullOrEmpty(tenantId);
        ArgumentNullException.ThrowIfNull(origin);

        var expiresAt = (timeProvider.GetUtcNow() + Lifetime).ToUnixTimeSeconds();

        var token = _protector.Protect(
            $"{tenantId}|{expiresAt.ToString(CultureInfo.InvariantCulture)}");

        return new Uri(origin, $"{TenantConsentFlow.StartPath}?{Parameter}={Uri.EscapeDataString(token)}");
    }

    /// <summary>
    /// The tenant an Approval Link was minted for, or null when there is no reason to believe it
    /// was: no token, one this deployment cannot decrypt — altered, minted for another purpose, or
    /// protected by a key ring it no longer holds — or one past its week.
    /// </summary>
    public string? TenantFrom(string? token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return null;
        }

        string payload;

        try
        {
            payload = _protector.Unprotect(token);
        }
        catch (CryptographicException)
        {
            return null;
        }

        var parts = payload.Split('|');

        if (parts.Length != 2
            || parts[0].Length == 0
            || !long.TryParse(parts[1], CultureInfo.InvariantCulture, out var expiresAt)
            || timeProvider.GetUtcNow().ToUnixTimeSeconds() > expiresAt)
        {
            return null;
        }

        return parts[0];
    }
}
