namespace TodoWerk.Web.Security;

internal static class SecurityHeadersMiddlewareExtensions
{
    /// <summary>
    /// The hosts allowed to frame the tab. Teams desktop and Teams web both render tabs from
    /// <c>teams.microsoft.com</c>; <c>*.cloud.microsoft</c> is the domain Microsoft has moved its
    /// app hosts onto, and it admits the new Outlook (<c>outlook.cloud.microsoft</c>) and the
    /// Microsoft 365 app (<c>m365.cloud.microsoft</c>) as well. A manifest at schema 1.13 or later
    /// offers a personal tab in both whether or not this list does, so they are named on purpose.
    /// The list is Microsoft's own, complete, because <c>frame-ancestors</c> is checked against
    /// every ancestor frame, not just the one in the address bar: <c>*.office.com</c> and
    /// <c>*.microsoft365.com</c> (the classic Microsoft 365 app hosts, and any intermediate frame
    /// Outlook on the web serves from <c>office.com</c>), and the two <c>outlook-sdf</c> origins
    /// Microsoft's own early-ring tenants land on. Every host named here is a host the tab has to
    /// be walked in
    /// ([ADR-0010](../../../docs/adr/0010-teams-tab-session-and-framing.md), as amended).
    /// The list: https://learn.microsoft.com/microsoftteams/platform/tabs/how-to/tab-requirements
    /// </summary>
    private const string TeamsFrameAncestors =
        "frame-ancestors 'self' https://teams.microsoft.com https://*.teams.microsoft.com "
        + "https://*.cloud.microsoft https://*.microsoft365.com https://*.office.com "
        + "https://outlook.office.com https://outlook.office365.com "
        + "https://outlook-sdf.office.com https://outlook-sdf.office365.com";

    /// <summary>
    /// The response headers every page and API answer carries. React escapes what it renders and
    /// the antiforgery pair guards the mutating endpoints — these are the second line: no MIME
    /// sniffing, no framing (the sign-out form, and rename and merge since M2, are clickjacking
    /// targets), no referrer leakage beyond the origin, and — outside Development, where Vite's
    /// tooling wants more freedom than a policy should give — a same-origin CSP behind React's
    /// escaping. Styles allow <c>unsafe-inline</c> because Fluent's CSS-in-JS injects style
    /// elements at runtime; scripts do not get the same latitude.
    /// <para>
    /// One branch, and it is the Teams tab's. A document under <see cref="TeamsTab.BasePath"/>
    /// names the Teams hosts in <c>frame-ancestors</c> and sends no <c>X-Frame-Options</c> at all,
    /// because that header has no allow-list — it can be dropped but never narrowed. Every other
    /// document, <c>/</c> included, keeps <c>frame-ancestors 'none'</c> and <c>DENY</c>: the
    /// Workbench is not the thing being embedded, and relaxing framing across the origin would put
    /// renames, merges and erasure one clickjacking frame away from anybody.
    /// </para>
    /// </summary>
    /// <param name="signInAuthority">
    /// The identity provider's address. Sign-out is a form post answered with a redirect to its
    /// end-session endpoint, and engines differ on whether <c>form-action</c> is re-checked after
    /// a redirect — so the authority is named rather than left to the stricter reading breaking
    /// the last step of signing out.
    /// </param>
    public static IApplicationBuilder UseSecurityHeaders(
        this IApplicationBuilder app,
        IHostEnvironment environment,
        string? signInAuthority)
    {
        var authority = Uri.TryCreate(signInAuthority, UriKind.Absolute, out var instance)
            ? instance.GetLeftPart(UriPartial.Authority)
            : "https://login.microsoftonline.com";

        var policyWithoutFraming =
            "default-src 'self'; "
            + "script-src 'self'; "
            + "style-src 'self' 'unsafe-inline'; "
            + "img-src 'self' data:; "
            + "connect-src 'self'; "
            + "base-uri 'self'; "
            + $"form-action 'self' {authority}; "
            + "object-src 'none'; ";

        var contentSecurityPolicy = policyWithoutFraming + "frame-ancestors 'none'";
        var teamsContentSecurityPolicy = policyWithoutFraming + TeamsFrameAncestors;

        var applyContentSecurityPolicy = !environment.IsDevelopment();

        return app.Use((context, next) =>
        {
            var headers = context.Response.Headers;
            var framable = TeamsTab.Hosts(context.Request.Path);

            headers.XContentTypeOptions = "nosniff";
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";

            if (framable)
            {
                // Not merely "do not set one" — actively take back whatever set one later.
                // ASP.NET Core's antiforgery adds `X-Frame-Options: SAMEORIGIN` whenever it issues
                // a token and finds the header absent, and this application issues a token on
                // every safe extensionless GET — the tab's own document above all. SAMEORIGIN
                // refuses Teams exactly as DENY would, so the tab would render as an empty frame
                // with the reason only in the browser console.
                //
                // On the way out, because that is the only moment after every other writer has had
                // its turn.
                context.Response.OnStarting(
                    static state =>
                    {
                        ((HttpContext)state).Response.Headers.XFrameOptions = default;

                        return Task.CompletedTask;
                    },
                    context);
            }
            else
            {
                headers.XFrameOptions = "DENY";
            }

            if (applyContentSecurityPolicy)
            {
                headers.ContentSecurityPolicy = framable
                    ? teamsContentSecurityPolicy
                    : contentSecurityPolicy;
            }

            return next(context);
        });
    }
}
