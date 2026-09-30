namespace TodoWerk.Infrastructure.Authentication;

/// <summary>
/// Binding target for the <c>EntraId</c> configuration section. The app registration is
/// configuration, never code: a Self-Host installation points the same binary at its own
/// tenant and client id (ADR-0002).
/// </summary>
public sealed class EntraIdOptions
{
    public const string SectionName = "EntraId";

    public string Instance { get; set; } = "https://login.microsoftonline.com/";

    /// <summary>
    /// Tenant id, or <c>organizations</c> for the multi-tenant Hosted Service. Never
    /// <c>common</c> — consumer Microsoft accounts are out of scope (B2B only).
    /// </summary>
    public string TenantId { get; set; } = "organizations";

    public string ClientId { get; set; } = string.Empty;

    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// The registration's Application ID URI — <c>api://&lt;host&gt;/&lt;clientId&gt;</c> — which is
    /// the audience of the token a Teams client acquires for the tab. Empty on a deployment that
    /// has no Teams App Package, and then the tab's exchange endpoint accepts nothing but the
    /// audiences Microsoft.Identity.Web derives from the client id.
    /// <para>
    /// One registration, one host: Teams single sign-on does not support several domains per app,
    /// which is why a Self-Host needs a registration and an App Package of its own
    /// ([ADR-0011](../../../docs/adr/0011-two-app-packages-and-a-template.md)).
    /// </para>
    /// </summary>
    public Uri? ApplicationIdUri { get; set; }

    public string CallbackPath { get; set; } = "/signin-oidc";

    public string SignedOutCallbackPath { get; set; } = "/signout-callback-oidc";
}
