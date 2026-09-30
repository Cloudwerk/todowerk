using System.Net;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Client;
using Microsoft.Identity.Web.TokenCacheProviders;
using TodoWerk.Infrastructure.Authentication;
using TodoWerk.Infrastructure.Persistence;
using Xunit;

namespace TodoWerk.IntegrationTests;

/// <summary>
/// The durable token cache: a refresh token written by one host is usable by the next
/// (ADR-0002's background jobs depend on it), encrypted at rest, evicted on sign-out, and stamped
/// with an expiry so abandoned entries leave the table.
/// <para>
/// Seeding goes through the application's own registered <see cref="IMsalTokenCacheProvider"/>
/// and MSAL's real serializer — the exact write path a completed sign-in uses — against
/// <see cref="FakeEntraAndGraphHandler"/> standing in for Entra ID and Graph. Only the wire is
/// canned: no test contacts a live tenant (CONTRIBUTING § Testing).
/// </para>
/// </summary>
public sealed class TokenCacheDurabilityTests(SqlServerDatabaseFixture database)
    : IClassFixture<SqlServerDatabaseFixture>, IDisposable
{
    private const string SessionCookieName = "todowerk.session";
    private const string AntiforgeryCookieName = "todowerk.antiforgery";
    private const string RequestTokenCookieName = "XSRF-TOKEN";
    private const string FormFieldName = "__RequestVerificationToken";

    /// <summary>
    /// The cache row for the one user the fake signs in: MSAL keys entries on
    /// <c>uid.utid</c> from <c>client_info</c>.
    /// </summary>
    private const string HomeAccountId =
        $"{FakeEntraAndGraphHandler.UserObjectId}.{TodoWerkWebApplicationFactory.TenantId}";

    /// <summary>
    /// Both hosts in a restart pair share this key ring, as deployments sharing
    /// <c>DataProtection:KeyRingPath</c> do — decrypting yesterday's cache entries and
    /// yesterday's session cookie are the same requirement (ADR-0002).
    /// </summary>
    private readonly string _keyRingPath = Directory.CreateDirectory(Path.Combine(
        Path.GetTempPath(),
        "todowerk-token-cache-tests",
        Guid.NewGuid().ToString("N"))).FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_keyRingPath, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A straggling handle on a key file is not worth failing the test run over.
        }
    }

    [Fact]
    public async Task RefreshToken_SurvivesAHostRestart_AndStillYieldsAGraphCall()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var cloud = new EntraAndGraphRecorder();

        string session;

        await using (var restartedAway = CreateHost(cloud))
        {
            await MigrateAsync(restartedAway, cancellationToken);
            await SeedRefreshTokenAsync(restartedAway, cloud, cancellationToken);

            Assert.Equal(1, await CountCacheRowsAsync(cancellationToken));

            // Issued before the restart, replayed after it — the browser's cookie does not
            // know the deployment changed underneath it.
            session = ProtectSessionTicket(restartedAway);
        }

        await using var survivor = CreateHost(cloud);
        using var client = survivor.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
        });

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/task-lists");
        request.Headers.Add("Cookie", $"{SessionCookieName}={session}");

        using var response = await client.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            $"expected the Graph read to succeed after the restart, got {(int)response.StatusCode}: {payload}");
        Assert.Contains(FakeEntraAndGraphHandler.TaskListDisplayName, payload, StringComparison.Ordinal);

        // The seeded access token was already expired, so a 200 alone does not prove
        // durability — the refresh token must have been redeemed, and Graph must have seen
        // the token that redemption produced.
        Assert.Contains("refresh_token", cloud.TokenGrantTypes);
        Assert.Contains(
            $"Bearer {FakeEntraAndGraphHandler.RefreshedAccessToken}",
            cloud.GraphAuthorizationHeaders);
        Assert.DoesNotContain(
            $"Bearer {FakeEntraAndGraphHandler.SeededAccessToken}",
            cloud.GraphAuthorizationHeaders);
    }

    [Fact]
    public async Task TokenCacheEntries_AtRest_AreEncrypted()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var cloud = new EntraAndGraphRecorder();

        await using var host = CreateHost(cloud);
        await MigrateAsync(host, cancellationToken);
        await SeedRefreshTokenAsync(host, cloud, cancellationToken);

        var (id, value) = await ReadCacheRowAsync(cancellationToken);

        Assert.Contains(FakeEntraAndGraphHandler.UserObjectId, id, StringComparison.OrdinalIgnoreCase);

        // MSAL's serialized cache is JSON with a "RefreshToken" section carrying the secret
        // verbatim. Neither the section name nor the secret may appear in the stored bytes.
        AssertBytesDoNotContain(value, "RefreshToken");
        AssertBytesDoNotContain(value, FakeEntraAndGraphHandler.RefreshTokenValue);
    }

    [Fact]
    public async Task SignOut_EvictsTheUsersTokenCacheEntry()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var cloud = new EntraAndGraphRecorder();

        await using var host = CreateHost(cloud);
        await MigrateAsync(host, cancellationToken);
        await SeedRefreshTokenAsync(host, cloud, cancellationToken);

        Assert.Equal(1, await CountCacheRowsAsync(cancellationToken));

        using var client = host.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
        });

        var session = ProtectSessionTicket(host);

        // The antiforgery pair arrives on a safe request, exactly as the SPA gets it.
        using var page = new HttpRequestMessage(HttpMethod.Get, "/health");
        page.Headers.Add("Cookie", $"{SessionCookieName}={session}");
        using var pageResponse = await client.SendAsync(page, cancellationToken);

        var requestToken = ReadSetCookie(pageResponse, RequestTokenCookieName);
        var antiforgeryCookie = ReadSetCookie(pageResponse, AntiforgeryCookieName);

        using var signOut = new HttpRequestMessage(HttpMethod.Post, "/auth/sign-out")
        {
            Content = new FormUrlEncodedContent(
                new Dictionary<string, string> { [FormFieldName] = Uri.UnescapeDataString(requestToken) }),
        };
        signOut.Headers.Add(
            "Cookie",
            $"{SessionCookieName}={session}; {AntiforgeryCookieName}={antiforgeryCookie}");

        using var response = await client.SendAsync(signOut, cancellationToken);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(0, await CountCacheRowsAsync(cancellationToken));
    }

    /// <summary>
    /// Pins the retention policy, because the failure mode is silent: an entry stored with no
    /// expiry falls back to the SQL cache's 20-minute default, and the first background scan
    /// longer than 20 minutes after the user's last visit dies — in production, weeks after
    /// the code change that caused it.
    /// </summary>
    [Fact]
    public async Task TokenCacheEntries_CarryTheAbandonedEntryExpiry()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var cloud = new EntraAndGraphRecorder();

        await using var host = CreateHost(cloud);
        await MigrateAsync(host, cancellationToken);
        await SeedRefreshTokenAsync(host, cloud, cancellationToken);

        var (slidingSeconds, expiresAt) = await ReadCacheExpiryAsync(cancellationToken);

        // 90 days sliding: every token acquisition slides the window, so an active user's — or
        // an active background job's — entry never lapses, while an abandoned entry outlives
        // its Entra ID refresh token (90 days of inactivity) by nothing.
        var expected = TimeSpan.FromDays(90);

        Assert.Equal((long)expected.TotalSeconds, slidingSeconds);
        Assert.InRange(
            expiresAt,
            DateTimeOffset.UtcNow + expected - TimeSpan.FromMinutes(10),
            DateTimeOffset.UtcNow + expected + TimeSpan.FromMinutes(10));
    }

    private TodoWerkWebApplicationFactory CreateHost(EntraAndGraphRecorder cloud) =>
        new TodoWerkWebApplicationFactory()
            .WithConfigurationOverride("ConnectionStrings:TodoWerk", database.ConnectionString)
            .WithConfigurationOverride("DataProtection:KeyRingPath", _keyRingPath)
            .WithOutboundHttpHandler(() => new FakeEntraAndGraphHandler(cloud));

    private static async Task MigrateAsync(
        TodoWerkWebApplicationFactory host,
        CancellationToken cancellationToken)
    {
        await using var scope = host.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<TodoWerkDbContext>()
            .Database.MigrateAsync(cancellationToken);
    }

    /// <summary>
    /// Redeems an authorization code through MSAL with the application's own registered cache
    /// provider attached — Microsoft.Identity.Web drives the same MSAL API with the same
    /// provider when the OpenID Connect handler hands it a real code.
    /// </summary>
    private static async Task SeedRefreshTokenAsync(
        TodoWerkWebApplicationFactory host,
        EntraAndGraphRecorder cloud,
        CancellationToken cancellationToken)
    {
        using var handler = new FakeEntraAndGraphHandler(cloud);
        using var httpClient = new HttpClient(handler, disposeHandler: false);

        var confidentialClient = ConfidentialClientApplicationBuilder
            .Create(TodoWerkWebApplicationFactory.ClientId)
            .WithClientSecret(TodoWerkWebApplicationFactory.ClientSecret)
            .WithAuthority(new Uri(
                $"https://login.microsoftonline.com/{TodoWerkWebApplicationFactory.TenantId}"))
            .WithInstanceDiscovery(false)
            .WithHttpClientFactory(new SingleClientMsalHttpFactory(httpClient))
            .Build();

        host.Services.GetRequiredService<IMsalTokenCacheProvider>()
            .Initialize(confidentialClient.UserTokenCache);

        var result = await confidentialClient
            .AcquireTokenByAuthorizationCode([.. GraphScopes.SignIn], "fake-authorization-code")
            .ExecuteAsync(cancellationToken);

        Assert.Equal(FakeEntraAndGraphHandler.SeededAccessToken, result.AccessToken);
    }

    /// <summary>
    /// A session cookie the real cookie handler accepts, carrying the claims token acquisition
    /// resolves the MSAL account from: <c>uid</c>/<c>utid</c> must match the seeded entry's
    /// <c>client_info</c> or the cache lookup finds nothing.
    /// </summary>
    private static string ProtectSessionTicket(TodoWerkWebApplicationFactory factory)
    {
        var options = factory.Services
            .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme);

        var identity = new ClaimsIdentity(
            [
                new Claim("uid", FakeEntraAndGraphHandler.UserObjectId),
                new Claim("utid", TodoWerkWebApplicationFactory.TenantId),
                new Claim("oid", FakeEntraAndGraphHandler.UserObjectId),
                new Claim("tid", TodoWerkWebApplicationFactory.TenantId),
                new Claim("preferred_username", "signed-in@todowerk.test"),
                new Claim(ClaimTypes.Name, "Signed In"),
            ],
            CookieAuthenticationDefaults.AuthenticationScheme,
            ClaimTypes.Name,
            ClaimTypes.Role);

        var ticket = new AuthenticationTicket(
            new ClaimsPrincipal(identity),
            CookieAuthenticationDefaults.AuthenticationScheme);

        return options.TicketDataFormat.Protect(ticket);
    }

    private async Task<int> CountCacheRowsAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM dbo.TokenCache";

        return (int)(await command.ExecuteScalarAsync(cancellationToken) ?? 0);
    }

    private async Task<(string Id, byte[] Value)> ReadCacheRowAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Value FROM dbo.TokenCache WHERE Id = @id";
        command.Parameters.AddWithValue("@id", HomeAccountId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        Assert.True(await reader.ReadAsync(cancellationToken), $"no cache row with id {HomeAccountId}");

        return (reader.GetString(0), (byte[])reader.GetValue(1));
    }

    private async Task<(long SlidingSeconds, DateTimeOffset ExpiresAt)> ReadCacheExpiryAsync(
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT SlidingExpirationInSeconds, ExpiresAtTime FROM dbo.TokenCache WHERE Id = @id";
        command.Parameters.AddWithValue("@id", HomeAccountId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        Assert.True(await reader.ReadAsync(cancellationToken), $"no cache row with id {HomeAccountId}");

        return (reader.GetInt64(0), reader.GetDateTimeOffset(1));
    }

    private static void AssertBytesDoNotContain(byte[] haystack, string needle)
    {
        Assert.True(
            haystack.AsSpan().IndexOf(Encoding.UTF8.GetBytes(needle)) < 0,
            $"the stored cache entry contains \"{needle}\" in plaintext");
    }

    /// <summary>The value of one <c>Set-Cookie</c> header, or empty when it was not sent.</summary>
    private static string ReadSetCookie(HttpResponseMessage response, string name)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var cookies))
        {
            return string.Empty;
        }

        var prefix = $"{name}=";

        var match = cookies.FirstOrDefault(cookie => cookie.StartsWith(prefix, StringComparison.Ordinal));

        if (match is null)
        {
            return string.Empty;
        }

        var value = match[prefix.Length..];
        var end = value.IndexOf(';', StringComparison.Ordinal);

        return end < 0 ? value : value[..end];
    }

    /// <summary>MSAL asks a factory for its client; the test has exactly one to give.</summary>
    private sealed class SingleClientMsalHttpFactory(HttpClient httpClient) : IMsalHttpClientFactory
    {
        public HttpClient GetHttpClient() => httpClient;
    }
}
