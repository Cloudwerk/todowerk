using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging;
using Xunit;

namespace TodoWerk.IntegrationTests;

/// <summary>
/// The half of the blocked-cookie diagnosis that is not on the screen. Safari refuses TodoWerk's
/// unpartitioned session cookie inside
/// the Teams frame, and the only trace of it is a pair of responses — an on-behalf-of exchange
/// that succeeded, and a 401 immediately after. The tab sees both halves and says so; the server
/// sees a successful exchange and, moments later, an anonymous 401 it cannot tell from any other.
/// <para>
/// So the confirming request wears a header saying what it is, and a 401 answered to a request
/// wearing it is logged as the browser refusing the cookie rather than as somebody arriving
/// signed out. The header decides a log line and no access, which is why it can be believed.
/// </para>
/// </summary>
public sealed class TeamsBootstrapDiagnosticsTests : IDisposable
{
    /// <summary>
    /// Spelled out rather than taken from <c>TeamsTab.BootstrapHeader</c>. It is a wire contract
    /// between two codebases — the header the TypeScript client sends and the one the C# host
    /// reads — and a constant shared with only one of them would let the pair be renamed apart
    /// without anything failing.
    /// </summary>
    private const string BootstrapHeader = "X-TodoWerk-Teams-Bootstrap";

    private readonly RecordedLogs _logs = new();

    private readonly TodoWerkWebApplicationFactory _factory;

    public TeamsBootstrapDiagnosticsTests()
    {
        _factory = new TodoWerkWebApplicationFactory()
            .WithoutBackgroundWorkers()
            .WithLoggerProvider(_logs);
    }

    private HttpClient CreateClient() => _factory.CreateClient(
        new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    [Fact]
    public async Task A401OnTheBootstrapsConfirmation_IsLoggedAsTheCookieTheBrowserRefused()
    {
        using var client = CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/me");
        request.Headers.Add(BootstrapHeader, "1");
        request.Headers.Add("User-Agent", "Mozilla/5.0 (Macintosh) Version/18.0 Safari/605.1.15");

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var record = Assert.Single(BlockedCookieWarnings());

        // The path, so the line is not merely a category; the user agent, because it says which
        // browser or webview refused the cookie.
        Assert.Contains("/api/me", record.Message, StringComparison.Ordinal);
        Assert.Contains("Safari/605.1.15", record.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The 401 every deployment answers all day: somebody's session lapsed, or a bookmark was
    /// opened cold. Logging that as a browser refusing a cookie would make the diagnosis useless
    /// on the day it is needed.
    /// </summary>
    [Fact]
    public async Task AnOrdinary401_IsNotLoggedAsTheCookieTheBrowserRefused()
    {
        using var client = CreateClient();

        using var response = await client.GetAsync("/api/me", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(BlockedCookieWarnings());
    }

    /// <summary>The 401 is half the signature. A request that succeeds is not the condition.</summary>
    [Fact]
    public async Task TheHeaderOnARequestThatSucceeds_IsNotLoggedAtAll()
    {
        using var client = CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add(BootstrapHeader, "1");

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(BlockedCookieWarnings());
    }

    /// <summary>
    /// The one path on which an anonymous caller decides that anything is written to the log, and
    /// the only part of it they also author. An ordinary 401 writes nothing at all, so without a
    /// bound here a caller could choose the log's volume as well as its contents.
    /// </summary>
    [Fact]
    public async Task AnAbsurdUserAgent_IsLoggedTruncatedRatherThanWhole()
    {
        using var client = CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/me");
        request.Headers.Add(BootstrapHeader, "1");
        request.Headers.TryAddWithoutValidation("User-Agent", new string('a', 4000));

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var record = Assert.Single(BlockedCookieWarnings());

        Assert.DoesNotContain(new string('a', 400), record.Message, StringComparison.Ordinal);
        Assert.Contains("…", record.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The path goes into the line whole, where the user agent is truncated, and the difference is
    /// not an oversight: the caller authors the user agent and the route table authors the path.
    /// A 401 is only ever answered to a request that matched a route, every one of them is a
    /// literal or a <c>{changeId:guid}</c>, and anything else falls through to the SPA's anonymous
    /// document and is answered 200. This pins that, because it is the assumption that would rot
    /// quietly the day somebody adds a route with an unconstrained parameter.
    /// </summary>
    [Fact]
    public async Task APathNoAuthenticatedRouteCanMatch_IsNot401AndIsNotLogged()
    {
        using var client = CreateClient();

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/changes/{new string('a', 4000)}/cancel");
        request.Headers.Add(BootstrapHeader, "1");

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(BlockedCookieWarnings());
    }

    /// <summary>
    /// The ceiling, and why there is one at all: TodoWerk's rate limiter runs *after* authorization,
    /// which short-circuits an unauthenticated request — so a 401 costs an anonymous caller nothing
    /// and the limiter never sees it. Without this ceiling the one path on which an anonymous
    /// request decides that something is written to the log would also let it decide how much.
    /// <para>
    /// The ceiling reports itself once rather than going quiet, because an operator reading these
    /// to find out which clients refuse the cookie has to know the answer in front of them is
    /// partial.
    /// </para>
    /// <para>
    /// Not covered here: the count of what a full window suppressed, which is written on the first
    /// occurrence of the <em>next</em> window and would need a test that either waits a minute or
    /// hands the middleware a clock it does not otherwise want. Left untested rather than reshaped
    /// for the test.
    /// </para>
    /// </summary>
    [Fact]
    public async Task PastTheCeiling_TheLineIsNotWrittenAndTheCeilingSaysSoOnce()
    {
        const int ceiling = 60;

        using var client = CreateClient();

        for (var attempt = 0; attempt < ceiling + 3; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/me");
            request.Headers.Add(BootstrapHeader, "1");

            using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        Assert.Equal(ceiling, BlockedCookieWarnings().Count);
        Assert.Single(CeilingWarnings());
    }

    /// <summary>
    /// A diagnostic, and diagnostics do not get to change the answer: the tab's decision is made
    /// from the response, and a header that altered it would move the decision to the server.
    /// </summary>
    [Fact]
    public async Task TheHeader_ChangesNothingAboutTheResponse()
    {
        using var client = CreateClient();

        using var marked = new HttpRequestMessage(HttpMethod.Get, "/api/me");
        marked.Headers.Add(BootstrapHeader, "1");

        using var withHeader = await client.SendAsync(marked, TestContext.Current.CancellationToken);
        using var without = await client.GetAsync("/api/me", TestContext.Current.CancellationToken);

        Assert.Equal(without.StatusCode, withHeader.StatusCode);
        Assert.Equal(without.Content.Headers.ContentType?.MediaType, withHeader.Content.Headers.ContentType?.MediaType);

        // The document, minus the trace identifier, which is per-request by design.
        Assert.Equal(await ProblemAsync(without), await ProblemAsync(withHeader));

        static async Task<string?> ProblemAsync(HttpResponseMessage response)
        {
            using var document = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

            return JsonSerializer.Serialize(
                document.RootElement.EnumerateObject()
                    .Where(property => property.Name != "traceId")
                    .ToDictionary(property => property.Name, property => property.Value.ToString()));
        }
    }

    private IReadOnlyList<RecordedLog> BlockedCookieWarnings() => Warnings("did not keep TodoWerk's session cookie");

    private IReadOnlyList<RecordedLog> CeilingWarnings() => Warnings("are not logged");

    private IReadOnlyList<RecordedLog> Warnings(string containing) =>
    [
        .. _logs.Records.Where(record =>
            record.Level >= LogLevel.Warning
            && record.Message.Contains(containing, StringComparison.Ordinal)),
    ];

    public void Dispose()
    {
        _factory.Dispose();
        _logs.Dispose();
    }
}
