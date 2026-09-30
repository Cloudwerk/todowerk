using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using TodoWerk.Application.Abstractions.Authentication;
using TodoWerk.Infrastructure.Authentication;
using TodoWerk.Web.Security;

namespace TodoWerk.Web.Endpoints;

internal static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        // Browser navigation, not fetch: the response is a redirect to Entra ID.
        //
        // `prompt` exists for the Teams tab and reaches Entra ID on no other path. When the tab's
        // on-behalf-of exchange comes back "consent required", the popup runs this flow with
        // prompt=consent and a return URL of the tab's auth-end page — Microsoft's own guidance for
        // a Teams consent popup is a round trip that starts and ends on your own domain, which is
        // what this endpoint already was (ADR-0010). Only that one value is honoured: anything
        // else is dropped rather than forwarded, so this cannot become a way to steer somebody
        // else's sign-in — `prompt=login` would re-authenticate, `prompt=none` would fail silently.
        app.MapGet("/auth/sign-in", (string? returnUrl, string? prompt) => Results.Challenge(
                new OpenIdConnectChallengeProperties
                {
                    RedirectUri = SafeReturnUrl(returnUrl),
                    Prompt = string.Equals(prompt, "consent", StringComparison.Ordinal) ? "consent" : null,
                },
                [OpenIdConnectDefaults.AuthenticationScheme]))
            .AllowAnonymous()
            .WithName("SignIn");

        // The last leg of erasure from inside the Teams tab, and the only thing it does is end the
        // Entra ID session — the cookie is already gone by the time anybody reaches this, deleted
        // by the erasure request itself.
        //
        // A GET where /auth/sign-out is a POST, and the difference is what each one can be made to
        // do from a hostile page. Signing out of TodoWerk destroys a session somebody is using, so
        // it is guarded; this signs nobody out of TodoWerk at all — it names the OpenID Connect
        // scheme only — and its whole effect is to end a Microsoft session, which is the effect of
        // any link to Microsoft's own logout URL. It has to be a GET because a Teams authentication
        // popup is opened at a URL, and nothing in TeamsJS can POST into one.
        app.MapGet("/auth/teams-end-session", () => Results.SignOut(
                new AuthenticationProperties { RedirectUri = TeamsTab.AuthEndPath },
                [OpenIdConnectDefaults.AuthenticationScheme]))
            .AllowAnonymous()
            .WithName("TeamsEndSession");

        // POST + antiforgery: sign-out is a state change, so it must not be reachable by a
        // cross-site GET. The SPA submits a real form so the browser follows the redirect to
        // the Entra ID end-session endpoint.
        app.MapPost("/auth/sign-out", () => Results.SignOut(
                new AuthenticationProperties { RedirectUri = "/" },
                [CookieAuthenticationDefaults.AuthenticationScheme, OpenIdConnectDefaults.AuthenticationScheme]))
            .RequireAuthorization(AuthorizationPolicies.Api)
            // Reachable whether or not the caller is licensed. The denied card is drawn inside the
            // application shell, and the session menu it carries is where the way out lives: gating
            // sign-out answers the form post with the licensing problem document, which leaves a
            // shut-out person staring at JSON and still signed in.
            .AllowUnlicensed()
            .ValidateAntiforgery()
            .WithName("SignOut");

        app.MapGet("/api/me", (ICurrentUser currentUser) => TypedResults.Ok(new SessionUserResponse(
                currentUser.DisplayName ?? string.Empty,
                currentUser.Username ?? string.Empty)))
            .RequireAuthorization(AuthorizationPolicies.Api)
            // Answered whether or not the caller is licensed. Both denied cards are drawn inside
            // the application shell, and the shell reads this to know whose name to put in the
            // header and to render the way out — including the request to be erased, which is an
            // obligation and cannot depend on a Licence.
            .AllowUnlicensed()
            .WithName("GetCurrentUser");

        return app;
    }

    /// <summary>Only same-site relative paths, so the sign-in link cannot become an open redirect.</summary>
    private static string SafeReturnUrl(string? returnUrl) =>
        IsLocalUrl(returnUrl) ? returnUrl! : "/";

    /// <summary>
    /// Internal for the tests: the decision is a truth table, and the functional round trip
    /// cannot show it — the redirect target rides encrypted inside the OpenID Connect state.
    /// </summary>
    internal static bool IsLocalReturnUrl(string? returnUrl) => IsLocalUrl(returnUrl);

    /// <summary>
    /// The framework's notion of a local URL, spelled out. One slash and then a normal character:
    /// browsers resolve both <c>//host</c> and <c>/\host</c> as protocol-relative — the WHATWG
    /// parser treats <c>\</c> like <c>/</c> after a slash — so rejecting only <c>//</c> leaves the
    /// redirect open through <c>/\evil.example</c>. Control characters are rejected because they
    /// have no place in a path and CR/LF would otherwise ride into a response header.
    /// </summary>
    private static bool IsLocalUrl(string? url)
    {
        if (string.IsNullOrEmpty(url) || url[0] != '/')
        {
            return false;
        }

        if (url.Length > 1 && url[1] is '/' or '\\')
        {
            return false;
        }

        foreach (var character in url)
        {
            if (char.IsControl(character))
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>Mirrored by <c>SessionUser</c> in the client's <c>src/api/types.ts</c>.</summary>
internal sealed record SessionUserResponse(string DisplayName, string Username);
