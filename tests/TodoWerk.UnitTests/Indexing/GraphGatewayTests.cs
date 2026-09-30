using System.Net;
using TodoWerk.Infrastructure.Indexing;
using TodoWerk.SharedKernel;
using Xunit;
using TodoWerk.Infrastructure.Graph;

namespace TodoWerk.UnitTests.Indexing;

/// <summary>
/// What every Graph call inherits: the delegated token, the throttling it has to survive, and the
/// difference between "try again" and "ask the user to sign in".
/// </summary>
public sealed class GraphGatewayTests
{
    private const string EmptyCollection = """{"value":[]}""";

    /// <summary>
    /// The claim that makes background work possible at all: MSAL finds a user's cache entry by
    /// tenant and object id, so a scan with no HTTP request in sight can still get a token.
    /// </summary>
    [Fact]
    public async Task GetAsync_AsksForATokenForTheGivenUser_NotForWhoeverIsSignedIn()
    {
        var authorization = new GraphTestHost.StubAuthorizationHeaderProvider();
        using var host = new GraphTestHost(
            new GraphTestHost.ScriptedHandler(HttpStatusCode.OK, EmptyCollection),
            authorization);

        await GetAsync(host);

        var principal = Assert.IsType<System.Security.Claims.ClaimsPrincipal>(authorization.SeenPrincipal);
        Assert.Contains(
            principal.Claims,
            claim => claim.Value == GraphTestHost.User.UserId);
        Assert.Contains(
            principal.Claims,
            claim => claim.Value == GraphTestHost.User.TenantId);
    }

    [Fact]
    public async Task GetAsync_SendsTheDelegatedTokenAsABearerHeader()
    {
        using var host = GraphTestHost.Answering(HttpStatusCode.OK, EmptyCollection);

        await GetAsync(host);

        Assert.Equal("Bearer", host.Handler.LastRequest?.Headers.Authorization?.Scheme);
        Assert.Equal("delegated-token", host.Handler.LastRequest?.Headers.Authorization?.Parameter);
    }

    [Fact]
    public async Task GetAsync_WhenTokenAcquisitionNeedsTheUser_AsksThemToReconnect()
    {
        using var host = new GraphTestHost(
            new GraphTestHost.ScriptedHandler(HttpStatusCode.OK, EmptyCollection),
            new GraphTestHost.ChallengingAuthorizationHeaderProvider());

        var result = await GetAsync(host);

        Assert.True(result.IsFailure);
        Assert.Equal(GraphErrors.ReconnectRequired, result.Error);
        Assert.Empty(host.Handler.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task GetAsync_WhenGraphRejectsTheToken_AsksTheUserToReconnect(HttpStatusCode statusCode)
    {
        using var host = GraphTestHost.Answering(statusCode, "{}");

        var result = await GetAsync(host);

        Assert.Equal(GraphErrors.ReconnectRequired, result.Error);
    }

    /// <summary>
    /// A 401 can simply mean the header in hand grew old — a throttled page waits out minutes of
    /// Retry-After pauses. One fresh header before believing it, or an expired token turns into a
    /// "reconnect" the user never needed to see.
    /// </summary>
    [Fact]
    public async Task GetAsync_AfterA401_TriesOnceWithAFreshHeaderBeforeGivingUp()
    {
        using var host = new GraphTestHost(
            new GraphTestHost.ScriptedHandler(
                (HttpStatusCode.Unauthorized, "{}"),
                (HttpStatusCode.OK, EmptyCollection)));

        var result = await GetAsync(host);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, host.Handler.Requests.Count);
    }

    /// <summary>
    /// A timeout is a cancellation nobody asked for. Left to escape it would sail past the
    /// caller's per-list failure handling and fail the whole scan, so it becomes a result here.
    /// </summary>
    [Theory]
    [MemberData(nameof(TransportFailures))]
    public async Task GetAsync_WhenGraphDoesNotAnswer_ReportsItUnavailableRatherThanThrowing(Exception failure)
    {
        var handler = new GraphTestHost.ScriptedHandler(HttpStatusCode.OK, EmptyCollection) { Throws = failure };
        using var host = new GraphTestHost(handler);

        var result = await GetAsync(host);

        Assert.True(result.IsFailure);
        Assert.Equal(GraphErrors.Unavailable, result.Error);
    }

    public static TheoryData<Exception> TransportFailures() =>
    [
        new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout."),
        new HttpRequestException("No such host is known."),
    ];

    /// <summary>
    /// The bypass a naive check leaves open: a protocol-relative link is not an absolute URI, so
    /// it passes a check made on the string as given — and is then resolved into another origin,
    /// with the delegated token attached.
    /// </summary>
    [Theory]
    [InlineData("https://evil.example/v1.0/me/todo/lists")]
    [InlineData("http://graph.microsoft.com/v1.0/me/todo/lists")]
    [InlineData("//evil.example/v1.0/me/todo/lists")]
    public async Task GetAsync_RefusesToSendTheTokenAnywhereButGraph(string destination)
    {
        using var host = GraphTestHost.Answering(HttpStatusCode.OK, EmptyCollection);

        var result = await host.Gateway.GetAsync<GraphTestResponse>(
            GraphTestHost.User,
            destination,
            TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(GraphErrors.Unavailable, result.Error);
        Assert.Empty(host.Handler.Requests);
    }

    /// <summary>
    /// Throttling is a normal outcome of scanning a real mailbox, not an incident: Graph says how
    /// long to wait and the scan waits, up to the configured number of attempts.
    /// </summary>
    [Fact]
    public async Task GetAsync_RetriesAThrottledRequest_ThenSucceeds()
    {
        using var host = new GraphTestHost(
            new GraphTestHost.ScriptedHandler(
                (HttpStatusCode.TooManyRequests, "{}"),
                (HttpStatusCode.TooManyRequests, "{}"),
                (HttpStatusCode.OK, EmptyCollection)),
            maxThrottleRetries: 3);

        var result = await GetAsync(host);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, host.Handler.Requests.Count);
    }

    [Fact]
    public async Task GetAsync_WhenThrottlingOutlastsTheRetries_ReportsThrottled()
    {
        using var host = new GraphTestHost(
            new GraphTestHost.ScriptedHandler(HttpStatusCode.TooManyRequests, "{}"),
            maxThrottleRetries: 2);

        var result = await GetAsync(host);

        Assert.Equal(GraphErrors.Throttled, result.Error);
        Assert.Equal(3, host.Handler.Requests.Count);
    }

    /// <summary>
    /// A 503 is retried like a 429 — Graph sends the same header — but it does not mean this
    /// account is being rate-limited, and the message a user sees should not say so.
    /// </summary>
    [Fact]
    public async Task GetAsync_WhenGraphIsUnavailable_RetriesButReportsItAsUnavailable()
    {
        using var host = new GraphTestHost(
            new GraphTestHost.ScriptedHandler(HttpStatusCode.ServiceUnavailable, "{}"),
            maxThrottleRetries: 1);

        var result = await GetAsync(host);

        Assert.Equal(GraphErrors.Unavailable, result.Error);
        Assert.Equal(2, host.Handler.Requests.Count);
    }

    /// <summary>
    /// The resync signal. Graph expires delta tokens and says so with a 410 carrying this code;
    /// mistaking it for an ordinary failure would leave a list syncing against a dead token
    /// forever.
    /// </summary>
    [Fact]
    public async Task GetAsync_WhenGraphExpiresTheDeltaToken_ReportsResyncRequired()
    {
        using var host = GraphTestHost.Answering(
            HttpStatusCode.Gone,
            """{"error":{"code":"resyncRequired","message":"Resync required."}}""");

        var result = await GetAsync(host);

        Assert.Equal(GraphErrors.ResyncRequired, result.Error);
    }

    /// <summary>A 410 that is not the resync signal must not silently discard a delta token.</summary>
    [Fact]
    public async Task GetAsync_WhenAGoneResponseIsNotTheResyncSignal_ReportsAnOrdinaryFailure()
    {
        using var host = GraphTestHost.Answering(
            HttpStatusCode.Gone,
            """{"error":{"code":"itemNotFound","message":"The list is gone."}}""");

        var result = await GetAsync(host);

        Assert.Equal(GraphErrors.Unavailable, result.Error);
    }

    private static Task<Result<GraphTestResponse>> GetAsync(GraphTestHost host) =>
        host.Gateway.GetAsync<GraphTestResponse>(
            GraphTestHost.User,
            "v1.0/me/todo/lists",
            TestContext.Current.CancellationToken);

    /// <summary>Any shape will do here; these tests are about the call, not about the payload.</summary>
    internal sealed record GraphTestResponse(IReadOnlyList<string>? Value);
}
