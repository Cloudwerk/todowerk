using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace TodoWerk.IntegrationTests;

/// <summary>
/// The About Page and the Administrator's Guide, as a stranger sees them. The App Package names
/// both — <c>developer.websiteUrl</c>, which the Teams admin centre shows as the app's support
/// link, and <c>publisherDocsUrl</c> — and everybody who follows either arrives without a session:
/// an administrator deciding whether to allow the product, and anyone following a support link,
/// who has no session; "anonymous" is the requirement rather than a detail
/// ([ADR-0013](../../docs/adr/0013-the-handbook-is-split-by-kinship.md)).
/// </summary>
public sealed class HandbookPageTests
{
    private const string About = "/about";

    private const string Administrators = "/administrators";

    [Theory]
    [InlineData(About)]
    [InlineData(Administrators)]
    public async Task AreServedToSomebodyWhoHasNotSignedIn(string path)
    {
        using var factory = new TodoWerkWebApplicationFactory().WithoutBackgroundWorkers();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Null(response.Headers.Location);
    }

    /// <summary>
    /// The one mechanical tie between the Administrator's Guide and the code it describes. The
    /// floor, the undo window and the dormancy window are configuration, and a page that stated
    /// their defaults would be wrong on every deployment that changed one — so the page reads them
    /// from the same options the code enforces. Four non-default values, and the page has to say
    /// each of them; the sentences named here are the ones the numbers live in.
    /// </summary>
    [Fact]
    public async Task TheAdministratorsGuideStatesThisInstallationsOwnNumbers()
    {
        using var factory = new TodoWerkWebApplicationFactory()
            .WithoutBackgroundWorkers()
            .WithConfigurationOverride("Onboarding:StatisticsFloor", "7")
            .WithConfigurationOverride("Onboarding:DormancyWindow", "200.00:00:00")
            .WithConfigurationOverride("Changes:ChangeRetention", "45.00:00:00")
            .WithConfigurationOverride("Indexing:IdleAfter", "21.00:00:00");
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var response = await client.GetAsync(Administrators, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        var page = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Contains("fewer than 7 people", page, StringComparison.Ordinal);
        Assert.Contains("Kept for 45 days after the change finished", page, StringComparison.Ordinal);
        Assert.Contains("after 200 days without a sign-in", page, StringComparison.Ordinal);
        Assert.Contains("signed in within the last 21", page, StringComparison.Ordinal);
        Assert.Contains("idle for 21 days or more", page, StringComparison.Ordinal);
        Assert.DoesNotContain("{{", page, StringComparison.Ordinal);
    }

    /// <summary>
    /// The documents there are, in the order to offer them, said once. <c>GET /api/handbook</c> is
    /// where the shell's help menu gets its list, and the header of every served document is built
    /// from the same <c>DocumentNav</c> — so this reads the nav back out of a rendered page and
    /// compares it with what the endpoint answered, rather than comparing either against a list
    /// written down a third time here.
    /// <para>
    /// The tie is enforced here rather than asserted in a comment: a menu that hardcoded its own
    /// paths and order could drift from the served pages without anything failing.
    /// </para>
    /// </summary>
    [Fact]
    public async Task TheDocumentListTheClientReadsIsTheOneTheServedPagesCarry()
    {
        using var factory = new TodoWerkWebApplicationFactory()
            .WithoutBackgroundWorkers()
            .WithConfigurationOverride("Handbook:GuideUrl", "https://todowerk.example/guide/");
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var response = await client.GetAsync("/api/handbook", TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        var answered = await response.Content.ReadFromJsonAsync<HandbookResponse>(TestContext.Current.CancellationToken);

        using var pageResponse = await client.GetAsync(About, TestContext.Current.CancellationToken);
        pageResponse.EnsureSuccessStatusCode();
        var page = await pageResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(answered);
        Assert.Equal(
            NavOf(page),
            answered.Documents.Select(document => (document.Text, document.Href)).ToList());

        // Named rather than only compared, because these two are what the App Package points at and
        // what the sign-in card and the Tenant Overview link to by path.
        Assert.Contains(answered.Documents, document => document.Href == About);
        Assert.Contains(answered.Documents, document => document.Href == Administrators);
        Assert.Contains(answered.Documents, document => document.Group == "Legal");
    }

    /// <summary>
    /// Anonymous, because the shell offers these documents to somebody who has not signed in —
    /// the reader ADR-0013 wrote the pages for. A client asked
    /// to draw the help control from an endpoint that answered 401 would draw nothing at the one
    /// moment it matters.
    /// </summary>
    [Fact]
    public async Task TheDocumentListIsAnsweredToSomebodyWhoHasNotSignedIn()
    {
        using var factory = new TodoWerkWebApplicationFactory().WithoutBackgroundWorkers();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var response = await client.GetAsync("/api/handbook", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    /// <summary>
    /// The About Page is the one served document with a shape beyond headings and prose: a lead
    /// sentence, and the operator's notice set off as a callout. Both are markdown the renderer has
    /// to be configured for — generic attributes for the one, a blockquote style for the other — and
    /// a renderer without either still returns 200 with the attribute printed as text. The header's
    /// icon is the third thing the page cannot fetch: it is inlined, because the content security
    /// policy allows no other origin, and a missing resource would fail the page rather than the tile.
    /// </summary>
    [Fact]
    public async Task TheAboutPageCarriesItsLeadItsCalloutAndTheInlinedIcon()
    {
        using var factory = new TodoWerkWebApplicationFactory().WithoutBackgroundWorkers();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var response = await client.GetAsync(About, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        var page = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Contains("<p class=\"lead\">", page, StringComparison.Ordinal);
        Assert.DoesNotContain("{.lead}", page, StringComparison.Ordinal);
        Assert.Contains("<blockquote>", page, StringComparison.Ordinal);
        Assert.Contains("<img src=\"data:image/png;base64,", page, StringComparison.Ordinal);
    }

    /// <summary>The nav a served page carries, in the order it carries it: text and address per entry.</summary>
    private static List<(string Text, string Href)> NavOf(string page)
    {
        var nav = Regex.Match(page, "<nav>(?<links>.*?)</nav>", RegexOptions.Singleline);
        Assert.True(nav.Success, "The served page carries no nav for the endpoint's list to be compared with.");

        return [.. Regex
            .Matches(nav.Groups["links"].Value, "<a href=\"(?<href>[^\"]*)\"[^>]*>(?<text>[^<]*)</a>")
            .Select(link => (link.Groups["text"].Value, link.Groups["href"].Value))];
    }

    private sealed record HandbookResponse(IReadOnlyList<HandbookDocumentResponse> Documents);

    private sealed record HandbookDocumentResponse(string Text, string Href, string Group);
}
