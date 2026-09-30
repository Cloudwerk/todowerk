using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using TodoWerk.Application.Abstractions.Licensing;
using TodoWerk.Application.Licensing;
using TodoWerk.Infrastructure.Licensing;
using TodoWerk.Web.Security;
using Xunit;

namespace TodoWerk.IntegrationTests;

/// <summary>
/// The Licensing module in the running application: what the Licence endpoint answers, what a
/// denied person meets, what stays open to them, and what a Self-Host does — which is nothing.
/// <para>
/// Driven through the real pipeline rather than against the resolver, because the thing most worth
/// pinning is not the resolution itself (the unit suite has that) but which endpoints the gate is
/// actually in front of. A gate that is not wired is a gate that passes every test about its
/// contents.
/// </para>
/// </summary>
public sealed class LicensingTests
{
    private const string Licence = "/api/licence";

    private const string Colleague = "33333333-3333-3333-3333-333333333333";

    private static readonly WebApplicationFactoryClientOptions ClientOptions = new()
    {
        AllowAutoRedirect = false,
        BaseAddress = new Uri("https://localhost"),
    };

    [Theory]
    // A Trial far from its end is a running Trial and says so; the two paid kinds say nothing at
    // all, however long they have to run.
    [InlineData("tenant", "Tenant", true, "None")]
    [InlineData("personal", "Personal", false, "None")]
    [InlineData("trial", "Trial", true, "TrialRunning")]
    public async Task TheLicenceEndpointAnswersTheKindAndWhetherConsentMayBeOffered(
        string portalKind,
        string kind,
        bool mayOfferConsent,
        string banner)
    {
        using var portal = new FakeManagementPortal(_ =>
            FakeManagementPortal.PortalAnswer.Ok(
                FakeManagementPortal.Valid(portalKind, "2027-03-14T00:00:00Z")));

        using var factory = HostedService(portal);
        using var client = factory.CreateClient(ClientOptions);

        using var document = await ReadAsync(client, factory, Licence);
        var licence = document.RootElement;

        Assert.Equal(kind, licence.GetProperty("kind").GetString());
        Assert.Equal(mayOfferConsent, licence.GetProperty("mayOfferTenantConsent").GetBoolean());
        Assert.Equal(banner, licence.GetProperty("banner").GetString());

        // The two server-side facts about a customer's purchase never cross to a browser.
        Assert.False(licence.TryGetProperty("licenceId", out _));
        Assert.False(licence.TryGetProperty("usageSecret", out _));
        Assert.DoesNotContain("us_integration_secret", licence.GetRawText(), StringComparison.Ordinal);
    }

    /// <summary>
    /// A Trial close to its end is the one thing the Workbench says out loud, and the threshold is
    /// configuration rather than a number in the client.
    /// </summary>
    [Fact]
    public async Task ATrialEndingInsideTheThresholdAsksForTheWarningBanner()
    {
        using var portal = new FakeManagementPortal(_ =>
            FakeManagementPortal.PortalAnswer.Ok(FakeManagementPortal.Valid(
                "trial",
                DateTimeOffset.UtcNow.AddDays(3).ToString("O"),
                "https://portal.todowerk.test/buy")));

        using var factory = HostedService(portal);
        using var client = factory.CreateClient(ClientOptions);

        using var document = await ReadAsync(client, factory, Licence);

        Assert.Equal("TrialEndingSoon", document.RootElement.GetProperty("banner").GetString());
        Assert.Equal(
            "https://portal.todowerk.test/buy",
            document.RootElement.GetProperty("purchaseUrl").GetString());
    }

    /// <summary>
    /// A confirmed negative denies that person's authenticated API calls with the "ended" code and
    /// the portal's own words — and leaves their colleague's alone, which is the whole of ADR-0012
    /// in one assertion.
    /// </summary>
    [Fact]
    public async Task AConfirmedNegativeDeniesOnePersonAndLeavesTheirColleagueServed()
    {
        using var portal = new FakeManagementPortal(body =>
            body.Contains(Colleague, StringComparison.Ordinal)
                ? FakeManagementPortal.PortalAnswer.Ok(
                    FakeManagementPortal.Valid("personal", "2027-03-14T00:00:00Z"))
                : FakeManagementPortal.PortalAnswer.Ok(
                    FakeManagementPortal.Expired("Your trial of TodoWerk ended on 2 October 2026.")));

        using var factory = HostedService(portal);
        using var client = factory.CreateClient(ClientOptions);

        using var denied = await SendAsync(client, factory, HttpMethod.Get, Licence);

        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        using var problem = JsonDocument.Parse(
            await denied.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        Assert.Equal(LicensingErrors.Ended.Code, problem.RootElement.GetProperty("code").GetString());
        Assert.Equal(
            "Your trial of TodoWerk ended on 2 October 2026.",
            problem.RootElement.GetProperty("detail").GetString());

        using var served = await SendAsync(client, factory, HttpMethod.Get, Licence, Colleague);

        Assert.Equal(HttpStatusCode.OK, served.StatusCode);
    }

    /// <summary>
    /// The ended card is the one screen in the product with nowhere else to send somebody, so the
    /// operator's contact travels with the refusal. The client renders it in the browser and never
    /// inside the Teams tab, which is that client's decision and its own test.
    /// </summary>
    [Fact]
    public async Task TheEndedProblemCarriesTheOperatorContact()
    {
        using var portal = new FakeManagementPortal(_ =>
            FakeManagementPortal.PortalAnswer.Ok(FakeManagementPortal.Expired("It ended.")));

        using var factory = HostedService(portal)
            .WithConfigurationOverride("Legal:OperatorContact", "support@cloudwerk.test");

        using var client = factory.CreateClient(ClientOptions);

        using var response = await SendAsync(client, factory, HttpMethod.Get, Licence);
        using var problem = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        Assert.Equal(
            "support@cloudwerk.test",
            problem.RootElement.GetProperty(LicenceGateMiddleware.ContactExtension).GetString());
    }

    /// <summary>
    /// The ended refusal carries somewhere to buy. The portal delivers the
    /// URL on a refusal on purpose — a person who has just been told their Trial ended is exactly
    /// who wants it — and the gate is the only route to the one screen they can still see, because
    /// this endpoint is refused them as well.
    /// </summary>
    [Fact]
    public async Task TheEndedProblemCarriesThePurchaseUrl()
    {
        using var portal = new FakeManagementPortal(_ =>
            FakeManagementPortal.PortalAnswer.Ok(FakeManagementPortal.Expired(
                "It ended.",
                "https://portal.todowerk.test/Solutions")));

        using var factory = HostedService(portal);
        using var client = factory.CreateClient(ClientOptions);

        using var response = await SendAsync(client, factory, HttpMethod.Get, Licence);
        using var problem = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        Assert.Equal(
            "https://portal.todowerk.test/Solutions",
            problem.RootElement.GetProperty(LicenceGateMiddleware.PurchaseUrlExtension).GetString());
    }

    /// <summary>
    /// The address on the refusal is the one the portal sent, character for character. It is a URL
    /// a browser is about to open, so escaping is not cosmetic: <c>Uri.ToString()</c> unescapes —
    /// <c>/Sol%20utions</c> comes back as <c>/Sol utions</c>, which is not an address at all.
    /// <para>
    /// The same rule keeps the two surfaces in step. <c>System.Text.Json</c> writes a
    /// <see cref="Uri"/> as its <c>OriginalString</c>, so the banner reads that through
    /// <c>/api/licence</c>; anything else here and one portal address would be spelled two ways
    /// depending on which screen a person was looking at.
    /// </para>
    /// </summary>
    [Fact]
    public async Task TheEndedProblemCarriesThePurchaseUrlExactlyAsThePortalSpelledIt()
    {
        const string Escaped = "https://portal.todowerk.test/Sol%20utions?from=trial%26ended";

        using var portal = new FakeManagementPortal(_ =>
            FakeManagementPortal.PortalAnswer.Ok(FakeManagementPortal.Expired("It ended.", Escaped)));

        using var factory = HostedService(portal);
        using var client = factory.CreateClient(ClientOptions);

        using var response = await SendAsync(client, factory, HttpMethod.Get, Licence);
        using var problem = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        Assert.Equal(
            Escaped,
            problem.RootElement.GetProperty(LicenceGateMiddleware.PurchaseUrlExtension).GetString());
    }

    /// <summary>
    /// A portal that sells nothing for TodoWerk sends no address, and the refusal then carries
    /// none rather than an empty one. Absent is a legitimate state — the client renders no link.
    /// </summary>
    [Fact]
    public async Task TheEndedProblemCarriesNoPurchaseUrlWhenThePortalDeliveredNone()
    {
        using var portal = new FakeManagementPortal(_ =>
            FakeManagementPortal.PortalAnswer.Ok(FakeManagementPortal.Expired("It ended.")));

        using var factory = HostedService(portal);
        using var client = factory.CreateClient(ClientOptions);

        using var response = await SendAsync(client, factory, HttpMethod.Get, Licence);
        using var problem = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        Assert.False(
            problem.RootElement.TryGetProperty(LicenceGateMiddleware.PurchaseUrlExtension, out _));
    }

    /// <summary>
    /// The refusal met by people who have <em>paid</em> offers no way to buy, and there is nothing
    /// to offer one from: an unreachable portal answered nothing at all. The two codes exist to
    /// keep these apart, and a link here would send a paying customer to buy what they already own.
    /// </summary>
    [Fact]
    public async Task TheCouldNotBeVerifiedProblemOffersNowhereToBuy()
    {
        using var portal = new FakeManagementPortal(_ => FakeManagementPortal.PortalAnswer.Unavailable());

        using var factory = HostedService(portal);
        using var client = factory.CreateClient(ClientOptions);

        using var response = await SendAsync(client, factory, HttpMethod.Get, Licence);
        using var problem = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        Assert.Equal(
            LicensingErrors.CouldNotBeVerified.Code,
            problem.RootElement.GetProperty("code").GetString());
        Assert.False(
            problem.RootElement.TryGetProperty(LicenceGateMiddleware.PurchaseUrlExtension, out _));
    }

    /// <summary>
    /// An unreachable portal with nothing cached is the second code, never the first. A paying
    /// customer meets this one during an outage, and the two must not be confusable.
    /// </summary>
    [Fact]
    public async Task AnUnreachablePortalDeniesWithCouldNotBeVerifiedRatherThanEnded()
    {
        using var portal = new FakeManagementPortal(_ => FakeManagementPortal.PortalAnswer.Unavailable());

        using var factory = HostedService(portal);
        using var client = factory.CreateClient(ClientOptions);

        using var response = await SendAsync(client, factory, HttpMethod.Get, Licence);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        using var problem = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        Assert.Equal(
            LicensingErrors.CouldNotBeVerified.Code,
            problem.RootElement.GetProperty("code").GetString());
    }

    /// <summary>
    /// The shell a denied person is looking at is drawn from <c>/api/me</c>, and the way to ask for
    /// erasure lives in that shell. Both endpoints answer whether or not somebody is licensed, and
    /// this is the test that would fail if a future endpoint convention swept them into the gate.
    /// </summary>
    [Fact]
    public async Task ADeniedPersonStillReachesTheirOwnSessionAndTheirWayOut()
    {
        using var portal = new FakeManagementPortal(_ =>
            FakeManagementPortal.PortalAnswer.Ok(FakeManagementPortal.Expired("It ended.")));

        using var factory = HostedService(portal);
        using var client = factory.CreateClient(ClientOptions);

        using var me = await SendAsync(client, factory, HttpMethod.Get, "/api/me");

        Assert.Equal(HttpStatusCode.OK, me.StatusCode);

        // The erasure endpoint is not exercised end to end here — it needs a database, and
        // ErasureTests owns that. What is pinned is that the gate is not what stands in its way:
        // anything but a licensing 403 means the request reached the handler.
        using var erasure = await SendAsync(client, factory, HttpMethod.Post, "/api/me/erasure");

        Assert.NotEqual(HttpStatusCode.Forbidden, erasure.StatusCode);
    }

    /// <summary>
    /// The way out has to work for the person the product has shut out. Exercised end to end,
    /// because the failure it pins is one no unit could see: the sign-out form's POST met the gate
    /// and was answered with the licensing problem document, so a browser navigation ended on a
    /// page of JSON with the session still alive behind it.
    /// </summary>
    [Fact]
    public async Task ADeniedPersonCanStillSignOut()
    {
        using var portal = new FakeManagementPortal(_ =>
            FakeManagementPortal.PortalAnswer.Ok(FakeManagementPortal.Expired("It ended.")));

        using var factory = HostedService(portal);

        // Cookies by hand: this test is about which ones travel with which request, and the
        // antiforgery pair has to be the one the sign-out form would really carry.
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = ClientOptions.BaseAddress,
            HandleCookies = false,
        });

        var session = TestSession.ProtectTicket(factory);

        var (antiforgery, requestToken) = await TestSession.GetAntiforgeryAsync(
            client,
            session,
            TestContext.Current.CancellationToken);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/auth/sign-out")
        {
            Content = new FormUrlEncodedContent(
                new Dictionary<string, string> { [TestSession.AntiforgeryFieldName] = requestToken }),
        };
        request.Headers.Add(
            "Cookie",
            $"{TestSession.SessionCookieName}={session}; {TestSession.AntiforgeryCookieName}={antiforgery}");

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        // The end-session endpoint, which is the whole point: the session cookie is dropped here
        // and the Entra ID session ends on the next hop.
        Assert.Contains(
            "/logout",
            response.Headers.Location!.ToString(),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The endpoints a shut-out person may still reach, by name, and no others.
    /// <para>
    /// The gate is on by default — it stands in front of every endpoint that requires
    /// authorization, including one added tomorrow by somebody who has never read
    /// <c>LicenceGate.cs</c> — so the failure worth catching is not a missing opt-out but an extra
    /// one. The two are not symmetrical. A missing <c>AllowUnlicensed()</c> is loud: somebody is
    /// shut out of something they should reach, and they say so. An unwanted one is silent by
    /// construction, because what it does is let a request through — the endpoint simply keeps
    /// working, for exactly the people it was meant to stop.
    /// </para>
    /// <para>
    /// Read off endpoint metadata rather than by calling each one, so a <c>.AllowUnlicensed()</c>
    /// pasted onto the wrong endpoint fails here rather than in production. That is the likeliest
    /// way this goes wrong, and it is why the assertion is the exact set rather than a rule about
    /// which files may contain one: two of the four live in <c>AuthEndpoints.cs</c>, so any rule
    /// coarse enough to allow that file would wave through a fifth added beside them.
    /// </para>
    /// <para>
    /// Modelled on <c>AuthenticationBoundaryTests.OnlyTheTeamsExchange_IsExemptFromTheAntiforgeryPair</c>,
    /// which does the same thing for the neighbouring escape hatch. Adding an opt-out is meant to
    /// cost two edits: the endpoint, and this list. The second is where somebody has to say the
    /// reason out loud to a reader who is not looking at the endpoint.
    /// </para>
    /// </summary>
    [Fact]
    public void OnlyFourEndpointsStayOpenToSomebodyWithoutALicence()
    {
        // No portal and no fake: which endpoints carry the metadata is a fact about routing, and
        // it is the same fact on a Hosted Service and a Self-Host.
        using var factory = new TodoWerkWebApplicationFactory().WithoutBackgroundWorkers();

        var openToTheUnlicensed = factory.Services
            .GetServices<Microsoft.AspNetCore.Routing.EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .Where(endpoint => endpoint.Metadata.GetMetadata<AllowUnlicensedMetadata>() is not null)
            .Select(endpoint => endpoint.DisplayName)
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(
            [
                // The shell the denied card is drawn inside, which has to load for the card to
                // exist at all.
                "HTTP: GET /api/me",

                // Erasure is an obligation rather than a feature, and a person TodoWerk has shut
                // out is the likeliest of anybody to want it.
                "HTTP: POST /api/me/erasure",

                // Being unable to sign in and being unlicensed are different things with different
                // cards, and the tab has to get through this to be shown either.
                "HTTP: POST /api/teams/session",

                // A person the product has shut out must still be able to leave it.
                "HTTP: POST /auth/sign-out",
            ],
            openToTheUnlicensed);
    }

    /// <summary>
    /// A Self-Host has no Licence, asks nobody and is never refused. Asserted as a fact about the
    /// container as well as about the answer: with no section there is no portal client registered
    /// for anything to reach for by accident.
    /// </summary>
    [Fact]
    public async Task ASelfHostAsksNobodyAndShowsNothing()
    {
        using var portal = new FakeManagementPortal(_ =>
            throw new InvalidOperationException("A Self-Host must not call the licensing portal."));

        using var factory = new TodoWerkWebApplicationFactory()
            .WithoutBackgroundWorkers()
            .WithSharedOutboundHttpHandler(portal);

        using var client = factory.CreateClient(ClientOptions);

        using var document = await ReadAsync(client, factory, Licence);
        var licence = document.RootElement;

        Assert.Equal(JsonValueKind.Null, licence.GetProperty("kind").ValueKind);
        Assert.Equal("None", licence.GetProperty("banner").GetString());

        // Unchanged on a Self-Host: there is no Licence to make the invitation conditional on.
        Assert.True(licence.GetProperty("mayOfferTenantConsent").GetBoolean());

        Assert.Empty(portal.Calls);
        Assert.IsType<SelfHostLicenceResolver>(factory.Services.GetRequiredService<ILicenceResolver>());
        Assert.IsType<SelfHostSeatUsageReporter>(factory.Services.GetRequiredService<ISeatUsageReporter>());
    }

    /// <summary>Every first-party call carries the application key, and only as a header.</summary>
    [Fact]
    public async Task EveryCallToThePortalCarriesTheApplicationKeyAndNoObjectIdInTheHeader()
    {
        using var portal = new FakeManagementPortal(_ =>
            FakeManagementPortal.PortalAnswer.Ok(
                FakeManagementPortal.Valid("tenant", "2027-03-14T00:00:00Z")));

        using var factory = HostedService(portal);
        using var client = factory.CreateClient(ClientOptions);

        await SendAsync(client, factory, HttpMethod.Get, Licence);

        var call = Assert.Single(portal.Calls);

        Assert.Equal("/api/v1/first-party/resolve", call.Path);
        Assert.Equal(FakeManagementPortal.ApplicationKey, call.ApplicationKey);
        Assert.Contains($"\"solutionSlug\":\"{FakeManagementPortal.SolutionSlug}\"", call.Body, StringComparison.Ordinal);
    }

    /// <summary>
    /// A host configured as the Hosted Service is.
    /// <para>
    /// The three settings go in as startup settings rather than ordinary overrides, and they have
    /// to: <c>AddTodoWerkLicensing</c> reads them while services are still being registered, to
    /// decide whether a portal client exists at all, and for a <c>WebApplicationBuilder</c> the
    /// configuration sources an ordinary override adds are layered at <c>Build()</c> — after that
    /// has already happened. A test using the ordinary path gets a Self-Host and every assertion
    /// here fails in a way that reads like a licensing bug.
    /// </para>
    /// </summary>
    private static TodoWerkWebApplicationFactory HostedService(FakeManagementPortal portal) =>
        new TodoWerkWebApplicationFactory()
            .WithoutBackgroundWorkers()
            .WithStartupSetting("Licensing:PortalHost", FakeManagementPortal.BaseAddress)
            .WithStartupSetting("Licensing:ApplicationKey", FakeManagementPortal.ApplicationKey)
            .WithStartupSetting("Licensing:SolutionSlug", FakeManagementPortal.SolutionSlug)
            .WithSharedOutboundHttpHandler(portal);

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        TodoWerkWebApplicationFactory factory,
        HttpMethod method,
        string path,
        string? objectId = null)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Add(
            "Cookie",
            $"{TestSession.SessionCookieName}={TestSession.ProtectTicket(factory, objectId)}");

        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static async Task<JsonDocument> ReadAsync(
        HttpClient client,
        TodoWerkWebApplicationFactory factory,
        string path)
    {
        using var response = await SendAsync(client, factory, HttpMethod.Get, path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }
}
