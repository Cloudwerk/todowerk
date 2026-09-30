using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TodoWerk.Application.Abstractions.Authentication;
using TodoWerk.Domain.Changes;
using TodoWerk.Domain.Indexing;
using TodoWerk.Domain.Markers;
using TodoWerk.Domain.Onboarding;
using TodoWerk.Infrastructure.Changes;
using TodoWerk.Infrastructure.Changes.Persistence;
using TodoWerk.Infrastructure.Indexing;
using TodoWerk.Infrastructure.Indexing.Persistence;
using TodoWerk.Infrastructure.Onboarding;
using TodoWerk.Infrastructure.Persistence;

namespace TodoWerk.IntegrationTests;

/// <summary>
/// A booted application with a real database and the fake cloud behind it, able to complete a real
/// sign-in — which is what everything the Onboarding module does hangs off.
/// <para>
/// Sibling of <see cref="ChangeTestHost"/> rather than an extension of it: that one seeds a refresh
/// token straight into the durable cache so a Change can write without anybody signing in, and
/// these tests need the opposite — the sign-in itself, because the write they are about happens
/// there.
/// </para>
/// </summary>
internal sealed class SignInTestHost : IAsyncDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly string _keyRingPath;

    private readonly FakeManagementPortal? _portal;

    private HttpClient? _client;

    private SignInTestHost(
        TodoWerkWebApplicationFactory factory,
        string keyRingPath,
        EntraAndGraphRecorder cloud,
        FakeManagementPortal? portal)
    {
        Factory = factory;
        _keyRingPath = keyRingPath;
        Cloud = cloud;
        _portal = portal;
    }

    internal TodoWerkWebApplicationFactory Factory { get; }

    /// <summary>
    /// The portal this host resolves Licences against, for the tests that ask what it was called
    /// about. Throws rather than answering null on a Self-Host: a test reading this has already
    /// decided it is a Hosted Service.
    /// </summary>
    internal FakeManagementPortal Portal => _portal
        ?? throw new InvalidOperationException("This host was started without a licensing portal.");

    /// <summary>
    /// What the fake cloud saw. The grant types in particular: an on-behalf-of exchange and an
    /// authorization-code redemption are two different flows, and only the recorder says which
    /// one a test actually provoked.
    /// </summary>
    internal EntraAndGraphRecorder Cloud { get; }

    /// <summary>The one person the fake Entra ID ever signs in.</summary>
    internal static IndexUser User { get; } =
        new(TodoWerkWebApplicationFactory.TenantId, FakeEntraAndGraphHandler.UserObjectId);

    /// <param name="licensing">
    /// What ManagementPortal answers about one person, given the resolve call's body — or null for
    /// a Self-Host, which asks nobody and licenses everybody. Supplying it is what makes this host
    /// a Hosted Service, and the three settings that takes go in as startup settings for the
    /// reason <c>LicensingTests.HostedService</c> gives: the licensing composition reads them
    /// while services are still being registered.
    /// </param>
    internal static async Task<SignInTestHost> StartAsync(
        string connectionString,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string?>? settings = null,
        FakeTodoTenant? tenant = null,
        bool withWorkers = false,
        string? refuseOnBehalfOfWith = null,
        Func<string, FakeManagementPortal.PortalAnswer>? licensing = null,
        ILoggerProvider? logs = null)
    {
        var keyRingPath = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(),
            "todowerk-sign-in-tests",
            Guid.NewGuid().ToString("N"))).FullName;

        var cloud = new EntraAndGraphRecorder();

        // One handler for every pipeline when there is a portal to record, rather than one per
        // pipeline: what a test asks afterwards is which calls the application made, and a
        // recording split across the handlers the client factory happened to build answers that
        // question wrongly. It stands in front of the same fake cloud either way, and is shared
        // through the seam that stops the client factory disposing it under the pipelines still
        // holding it.
        var portal = licensing is null
            ? null
            : new FakeManagementPortal(
                licensing,
                new FakeEntraAndGraphHandler(cloud, tenant, refuseOnBehalfOfWith));

        var factory = new TodoWerkWebApplicationFactory()
            .WithConfigurationOverride("ConnectionStrings:TodoWerk", connectionString)
            .WithConfigurationOverride("DataProtection:KeyRingPath", keyRingPath)
            .WithConfigurationOverride("Graph:MaxRetryDelay", "00:00:00");

        if (portal is null)
        {
            factory.WithOutboundHttpHandler(
                () => new FakeEntraAndGraphHandler(cloud, tenant, refuseOnBehalfOfWith));
        }
        else
        {
            factory
                .WithSharedOutboundHttpHandler(portal)
                .WithStartupSetting("Licensing:PortalHost", FakeManagementPortal.BaseAddress)
                .WithStartupSetting("Licensing:ApplicationKey", FakeManagementPortal.ApplicationKey)
                .WithStartupSetting("Licensing:SolutionSlug", FakeManagementPortal.SolutionSlug);
        }

        foreach (var (key, value) in settings ?? new Dictionary<string, string?>())
        {
            factory.WithConfigurationOverride(key, value);
        }

        if (logs is not null)
        {
            factory.WithLoggerProvider(logs);
        }

        if (!withWorkers)
        {
            factory.WithoutBackgroundWorkers();
        }

        var host = new SignInTestHost(factory, keyRingPath, cloud, portal);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<TodoWerkDbContext>();

            await context.Database.MigrateAsync(cancellationToken);

            // One database serves the whole class, and every test in it works the same person, so
            // each starts from an empty everything rather than from what the last test left.
            // Changes first: the plan rows and journals cascade off them.
            await context.Set<Change>().ExecuteDeleteAsync(cancellationToken);
            await context.Set<HashtagOccurrence>().ExecuteDeleteAsync(cancellationToken);
            await context.Set<IndexedTask>().ExecuteDeleteAsync(cancellationToken);
            await context.Set<TaskListIndexState>().ExecuteDeleteAsync(cancellationToken);
            await context.Set<IndexScan>().ExecuteDeleteAsync(cancellationToken);
            await context.Set<MarkerRule>().ExecuteDeleteAsync(cancellationToken);
            await context.Set<TenantMember>().ExecuteDeleteAsync(cancellationToken);
            await context.Set<TenantConsentGrant>().ExecuteDeleteAsync(cancellationToken);
        }

        return host;
    }

    /// <summary>
    /// One browser for the whole host, so a test that signs in and then reads is one session
    /// rather than two. Redirects are not followed: the sign-in flow is a chain of them, and each
    /// hop is something a test may want to look at.
    /// </summary>
    internal HttpClient Client => _client ??=
        Factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,

            // The cookies are handled by hand, because which cookie travels with which request is
            // the whole subject of a sign-in test.
            HandleCookies = false,
        });

    internal AsyncServiceScope Scope() => Factory.Services.CreateAsyncScope();

    /// <summary>
    /// A colleague, written straight into the table. There is one person the fake Entra ID can sign
    /// in, and the overview is about how many people a tenant has — so the others are seeded rather
    /// than impersonated.
    /// </summary>
    internal async Task SeedMemberAsync(
        string userId,
        DateTimeOffset firstSignedInAt,
        DateTimeOffset lastSignedInAt,
        CancellationToken cancellationToken)
    {
        await using var scope = Scope();

        var member = TenantMember.FirstSignIn(TodoWerkWebApplicationFactory.TenantId, userId, firstSignedInAt);
        member.SignedInAgain(lastSignedInAt);

        var context = scope.ServiceProvider.GetRequiredService<TodoWerkDbContext>();
        context.Add(member);

        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// A colleague who has since been forgotten: a membership row anonymised the way erasure leaves
    /// one — no object id, no last moment, the tenant and the first moment kept.
    /// </summary>
    internal async Task SeedAnonymisedMemberAsync(
        DateTimeOffset firstSignedInAt,
        CancellationToken cancellationToken)
    {
        await using var scope = Scope();

        var member = TenantMember.FirstSignIn(
            TodoWerkWebApplicationFactory.TenantId,
            // The id only exists to be removed on the next line; Anonymise is the real seed.
            $"anonymised-{Guid.NewGuid():N}",
            firstSignedInAt);
        member.Anonymise();

        var context = scope.ServiceProvider.GetRequiredService<TodoWerkDbContext>();
        context.Add(member);

        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>The membership rows of the tenant every test here runs in.</summary>
    internal async Task<List<TenantMember>> ReadMembersAsync(CancellationToken cancellationToken)
    {
        await using var scope = Scope();

        return await scope.ServiceProvider.GetRequiredService<TodoWerkDbContext>()
            .Set<TenantMember>()
            .AsNoTracking()
            .Where(member => member.TenantId == TodoWerkWebApplicationFactory.TenantId)
            .OrderBy(member => member.FirstSignedInAt)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// How many rows the durable token cache holds. Asked in SQL rather than through MSAL, because
    /// the claim being tested is that the row is gone rather than that a lookup misses.
    /// </summary>
    internal async Task<int> CountTokenCacheRowsAsync(CancellationToken cancellationToken)
    {
        await using var scope = Scope();
        var context = scope.ServiceProvider.GetRequiredService<TodoWerkDbContext>();

        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM dbo.TokenCache";

        await context.Database.OpenConnectionAsync(cancellationToken);

        try
        {
            return Convert.ToInt32(
                await command.ExecuteScalarAsync(cancellationToken),
                System.Globalization.CultureInfo.InvariantCulture);
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }
    }

    /// <summary>A completed interactive sign-in, and the session cookie header it produced.</summary>
    internal Task<string> SignInAsync(CancellationToken cancellationToken) =>
        TestSignIn.CompleteAsync(Client, cancellationToken);

    /// <summary>
    /// The one request the Teams tab makes on load: the token from <c>getAuthToken()</c>, offered
    /// to the exchange endpoint in the Authorization header. No antiforgery pair and no session
    /// cookie, because at this point the tab has neither — the whole point of the call is to get
    /// one.
    /// </summary>
    /// <param name="token">
    /// The Teams SSO token, or <c>null</c> to make the call the way a caller with none would.
    /// </param>
    internal async Task<HttpResponseMessage> ExchangeTeamsSsoAsync(
        string? token,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/teams/session");

        if (token is not null)
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        }

        return await Client.SendAsync(request, cancellationToken);
    }

    /// <summary>One safe request as a signed-in caller.</summary>
    internal async Task<ApiResult<T>> GetAsync<T>(
        string path,
        string session,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);

        if (!string.IsNullOrEmpty(session))
        {
            request.Headers.Add("Cookie", session);
        }

        return await SendAsync<T>(request, cancellationToken);
    }

    /// <summary>
    /// A form POST from a signed-in browser, carrying the antiforgery token as a hidden field —
    /// the way the SPA submits the one control that has to end in a browser-followed redirect.
    /// </summary>
    internal async Task<HttpResponseMessage> PostFormAsync(
        string path,
        string session,
        CancellationToken cancellationToken)
    {
        var (antiforgery, requestToken) = await AntiforgeryAsync(session, cancellationToken);

        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                [TestSession.AntiforgeryFieldName] = requestToken,
            }),
        };
        request.Headers.Add("Cookie", $"{session}; {TestSession.AntiforgeryCookieName}={antiforgery}");

        return await Client.SendAsync(request, cancellationToken);
    }

    /// <summary>One safe request as a caller carrying no session at all.</summary>
    internal async Task<HttpResponseMessage> GetAnonymousAsync(string path, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);

        return await Client.SendAsync(request, cancellationToken);
    }

    /// <summary>
    /// The response body as it went over the wire. For the assertions that are about what a payload
    /// does <em>not</em> contain: a field nobody deserialises is still a field somebody received.
    /// </summary>
    internal async Task<string> GetRawAsync(string path, string session, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("Cookie", session);

        using var response = await Client.SendAsync(request, cancellationToken);

        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    /// <summary>
    /// Indexes the fake mailbox the way the worker would, so a test that counts Occurrences is
    /// counting an index that was really built rather than rows it wrote by hand.
    /// </summary>
    internal async Task ScanAsync(CancellationToken cancellationToken)
    {
        await using var scope = Scope();
        var context = scope.ServiceProvider.GetRequiredService<TodoWerkDbContext>();
        var scheduler = scope.ServiceProvider.GetRequiredService<IndexScanScheduler>();

        context.Add(IndexScan.Request(User.TenantId, User.UserId, IndexScanMode.Full, null, DateTimeOffset.UtcNow));
        await context.SaveChangesAsync(cancellationToken);

        var claimed = await scheduler.ClaimNextAsync(TimeSpan.FromHours(1), cancellationToken);

        if (claimed is null)
        {
            throw new InvalidOperationException("The scan just queued could not be claimed.");
        }

        await scope.ServiceProvider.GetRequiredService<IndexScanRunner>().RunAsync(claimed, cancellationToken);
    }

    /// <summary>
    /// One JSON POST from a signed-in browser, with the antiforgery token in the header the SPA puts
    /// it in.
    /// </summary>
    internal async Task<HttpStatusCode> PostJsonAsync(
        string path,
        object payload,
        string session,
        CancellationToken cancellationToken)
    {
        var (antiforgery, requestToken) = await AntiforgeryAsync(session, cancellationToken);

        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = System.Net.Http.Json.JsonContent.Create(payload),
        };
        request.Headers.Add("Cookie", $"{session}; {TestSession.AntiforgeryCookieName}={antiforgery}");
        request.Headers.Add("X-CSRF-TOKEN", requestToken);

        using var response = await Client.SendAsync(request, cancellationToken);

        return response.StatusCode;
    }

    /// <summary>
    /// Runs whatever Change is queued, claimed the way the worker claims it — the real conditional
    /// update and the real lease, rather than a shortcut the product never takes.
    /// </summary>
    internal async Task<bool> RunQueuedChangeAsync(CancellationToken cancellationToken)
    {
        await using var scope = Scope();

        var claimed = await scope.ServiceProvider.GetRequiredService<ChangeQueue>()
            .ClaimNextAsync(TimeSpan.FromHours(1), cancellationToken);

        if (claimed is null)
        {
            return false;
        }

        await using var runScope = Scope();
        await runScope.ServiceProvider.GetRequiredService<ChangeRunner>().RunAsync(claimed, cancellationToken);

        return true;
    }

    /// <summary>
    /// One pass of the retention sweep, run the way the worker runs one.
    /// </summary>
    /// <returns>How many people the pass forgot; how many it found is the worker's concern.</returns>
    internal async Task<int> SweepAsync(CancellationToken cancellationToken)
    {
        await using var scope = Scope();

        var result = await scope.ServiceProvider.GetRequiredService<RetentionSweep>()
            .SweepAsync(cancellationToken);

        return result.Forgotten;
    }

    /// <summary>
    /// The first leg of the Tenant Consent round trip: where TodoWerk sends the administrator, and
    /// the state it will insist on seeing again.
    /// </summary>
    /// <param name="session">
    /// Whose session starts it, or null for the caller the card behind a refused sign-in sends —
    /// who has none, and cannot be sent to get one.
    /// </param>
    internal async Task<ConsentRedirect> StartTenantConsentAsync(
        string? session,
        CancellationToken cancellationToken,
        string? returnUrl = null)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            returnUrl is null
                ? TenantConsentFlow.StartPath
                : $"{TenantConsentFlow.StartPath}?returnUrl={Uri.EscapeDataString(returnUrl)}");

        if (!string.IsNullOrEmpty(session))
        {
            request.Headers.Add("Cookie", session);
        }

        using var response = await Client.SendAsync(request, cancellationToken);

        var state = response.Headers.Location is { } location
            ? System.Web.HttpUtility.ParseQueryString(location.Query)["state"] ?? string.Empty
            : string.Empty;

        return new ConsentRedirect(
            response.StatusCode,
            response.Headers.Location,
            TestSession.ReadSetCookie(response, TenantConsentFlow.StateCookieName),
            TestSession.ReadSetCookie(response, Web.Endpoints.OnboardingEndpoints.ReturnCookieName),
            state);
    }

    /// <summary>
    /// Both legs: start the flow, then come back to the callback the way Microsoft would — carrying
    /// the state cookie it set, and whatever <c>admin_consent</c>, <c>tenant</c> and <c>error</c>
    /// the caller wants to test with.
    /// </summary>
    internal async Task<HttpResponseMessage> CompleteTenantConsentResponseAsync(
        string? session,
        string? adminConsent,
        string? tenant,
        CancellationToken cancellationToken,
        string? error = null,
        string? returnUrl = null)
    {
        var started = await StartTenantConsentAsync(session, cancellationToken, returnUrl);

        var query = new List<string> { $"state={Uri.EscapeDataString(started.State)}" };

        if (adminConsent is not null)
        {
            query.Add($"admin_consent={Uri.EscapeDataString(adminConsent)}");
        }

        if (tenant is not null)
        {
            query.Add($"tenant={Uri.EscapeDataString(tenant)}");
        }

        if (error is not null)
        {
            query.Add($"error={Uri.EscapeDataString(error)}");
        }

        using var callback = new HttpRequestMessage(
            HttpMethod.Get,
            $"{TenantConsentFlow.CallbackPath}?{string.Join('&', query)}");
        // Both cookies the start set, because the browser would carry both: the state that makes
        // the callback believable, and the return cookie that says where the round trip ends.
        callback.Headers.Add(
            "Cookie",
            string.IsNullOrEmpty(started.ReturnCookie)
                ? $"{TenantConsentFlow.StateCookieName}={started.StateCookie}"
                : $"{TenantConsentFlow.StateCookieName}={started.StateCookie}; "
                    + $"{Web.Endpoints.OnboardingEndpoints.ReturnCookieName}={started.ReturnCookie}");

        return await Client.SendAsync(callback, cancellationToken);
    }

    /// <summary>Both legs, for the tests whose subject is the status rather than where it went.</summary>
    internal async Task<HttpStatusCode> CompleteTenantConsentAsync(
        string? session,
        string? adminConsent,
        string? tenant,
        CancellationToken cancellationToken,
        string? error = null)
    {
        using var response = await CompleteTenantConsentResponseAsync(
            session,
            adminConsent,
            tenant,
            cancellationToken,
            error);

        return response.StatusCode;
    }

    /// <summary>Where an administrator was sent, and what has to come back with them.</summary>
    internal sealed record ConsentRedirect(
        HttpStatusCode StatusCode,
        Uri? Location,
        string StateCookie,
        string ReturnCookie,
        string State);

    public async ValueTask DisposeAsync()
    {
        _client?.Dispose();

        await Factory.DisposeAsync();

        // After the factory, which is what stops the application reaching for it mid-disposal.
        _portal?.Dispose();

        try
        {
            Directory.Delete(_keyRingPath, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A straggling handle on a key file is not worth failing the test run over.
        }
    }

    private async Task<(string Cookie, string RequestToken)> AntiforgeryAsync(
        string session,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add("Cookie", session);

        using var response = await Client.SendAsync(request, cancellationToken);

        return (
            TestSession.ReadSetCookie(response, TestSession.AntiforgeryCookieName),
            Uri.UnescapeDataString(TestSession.ReadSetCookie(response, TestSession.RequestTokenCookieName)));
    }

    private async Task<ApiResult<T>> SendAsync<T>(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        using var response = await Client.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            return new ApiResult<T>(response.StatusCode, JsonSerializer.Deserialize<T>(body, Json), null, null);
        }

        if (!(response.Content.Headers.ContentType?.MediaType?.Contains("json", StringComparison.Ordinal) ?? false))
        {
            return new ApiResult<T>(response.StatusCode, default, null, null);
        }

        using var problem = JsonDocument.Parse(body);

        return new ApiResult<T>(
            response.StatusCode,
            default,
            problem.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null,
            problem.RootElement.TryGetProperty("detail", out var detail) ? detail.GetString() : null);
    }
}
