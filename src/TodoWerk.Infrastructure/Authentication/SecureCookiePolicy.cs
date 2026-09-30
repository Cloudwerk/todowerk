using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;

namespace TodoWerk.Infrastructure.Authentication;

/// <summary>
/// The two cookie attributes every TodoWerk cookie decides the same way, in one place, because
/// they are decided by the environment rather than by what the cookie is for.
/// </summary>
public static class SecureCookiePolicy
{
    /// <summary>
    /// <c>Always</c> everywhere the app is really deployed. Development also serves plain HTTP
    /// (and the antiforgery system throws outright when <c>Always</c> meets a non-SSL request),
    /// so there it follows the request scheme instead.
    /// </summary>
    public static CookieSecurePolicy For(IHostEnvironment environment) =>
        environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;

    /// <summary>
    /// <c>None</c> everywhere the app is really deployed, and unpartitioned — which is what lets
    /// the Teams Tab hold a session at all. The tab is TodoWerk inside somebody else's iframe, so
    /// every cookie it needs is a third-party cookie, and a cookie that is not
    /// <c>SameSite=None; Secure</c> is simply absent there
    /// ([ADR-0010](../../../docs/adr/0010-teams-tab-session-and-framing.md)).
    /// <para>
    /// Development keeps <c>Lax</c> for the same reason <see cref="For"/> keeps
    /// <c>SameAsRequest</c>: it also serves plain HTTP, and every browser drops a
    /// <c>SameSite=None</c> cookie that is not <c>Secure</c> — which would not fail, it would
    /// silently sign a developer out on every request.
    /// </para>
    /// <para>
    /// <strong>This is a security change wherever it is read.</strong> Cross-site request forgery
    /// protection no longer has <c>SameSite</c> behind it and rests entirely on the antiforgery
    /// double-submit pair. That pair still holds — <c>SameSite=None</c> lets a hostile page
    /// <em>send</em> the cookie, never <em>read</em> it — but it has stopped being defence in
    /// depth and become the defence.
    /// </para>
    /// </summary>
    public static SameSiteMode SameSiteFor(IHostEnvironment environment) =>
        environment.IsDevelopment() ? SameSiteMode.Lax : SameSiteMode.None;
}
