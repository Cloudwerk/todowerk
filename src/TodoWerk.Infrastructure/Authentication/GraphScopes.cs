namespace TodoWerk.Infrastructure.Authentication;

/// <summary>
/// Delegated Microsoft Graph scopes requested at sign-in. One prompt, once, covering everything
/// TodoWerk does — see [ADR-0007](../../../docs/adr/0007-one-consent-grant.md).
/// </summary>
public static class GraphScopes
{
    /// <summary>
    /// Fully qualified so the resource is unambiguous, rather than relying on Graph being
    /// the assumed default for a bare scope name.
    /// </summary>
    public const string TasksReadWrite = "https://graph.microsoft.com/Tasks.ReadWrite";

    /// <summary>
    /// One element, and it is the write scope. It subsumes <c>Tasks.Read</c>, so asking for both
    /// would only make the grant look wider than it is; and the write scope has to be in the
    /// durable cache before a Change is ever queued, because the worker that performs the write
    /// has no browser to prompt through (ADR-0002, ADR-0007).
    /// <para>
    /// <c>offline_access</c> is deliberately absent: MSAL adds it when redeeming the authorization
    /// code, and passing a reserved scope explicitly makes token acquisition throw.
    /// </para>
    /// </summary>
    public static IReadOnlyList<string> SignIn { get; } = [TasksReadWrite];

    /// <summary>
    /// What an administrator approves on behalf of a whole tenant. The same permissions sign-in
    /// asks each person for and nothing wider — approving for the organisation has to be the same
    /// decision every individual was making, or the two prompts describe two different products.
    /// <para>
    /// The OpenID Connect scopes are spelled out here where <see cref="SignIn"/> leaves them
    /// implicit, and that is not a widening. Sign-in gets them from MSAL, which adds them when it
    /// redeems the authorization code and throws if they are passed to it explicitly. Nothing
    /// intermediates this list — it goes into a URL — and leaving them out would mean the tenant
    /// was approved for the Graph permission while everybody was still prompted for the rest,
    /// which is the one outcome Tenant Consent exists to prevent.
    /// </para>
    /// </summary>
    public static IReadOnlyList<string> TenantConsent { get; } =
        ["openid", "profile", "offline_access", TasksReadWrite];
}
