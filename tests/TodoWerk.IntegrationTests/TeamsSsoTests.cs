using System.Net;
using System.Text.Json;
using Xunit;

namespace TodoWerk.IntegrationTests;

/// <summary>
/// The tab's bootstrap: one request that turns a Teams identity into the same session cookie the
/// browser gets, and the failures it has to keep apart.
/// <para>
/// The consent-required answer is the one the client switches on, and everything hangs off it
/// being distinguishable. A tenant that has not granted Tenant Consent meets it on every first run
/// and is offered the popup; a tenant meeting anything else must be told to retry, because a popup
/// that cannot fix the problem is a popup the person is sent through forever.
/// </para>
/// </summary>
public sealed class TeamsSsoTests(SqlServerDatabaseFixture database) : IClassFixture<SqlServerDatabaseFixture>
{
    [Fact]
    public async Task AValidToken_IsExchangedOnBehalfOfAndEstablishesTheSessionCookie()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var host = await SignInTestHost.StartAsync(database.ConnectionString, cancellationToken);

        using var response = await host.ExchangeTeamsSsoAsync(TeamsSsoToken.Mint(), cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.NotEmpty(TestSession.ReadSetCookie(response, TestSession.SessionCookieName));

        // The grant type, not merely "a token was fetched": an authorization-code redemption would
        // also leave a token behind and would mean the tab had signed somebody in through a flow
        // it never ran.
        Assert.Contains(
            FakeEntraAndGraphHandler.OnBehalfOfGrantType,
            host.Cloud.TokenGrantTypes,
            StringComparer.Ordinal);
    }

    /// <summary>
    /// The session that comes out is an ordinary one: it is accepted by an endpoint that knows
    /// nothing about Teams, which is the whole of what ADR-0010 bought by choosing a cookie.
    /// </summary>
    [Fact]
    public async Task TheSessionItEstablishes_IsAcceptedByTheOrdinaryApi()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var host = await SignInTestHost.StartAsync(database.ConnectionString, cancellationToken);

        string session;

        using (var exchange = await host.ExchangeTeamsSsoAsync(TeamsSsoToken.Mint(), cancellationToken))
        {
            session = $"{TestSession.SessionCookieName}={TestSession.ReadSetCookie(exchange, TestSession.SessionCookieName)}";
        }

        var me = await host.GetAsync<SessionUserPayload>("/api/me", session, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        Assert.Equal("Signed In", me.Body?.DisplayName);
    }

    /// <summary>
    /// The half of "the session is an ordinary one" that <c>/api/me</c> cannot show, because
    /// <c>/api/me</c> reads claims out of a cookie and never asks Microsoft for anything.
    /// <para>
    /// A browser sign-in redeems an authorization code, and MSAL files the result under the home
    /// account id that <c>GraphGateway</c> later looks it up by. An on-behalf-of exchange files its
    /// result under a hash of the assertion instead — a different partition of the same distributed
    /// cache — so a tab could sign somebody in perfectly and then fail every single Graph call with
    /// "reconnect required", in a tenant that had consented and with nothing wrong. This test is the
    /// only thing in the suite that would notice.
    /// </para>
    /// </summary>
    [Fact]
    public async Task AfterTheExchange_TheSessionCanActuallyReachGraph()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var host = await SignInTestHost.StartAsync(database.ConnectionString, cancellationToken);

        string session;

        using (var exchange = await host.ExchangeTeamsSsoAsync(TeamsSsoToken.Mint(), cancellationToken))
        {
            Assert.Equal(HttpStatusCode.NoContent, exchange.StatusCode);
            session = $"{TestSession.SessionCookieName}={TestSession.ReadSetCookie(exchange, TestSession.SessionCookieName)}";
        }

        var lists = await host.GetAsync<TaskListPayload[]>("/api/task-lists", session, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, lists.StatusCode);
        Assert.Contains(
            lists.Body ?? [],
            list => list.DisplayName == FakeEntraAndGraphHandler.TaskListDisplayName);
    }

    /// <summary>
    /// And the same again after the process that performed the exchange is gone, which is what a
    /// background scan is: no request, no assertion in hand, only the durable cache and the two
    /// identifiers the queue row carries.
    /// </summary>
    [Fact]
    public async Task AfterTheExchange_ABackgroundScanCanStillReachGraph()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var host = await SignInTestHost.StartAsync(database.ConnectionString, cancellationToken);

        using (await host.ExchangeTeamsSsoAsync(TeamsSsoToken.Mint(), cancellationToken))
        {
        }

        await host.ScanAsync(cancellationToken);

        // The scan reads the list of lists through the gateway before anything else, so a token it
        // could not find shows up as a scan that indexed nothing at all.
        Assert.NotEmpty(host.Cloud.GraphAuthorizationHeaders);
    }

    [Fact]
    public async Task TheExchange_WritesTheTenantMemberRecordExactlyAsTheBrowserPathDoes()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var host = await SignInTestHost.StartAsync(database.ConnectionString, cancellationToken);

        using (await host.ExchangeTeamsSsoAsync(TeamsSsoToken.Mint(), cancellationToken))
        {
        }

        var member = Assert.Single(await host.ReadMembersAsync(cancellationToken));

        Assert.Equal(FakeEntraAndGraphHandler.UserObjectId, member.UserId);
        Assert.Equal(TodoWerkWebApplicationFactory.TenantId, member.TenantId);
        Assert.Equal(member.FirstSignedInAt, member.LastSignedInAt);
    }

    /// <summary>
    /// No token, of any kind, in any response body — including the successful one, which has no
    /// body at all (ADR-0002).
    /// </summary>
    [Fact]
    public async Task NoResponseBody_CarriesAToken()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var host = await SignInTestHost.StartAsync(database.ConnectionString, cancellationToken);

        using var response = await host.ExchangeTeamsSsoAsync(TeamsSsoToken.Mint(), cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        Assert.Empty(body);
        Assert.DoesNotContain(
            FakeEntraAndGraphHandler.SeededAccessToken,
            body,
            StringComparison.Ordinal);
    }

    [Theory]
    // The audience Microsoft.Identity.Web would derive from the client id on its own, which is not
    // the audience a Teams client asks for — and which must not become a second accepted one.
    [InlineData("wrong-audience")]
    [InlineData("api://somebody-elses-host/11111111-1111-1111-1111-111111111111")]
    public async Task ATokenForAnotherAudience_IsRefusedWithNoSession(string audience)
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var host = await SignInTestHost.StartAsync(database.ConnectionString, cancellationToken);

        using var response = await host.ExchangeTeamsSsoAsync(
            TeamsSsoToken.Mint(audience: audience),
            cancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(TestSession.ReadSetCookie(response, TestSession.SessionCookieName));
    }

    [Fact]
    public async Task ATokenFromAnotherIssuer_IsRefused()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var host = await SignInTestHost.StartAsync(database.ConnectionString, cancellationToken);

        using var response = await host.ExchangeTeamsSsoAsync(
            TeamsSsoToken.Mint(issuer: "https://login.microsoftonline.com/somebody-else/v2.0"),
            cancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(TestSession.ReadSetCookie(response, TestSession.SessionCookieName));
    }

    [Fact]
    public async Task ATokenSignedByAKeyTheHostDoesNotTrust_IsRefused()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var host = await SignInTestHost.StartAsync(database.ConnectionString, cancellationToken);

        using var response = await host.ExchangeTeamsSsoAsync(
            TeamsSsoToken.Mint(credentials: TeamsSsoToken.UntrustedCredentials),
            cancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(TestSession.ReadSetCookie(response, TestSession.SessionCookieName));
    }

    [Fact]
    public async Task NoTokenAtAll_IsRefused()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var host = await SignInTestHost.StartAsync(database.ConnectionString, cancellationToken);

        using var response = await host.ExchangeTeamsSsoAsync(token: null, cancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(TestSession.ReadSetCookie(response, TestSession.SessionCookieName));
    }

    /// <summary>
    /// The expected first run of every tenant that has not granted Tenant Consent — not an error,
    /// and the one answer the client is allowed to raise a popup on.
    /// </summary>
    [Fact]
    public async Task WithoutConsentForTheGraphScopes_TheAnswerNamesConsentSpecifically()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var host = await SignInTestHost.StartAsync(
            database.ConnectionString,
            cancellationToken,
            refuseOnBehalfOfWith: FakeEntraAndGraphHandler.ConsentRequiredError);

        using var response = await host.ExchangeTeamsSsoAsync(TeamsSsoToken.Mint(), cancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("TeamsSso.ConsentRequired", await CodeOf(response, cancellationToken));
        Assert.Empty(TestSession.ReadSetCookie(response, TestSession.SessionCookieName));
    }

    /// <summary>
    /// And the failure that looks nothing like consent from the outside and everything like it from
    /// the inside — the exchange refused, no session, an exception out of MSAL. If this carried the
    /// consent code the tenant would be sent through a popup that changes nothing, on a loop.
    /// </summary>
    [Fact]
    public async Task AFailureNoConsentScreenFixes_IsToldApartFromConsent()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var host = await SignInTestHost.StartAsync(
            database.ConnectionString,
            cancellationToken,
            refuseOnBehalfOfWith: FakeEntraAndGraphHandler.InvalidClientError);

        using var response = await host.ExchangeTeamsSsoAsync(TeamsSsoToken.Mint(), cancellationToken);

        var code = await CodeOf(response, cancellationToken);

        Assert.NotEqual("TeamsSso.ConsentRequired", code);
        Assert.Equal("TeamsSso.ExchangeFailed", code);
        Assert.Empty(TestSession.ReadSetCookie(response, TestSession.SessionCookieName));
    }

    /// <summary>
    /// A token that validates and identifies nobody. Not reachable from a real Entra ID token; here
    /// because the alternative is a session cookie with no identity in it, and because it must not
    /// be mistaken for the consent case either.
    /// </summary>
    [Fact]
    public async Task ATokenWithNoTenantOrObjectId_EstablishesNoSession()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var host = await SignInTestHost.StartAsync(database.ConnectionString, cancellationToken);

        using var response = await host.ExchangeTeamsSsoAsync(
            TeamsSsoToken.Mint(withIdentityClaims: false),
            cancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("TeamsSso.IdentityIncomplete", await CodeOf(response, cancellationToken));
        Assert.Empty(TestSession.ReadSetCookie(response, TestSession.SessionCookieName));
    }

    /// <summary>The RFC 7807 <c>code</c> extension, which is what the client switches on.</summary>
    private static async Task<string?> CodeOf(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        using var problem = JsonDocument.Parse(body);

        return problem.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private sealed record SessionUserPayload(string DisplayName, string Username);

    private sealed record TaskListPayload(string Id, string DisplayName);
}
