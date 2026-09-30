using Microsoft.Extensions.Options;
using TodoWerk.Web.Documents;
using TodoWerk.Web.Handbook;

namespace TodoWerk.Web.Endpoints;

/// <summary>One document the client may offer a reader. Mirror of <see cref="DocumentLink"/>.</summary>
internal sealed record HandbookDocumentDto(string Text, string Href, DocumentGroup Group);

/// <summary>
/// What the client is told about the Handbook: the documents there are, in the order to offer
/// them. The list is <see cref="DocumentNav"/>'s — the same one the header of every served
/// document is built from — so the shell and the documents cannot disagree about what the document
/// set is, which nothing else enforces.
/// <para>
/// The Guide's absence needs no flag of its own: a Self-Host has none, so the list simply does not
/// carry it and the client draws no entry rather than a dead one (ADR-0013).
/// </para>
/// </summary>
internal sealed record HandbookDto(IReadOnlyList<HandbookDocumentDto> Documents);

internal static class HandbookEndpoints
{
    /// <summary>
    /// The About Page and the Administrator's Guide, served as pages a stranger can open, and the
    /// one fact about the Handbook the Workbench needs. The App Package names both pages —
    /// <c>developer.websiteUrl</c> and <c>publisherDocsUrl</c> — and the Teams admin centre shows
    /// the first as the app's support link, where requiring a sign-in is a must-fix in the Store
    /// guidelines. So both are anonymous, and both are paths on the deployment's own domain
    /// ([ADR-0013](../../../docs/adr/0013-the-handbook-is-split-by-kinship.md)).
    /// </summary>
    public static IEndpointRouteBuilder MapHandbookEndpoints(this IEndpointRouteBuilder app)
    {
        // Rendered here rather than on the first request, for the reason the legal pages are: a
        // document that cannot be rendered at all is a host that refuses to start rather than a
        // support link answering 500 to the administrator deciding whether to allow the product.
        var pages = app.ServiceProvider.GetRequiredService<HandbookPages>();
        var about = pages.About;
        var administrators = pages.Administrators;

        app.MapGet(HandbookPages.AboutPath, () => Results.Content(about, "text/html; charset=utf-8"))
            .AllowAnonymous()
            .WithName("About");

        app.MapGet(HandbookPages.AdministratorsPath, () => Results.Content(administrators, "text/html; charset=utf-8"))
            .AllowAnonymous()
            .WithName("AdministratorsGuide");

        // Anonymous, and read once by the shell: which documents exist is a fact about the
        // deployment rather than about the person, and nothing is given away by answering. It has
        // to stay anonymous for a second reason — the shell offers these to somebody who has not
        // signed in, which is the case ADR-0013 wrote the pages for.
        //
        // Built once, like the pages above: the list is a function of configuration read at
        // startup, so rebuilding it per request would be work to produce the same answer.
        var handbook = new HandbookDto(
            [.. app.ServiceProvider.GetRequiredService<DocumentNav>().Links
                .Select(link => new HandbookDocumentDto(link.Text, link.Href, link.Group))]);

        app.MapGet("/api/handbook", () => TypedResults.Ok(handbook))
            .AllowAnonymous()
            .WithName("Handbook");

        SayWhatIsNotLinked(app.ServiceProvider);

        return app;
    }

    /// <summary>
    /// An unset link is invisible: the About Page renders without it and reads as complete. Named
    /// once at startup so that a Hosted Service which forgot one finds out from its own log rather
    /// than from a broken page — at information level rather than as a warning, because on a Self-Host
    /// that has no Guide, no imprint and nothing to order, all five unset is the correct state.
    /// </summary>
    private static void SayWhatIsNotLinked(IServiceProvider services)
    {
        if (services.GetRequiredService<IHostEnvironment>().IsDevelopment())
        {
            return;
        }

        if (services.GetRequiredService<IOptions<HandbookOptions>>().Value.UnsetSettings is not { Count: > 0 } unset)
        {
            return;
        }

        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(HandbookEndpoints));

        if (!logger.IsEnabled(LogLevel.Information))
        {
            return;
        }

        logger.LogInformation(
                "{Unset} not set, so the About Page at {AboutPath} links to none of them. That is "
                + "right for a Self-Host with none to link to; a deployment that has a Guide, an "
                + "imprint or a way to order should name them.",
                string.Join(", ", unset),
                HandbookPages.AboutPath);
    }
}
