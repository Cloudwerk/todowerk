using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using TodoWerk.Infrastructure.Authentication;
using TodoWerk.Web.Handbook;
using TodoWerk.Web.Legal;

namespace TodoWerk.Web.Security;

/// <summary>
/// Publishes the antiforgery request token to the SPA as a JS-readable cookie on every safe
/// authenticated request. The client echoes it back in the <c>X-CSRF-TOKEN</c> header (or a
/// hidden form field) on mutating requests, which is what
/// <see cref="AntiforgeryEndpointExtensions.ValidateAntiforgery{TBuilder}"/> checks.
/// </summary>
internal sealed class AntiforgeryTokenMiddleware(
    RequestDelegate next,
    IAntiforgery antiforgery,
    IHostEnvironment environment)
{
    internal const string CookieName = "XSRF-TOKEN";

    public async Task InvokeAsync(HttpContext context)
    {
        if (ShouldIssueToken(context.Request))
        {
            // The token is bound to whoever HttpContext.User is when it is issued, and checked
            // against whoever it is when the mutating request validates it. Nothing has populated
            // the user at this point: the default scheme is OpenID Connect, a remote handler that
            // returns no result for an ordinary request, and the endpoints that do see a user get
            // one from the authorization policy naming the cookie scheme. Issuing the token for
            // the anonymous identity and validating it against the signed-in one rejects every
            // sign-out, so authenticate the cookie here rather than leaving the two identities to
            // disagree. Making the cookie the default authenticate scheme would fix this too, and
            // break Graph: Microsoft.Identity.Web then infers 'Cookies' as the scheme to read its
            // options from, and fails with IDW10503.
            if (context.User.Identity?.IsAuthenticated != true)
            {
                var session = await context.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);

                if (session.Succeeded)
                {
                    context.User = session.Principal;
                }
            }

            var tokens = antiforgery.GetAndStoreTokens(context);

            if (tokens.RequestToken is not null)
            {
                context.Response.Cookies.Append(
                    CookieName,
                    tokens.RequestToken,
                    new CookieOptions
                    {
                        // Readable by the SPA by design — this is the token it must echo back.
                        // The paired secret stays in the HttpOnly antiforgery cookie.
                        HttpOnly = false,
                        // The same rule every other cookie follows: Always outside Development.
                        // Following the request scheme here would drop the Secure flag behind any
                        // proxy that terminates TLS without forwarding the scheme — and this is
                        // the one cookie an attacker needs to complete a CSRF.
                        Secure = SecureCookiePolicy.For(environment) is not CookieSecurePolicy.SameAsRequest
                            || context.Request.IsHttps,
                        // Not Strict. In the Teams tab this document is third-party, and a
                        // Strict cookie would never be sent to it — the client would read no
                        // token and every mutating request would be refused (ADR-0010).
                        //
                        // Read this as a security change, not as a relaxed default: SameSite has
                        // stopped being part of TodoWerk's CSRF defence and the double-submit
                        // pair is now the whole of it. It still holds, because a hostile page can
                        // make the browser *send* this cookie but never *read* it, and the header
                        // this value has to appear in cannot be forged cross-origin. What is gone
                        // is the second layer that used to cover a mistake in the first one.
                        SameSite = SecureCookiePolicy.SameSiteFor(environment),
                        Path = "/",
                    });
            }
        }

        await next(context);
    }

    /// <summary>
    /// Safe methods only, and not for static assets: a page load pulls dozens of scripts and
    /// styles, and minting a token — a serialize, an encrypt, a Set-Cookie — for each of them
    /// buys nothing. The SPA reads the cookie the document and API requests already set. Paths
    /// with a file extension are assets; the entry document arrives as <c>/</c> or a client
    /// route, both extensionless, because this runs ahead of the default-files rewrite.
    /// <para>
    /// The legal pages and the two Handbook pages are excluded for a different reason. They carry
    /// no form, post nowhere and belong to no session, and the people who read them are deciding
    /// whether to sign in at all — so setting two cookies on somebody who has arrived to read the
    /// privacy notice is the one thing that page should not do.
    /// </para>
    /// </summary>
    private static bool ShouldIssueToken(HttpRequest request) =>
        (HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method))
        && !Path.HasExtension(request.Path.Value ?? "/")
        && !LegalPages.Hosts(request.Path)
        && !HandbookPages.Hosts(request.Path);
}
