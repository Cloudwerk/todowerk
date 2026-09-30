using System.Globalization;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using TodoWerk.Infrastructure.Authentication;

namespace TodoWerk.Infrastructure.Onboarding;

/// <summary>
/// The two ends of the Tenant Consent round trip: the admin-consent URL an administrator is sent
/// to, and the check that decides whether what comes back may be believed.
/// <para>
/// The check exists because Microsoft's own guidance says so. The <c>tenant</c> parameter on the
/// way back "can be updated and sent by bad actors to impersonate a response to your app", so a
/// callback that recorded whatever tenant it was handed would let anybody switch off another
/// organisation's invitation to approve. So the flow is started with a state that is remembered in
/// a protected, short-lived cookie, and the callback is only believed when the state matches and
/// names the same tenant the redirect does.
/// </para>
/// </summary>
public sealed class TenantConsentFlow(
    IOptions<EntraIdOptions> entra,
    IDataProtectionProvider dataProtection,
    TimeProvider timeProvider)
{
    /// <summary>Where an administrator is sent from inside TodoWerk.</summary>
    public const string StartPath = "/auth/tenant-consent";

    /// <summary>
    /// Where Microsoft comes back to. It has to be registered on the app registration exactly as
    /// spelled here, alongside the sign-in and sign-out redirect URIs
    /// (CONTRIBUTING § Development setup).
    /// </summary>
    public const string CallbackPath = "/auth/tenant-consent/callback";

    /// <summary>Short: the whole round trip is one administrator reading one consent screen.</summary>
    private static readonly TimeSpan StateLifetime = TimeSpan.FromMinutes(15);

    /// <summary>Named like every other cookie this application sets, and scoped to the callback.</summary>
    public const string StateCookieName = "todowerk.tenant-consent";

    /// <summary>
    /// Stands in the state for a flow that began without a session, so nothing knew whose tenant
    /// it was for. A character no tenant id can contain, so it can never accidentally match one
    /// coming back — which is the whole of what keeps such a flow out of the grant table.
    /// </summary>
    private const string NoTenantKnown = "*";

    private readonly IDataProtector _protector = dataProtection.CreateProtector("TodoWerk.TenantConsent.v1");

    /// <summary>
    /// The admin-consent URL, and the cookie value that remembers which tenant it was for.
    /// </summary>
    /// <param name="tenantId">
    /// The tenant being approved, from the session of whoever started the flow — or null when
    /// nobody is signed in, which is the person who has just been told their organisation has to
    /// approve TodoWerk before they can use it. Then the flow is addressed to this deployment's
    /// own authority and Microsoft resolves the tenant from whoever signs in to approve.
    /// <para>
    /// A flow started that way records no grant when it comes back, whatever it comes back with.
    /// The <c>tenant</c> parameter on the way back is forgeable — Microsoft's own guidance says so
    /// and this class exists to act on it — and the state is the only thing that makes it
    /// believable. Without a session there is nothing to bind the state to, so the round trip can
    /// tell somebody what happened and nothing more. The consequence is the one already written
    /// down: TodoWerk cannot see consent granted outside itself, and an administrator who approves
    /// this way keeps seeing the invitation until somebody approves from inside the product.
    /// </para>
    /// </param>
    public (Uri Url, string State, string StateCookie) Start(string? tenantId, Uri redirectUri)
    {
        ArgumentNullException.ThrowIfNull(redirectUri);

        var state = RandomNumberGenerator.GetHexString(32, lowercase: true);

        var authority = tenantId ?? entra.Value.TenantId;

        var url = new UriBuilder(new Uri(new Uri(entra.Value.Instance), $"{authority}/v2.0/adminconsent"))
        {
            Query = string.Join('&',
                $"client_id={Uri.EscapeDataString(entra.Value.ClientId)}",
                // Exactly what sign-in asks each person for, and nothing wider: approving for the
                // organisation has to be the same decision every individual was making.
                $"scope={Uri.EscapeDataString(string.Join(' ', GraphScopes.TenantConsent))}",
                $"redirect_uri={Uri.EscapeDataString(redirectUri.ToString())}",
                $"state={state}"),
        }.Uri;

        var expiresAt = (timeProvider.GetUtcNow() + StateLifetime).ToUnixTimeSeconds();

        return (url, state, _protector.Protect(
            $"{tenantId ?? NoTenantKnown}|{state}|{expiresAt.ToString(CultureInfo.InvariantCulture)}"));
    }

    /// <summary>
    /// The tenant this flow was started for, or null when there is no reason to believe it was.
    /// Null covers every way that can happen — no cookie, a cookie this deployment cannot decrypt,
    /// a state that does not match, an expired one, a flow that began without a session, or a
    /// redirect omitting the tenant or naming a different one than the flow began for.
    /// </summary>
    /// <remarks>
    /// This is the question the grant table is written on the answer to, and nothing else.
    /// <see cref="Started"/> asks the weaker one — whether this callback answers a flow at all —
    /// which is what the screen and the log line are allowed to go on.
    /// </remarks>
    public string? TenantStartedFor(string? stateCookie, string? state, string? tenantFromRedirect) =>
        Started(stateCookie, state) is { TenantId: { } tenantStartedFor }
            // The redirect must name the tenant the flow began for. Microsoft's documented success
            // always carries it; a decline omits it, so an absent tenant is the decline path
            // rather than a success being terse — and a different tenant is the impersonation
            // Microsoft's documentation warns about.
            && string.Equals(tenantFromRedirect, tenantStartedFor, StringComparison.OrdinalIgnoreCase)
                ? tenantStartedFor
                : null;

    /// <summary>
    /// The flow this callback answers, or null when nothing here may be believed: no cookie, one
    /// this deployment cannot decrypt, a state that does not match, or an expired one.
    /// <para>
    /// Weaker than <see cref="TenantStartedFor"/> on purpose. It says "this is the round trip that
    /// left from here", which is enough to tell somebody what happened to it and enough to log —
    /// and short of what recording a grant requires, because it says nothing about whose tenant
    /// came back.
    /// </para>
    /// </summary>
    public StartedConsentFlow? Started(string? stateCookie, string? state)
    {
        if (string.IsNullOrEmpty(stateCookie) || string.IsNullOrEmpty(state))
        {
            return null;
        }

        string payload;

        try
        {
            payload = _protector.Unprotect(stateCookie);
        }
        catch (CryptographicException)
        {
            // Tampered with, or protected by a key ring this instance cannot read. Either way there
            // is nothing here to trust.
            return null;
        }

        var parts = payload.Split('|');

        if (parts.Length != 3
            || !string.Equals(parts[1], state, StringComparison.Ordinal)
            || !long.TryParse(parts[2], CultureInfo.InvariantCulture, out var expiresAt)
            || timeProvider.GetUtcNow().ToUnixTimeSeconds() > expiresAt)
        {
            return null;
        }

        return new StartedConsentFlow(parts[0] is NoTenantKnown ? null : parts[0]);
    }
}

/// <summary>A Tenant Consent round trip that left from this deployment.</summary>
/// <param name="TenantId">
/// The tenant it began for, or null when it began without a session and nothing may be recorded on
/// the strength of what comes back.
/// </param>
public sealed record StartedConsentFlow(string? TenantId);
