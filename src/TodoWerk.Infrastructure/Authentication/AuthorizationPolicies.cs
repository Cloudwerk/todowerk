namespace TodoWerk.Infrastructure.Authentication;

public static class AuthorizationPolicies
{
    /// <summary>
    /// Requires a signed-in cookie session and answers an anonymous caller with 401, never a
    /// redirect to Entra ID. Every endpoint the SPA calls with <c>fetch</c> uses this.
    /// </summary>
    public const string Api = "Api";

    /// <summary>
    /// Requires a valid Teams single sign-on token. Exactly one endpoint may use this — the one
    /// that exchanges that token for the session cookie <see cref="Api"/> asks for — because a
    /// second way of being signed in is what
    /// [ADR-0010](../../../docs/adr/0010-teams-tab-session-and-framing.md) declined.
    /// </summary>
    public const string TeamsSso = "TeamsSso";
}
