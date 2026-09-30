using Microsoft.Extensions.Options;
using TodoWerk.Web.Legal;

namespace TodoWerk.Web.Endpoints;

internal static class LegalEndpoints
{
    /// <summary>
    /// The terms of use and the privacy notice, served as pages a stranger can open. Three places
    /// have to name the same two addresses — the Entra ID app registration's Branding &amp;
    /// properties, which is where the links on the Microsoft consent dialog come from; the Teams
    /// app manifest's <c>developer</c> block; and the Partner Center submission — and a listing is
    /// re-validated against them long afterwards, so they are paths on the deployment's own domain
    /// rather than blob URLs that move when a file is renamed.
    /// <para>
    /// Anonymous, and that is the requirement rather than a convenience: both are read by people
    /// deciding whether to sign in at all, and by people who never will.
    /// </para>
    /// </summary>
    public static IEndpointRouteBuilder MapLegalEndpoints(this IEndpointRouteBuilder app)
    {
        // Rendered here rather than on the first request, so that a document that cannot be
        // rendered at all — a resource that failed to be embedded, say — is a host that refuses to
        // start rather than a legal page answering 500 to the one person who opens it.
        var pages = app.ServiceProvider.GetRequiredService<LegalPages>();
        var terms = pages.Terms;
        var privacy = pages.Privacy;

        app.MapGet(LegalPages.TermsPath, () => Results.Content(terms, "text/html; charset=utf-8"))
            .AllowAnonymous()
            .WithName("TermsOfUse");

        app.MapGet(LegalPages.PrivacyPath, () => Results.Content(privacy, "text/html; charset=utf-8"))
            .AllowAnonymous()
            .WithName("PrivacyNotice");

        WarnIfNobodyIsNamed(app.ServiceProvider);

        return app;
    }

    /// <summary>
    /// A fallback is invisible: the terms still render, still read as English, and still say
    /// nothing about who is serving them. That is fine for a developer and wrong for anything a
    /// consent dialog or a Store listing points at, so every unset value is named once at startup
    /// where somebody deploying will see it, rather than left to be noticed by a reader.
    /// </summary>
    private static void WarnIfNobodyIsNamed(IServiceProvider services)
    {
        if (services.GetRequiredService<IHostEnvironment>().IsDevelopment())
        {
            return;
        }

        if (services.GetRequiredService<IOptions<LegalOptions>>().Value.UnsetSettings is not { Count: > 0 } unset)
        {
            return;
        }

        services.GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(LegalEndpoints))
            .LogWarning(
                "{Unset} not set, so the terms served at {TermsPath} fall back to wording that names "
                + "nobody. Set them before pointing an app registration or a Store listing at these "
                + "pages.",
                string.Join(", ", unset),
                LegalPages.TermsPath);
    }
}
