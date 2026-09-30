namespace TodoWerk.Infrastructure.Authentication;

/// <summary>
/// The second authentication scheme, and the only one that is not the cookie.
/// <para>
/// [ADR-0010](../../../docs/adr/0010-teams-tab-session-and-framing.md) rejected a bearer token as
/// TodoWerk's session because it would make the application accept two ways of knowing who is
/// calling on every endpoint forever. This is not that: exactly one endpoint names this scheme,
/// its entire job is to trade the token for the cookie, and
/// <see cref="AuthorizationPolicies.Api"/> still names the cookie scheme and nothing else.
/// </para>
/// </summary>
public static class TeamsSsoDefaults
{
    /// <summary>The JWT bearer scheme the tab's exchange endpoint authenticates against.</summary>
    public const string AuthenticationScheme = "TeamsSso";

    /// <summary>
    /// Which partition of the durable token cache a Teams sign-in's tokens live in, and the one
    /// fact <c>TeamsSsoSignIn</c> and <c>GraphGateway</c> have to agree on without ever meeting.
    /// <para>
    /// MSAL does not file an on-behalf-of result where it files an authorization-code one. A code
    /// redemption is filed under the home account id, which is what <c>GraphGateway</c> looks a
    /// token up by; an on-behalf-of result is filed under a hash of the assertion it was made with,
    /// which nothing outside that one request has. So a tab could sign somebody in perfectly and
    /// then fail every Graph call afterwards with "reconnect required", in a tenant that had
    /// consented and with nothing wrong.
    /// </para>
    /// <para>
    /// The answer MSAL offers is a long-running on-behalf-of process, which files under a key the
    /// caller chooses. This is that key: the directory pair TodoWerk already identifies a person
    /// by, which both ends can compute from what they hold — the SSO token at sign-in, the queue
    /// row hours later.
    /// </para>
    /// </summary>
    public static string SessionKeyFor(string tenantId, string objectId) => $"teams.{objectId}.{tenantId}";
}
