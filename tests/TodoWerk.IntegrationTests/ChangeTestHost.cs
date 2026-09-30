using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Identity.Client;
using Microsoft.Identity.Web.TokenCacheProviders;
using TodoWerk.Application.Abstractions.Authentication;
using TodoWerk.Domain.Changes;
using TodoWerk.Domain.Indexing;
using TodoWerk.Infrastructure.Authentication;
using TodoWerk.Infrastructure.Changes;
using TodoWerk.Infrastructure.Changes.Persistence;
using TodoWerk.Infrastructure.Indexing;
using TodoWerk.Infrastructure.Indexing.Persistence;
using TodoWerk.Domain.Markers;
using TodoWerk.Domain.Onboarding;
using TodoWerk.Infrastructure.Persistence;
using Xunit;

namespace TodoWerk.IntegrationTests;

/// <summary>
/// A booted application with a fake mailbox behind it and a refresh token already in the durable
/// cache — everything a Change needs before it can write a task, without a sign-in and without a
/// live tenant (CONTRIBUTING § Testing).
/// <para>
/// Shared by the tests that drive the change queue, so the seeding they all need is written once
/// and the tests themselves are about the product rather than about their own scaffolding.
/// </para>
/// </summary>
internal sealed class ChangeTestHost : IAsyncDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly string _keyRingPath;

    private readonly FakeManagementPortal? _portal;

    private HttpClient? _client;

    private ChangeTestHost(
        TodoWerkWebApplicationFactory factory,
        FakeTodoTenant tenant,
        string keyRingPath,
        FakeManagementPortal? portal)
    {
        Factory = factory;
        Tenant = tenant;
        _keyRingPath = keyRingPath;
        _portal = portal;
    }

    internal TodoWerkWebApplicationFactory Factory { get; }

    internal FakeTodoTenant Tenant { get; }

    /// <summary>
    /// The portal this host resolves Licences against, for the tests that ask what it was called
    /// about. Throws rather than answering null on a Self-Host: a test reading this has already
    /// decided it is a Hosted Service.
    /// </summary>
    internal FakeManagementPortal Portal => _portal
        ?? throw new InvalidOperationException("This host was started without a licensing portal.");

    internal static IndexUser User { get; } =
        new(TodoWerkWebApplicationFactory.TenantId, FakeEntraAndGraphHandler.UserObjectId);

    /// <param name="licensing">
    /// What ManagementPortal answers about one person, given the resolve call's body — or null for
    /// a Self-Host, which asks nobody and licenses everybody. Supplying it is what makes this host
    /// a Hosted Service, and the three settings that takes go in as startup settings for the
    /// reason <c>LicensingTests.HostedService</c> gives: the licensing composition reads them
    /// while services are still being registered.
    /// </param>
    /// <param name="settings">
    /// Anything else to override, applied after this host's own — a poll interval, most usefully,
    /// for a test that means to drive a worker itself.
    /// </param>
    internal static async Task<ChangeTestHost> StartAsync(
        string connectionString,
        FakeTodoTenant tenant,
        CancellationToken cancellationToken,
        bool withWorkers = false,
        int maxTasksPerChange = 1000,
        Func<string, FakeManagementPortal.PortalAnswer>? licensing = null,
        IReadOnlyDictionary<string, string?>? settings = null)
    {
        var keyRingPath = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(),
            "todowerk-change-tests",
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
            : new FakeManagementPortal(licensing, new FakeEntraAndGraphHandler(cloud, tenant));

        var factory = new TodoWerkWebApplicationFactory()
            .WithConfigurationOverride("ConnectionStrings:TodoWerk", connectionString)
            .WithConfigurationOverride("DataProtection:KeyRingPath", keyRingPath)
            .WithConfigurationOverride("Graph:MaxRetryDelay", "00:00:00")
            .WithConfigurationOverride("Changes:PollInterval", "00:00:01")
            .WithConfigurationOverride(
                "Changes:MaxTasksPerChange",
                maxTasksPerChange.ToString(System.Globalization.CultureInfo.InvariantCulture));

        if (portal is null)
        {
            factory.WithOutboundHttpHandler(() => new FakeEntraAndGraphHandler(cloud, tenant));
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

        if (!withWorkers)
        {
            factory.WithoutBackgroundWorkers();
        }

        var host = new ChangeTestHost(factory, tenant, keyRingPath, portal);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<TodoWerkDbContext>();

            await context.Database.MigrateAsync(cancellationToken);

            // One database serves the whole class and every test here works the same user, so each
            // starts from an empty index and an empty queue rather than from what the last left.
            // Changes first: the plan rows and journals cascade off them.
            await context.Set<Change>().ExecuteDeleteAsync(cancellationToken);
            await context.Set<HashtagOccurrence>().ExecuteDeleteAsync(cancellationToken);
            await context.Set<IndexedTask>().ExecuteDeleteAsync(cancellationToken);
            await context.Set<TaskListIndexState>().ExecuteDeleteAsync(cancellationToken);
            await context.Set<IndexScan>().ExecuteDeleteAsync(cancellationToken);
            await context.Set<MarkerRule>().ExecuteDeleteAsync(cancellationToken);
            // Who the scan worker keeps fresh on its own is read off this table, so a test that
            // means somebody to be present says so rather than inheriting a sign-in from the last.
            await context.Set<TenantMember>().ExecuteDeleteAsync(cancellationToken);
        }

        await SeedRefreshTokenAsync(factory, cloud, tenant, cancellationToken);

        return host;
    }

    /// <summary>
    /// Indexes the fake mailbox the way the worker would, so the tests that follow are planning
    /// over an index that was really built rather than over rows they wrote by hand.
    /// </summary>
    internal async Task ScanAsync(CancellationToken cancellationToken)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TodoWerkDbContext>();
        var scheduler = scope.ServiceProvider.GetRequiredService<IndexScanScheduler>();

        context.Add(IndexScan.Request(User.TenantId, User.UserId, IndexScanMode.Full, null, DateTimeOffset.UtcNow));
        await context.SaveChangesAsync(cancellationToken);

        var claimed = await scheduler.ClaimNextAsync(TimeSpan.FromHours(1), cancellationToken);
        Assert.NotNull(claimed);

        await scope.ServiceProvider.GetRequiredService<IndexScanRunner>().RunAsync(claimed, cancellationToken);
    }

    /// <summary>
    /// Runs whatever Change is queued, claimed the way the worker claims it — the real conditional
    /// update and the real lease, rather than a shortcut the product never takes.
    /// </summary>
    /// <returns>False when nothing could be claimed, which is itself an assertable outcome.</returns>
    internal async Task<bool> RunQueuedChangeAsync(CancellationToken cancellationToken)
    {
        var claimed = await ClaimQueuedChangeAsync(cancellationToken);

        if (claimed is null)
        {
            return false;
        }

        await RunClaimedChangeAsync(claimed, cancellationToken);

        return true;
    }

    /// <summary>
    /// Claims the queued Change without running it, the way the worker claims it. Split out so a
    /// test can run one claim twice — which is the only way to reach what a process replaced
    /// mid-run leaves behind.
    /// </summary>
    internal async Task<ClaimedChange?> ClaimQueuedChangeAsync(CancellationToken cancellationToken)
    {
        await using var scope = Factory.Services.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<ChangeQueue>()
            .ClaimNextAsync(TimeSpan.FromHours(1), cancellationToken);
    }

    /// <summary>Runs a claim, in a scope of its own as the worker does.</summary>
    internal async Task RunClaimedChangeAsync(ClaimedChange claimed, CancellationToken cancellationToken)
    {
        await using var runScope = Factory.Services.CreateAsyncScope();
        await runScope.ServiceProvider.GetRequiredService<ChangeRunner>().RunAsync(claimed, cancellationToken);
    }

    /// <summary>
    /// Puts the Change row back exactly the way a process replaced between the runner's last write
    /// and <c>ChangeQueue.FinishAsync</c> leaves it: still Running, still carrying the lease this
    /// claim holds. Running the same claim again from there is the tail being redone, which the
    /// runner does on purpose — it carries the rules before it finishes, so a crash between the two
    /// is repeated rather than lost.
    /// <para>
    /// Two rows and no production code: nothing the product exposes can produce this state, and the
    /// window is a few milliseconds wide, so the alternative to writing it is not testing it.
    /// </para>
    /// </summary>
    internal async Task ReopenClaimedChangeAsync(ClaimedChange claimed, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(claimed);

        await using var scope = Factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TodoWerkDbContext>();

        await context.Set<Change>()
            .Where(row => row.Id == claimed.Id)
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(row => row.State, ChangeState.Running)
                    .SetProperty(row => row.StartedAt, claimed.Lease),
                cancellationToken);
    }

    internal AsyncServiceScope Scope() => Factory.Services.CreateAsyncScope();

    /// <summary>
    /// One signed-in browser for the whole host. Kept rather than made per call so a test that
    /// posts three times is one client's session rather than three.
    /// </summary>
    internal HttpClient Client => _client ??=
        Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    /// <summary>
    /// One authenticated, antiforgery-carrying POST, the way the SPA makes it. Handlers are not
    /// called directly from these tests: <c>ICurrentUser</c> reads the request, so a handler
    /// resolved straight out of a scope is a handler nobody is signed in to.
    /// </summary>
    internal Task<ApiResult<T>> PostAsync<T>(string path, object payload, CancellationToken cancellationToken) =>
        SendGuardedAsync<T>(HttpMethod.Post, path, payload, cancellationToken);

    /// <summary>The same, for the endpoints that change or delete a thing that already exists.</summary>
    internal Task<ApiResult<T>> PatchAsync<T>(string path, object payload, CancellationToken cancellationToken) =>
        SendGuardedAsync<T>(HttpMethod.Patch, path, payload, cancellationToken);

    internal Task<ApiResult<T>> DeleteAsync<T>(string path, CancellationToken cancellationToken) =>
        SendGuardedAsync<T>(HttpMethod.Delete, path, payload: null, cancellationToken);

    private async Task<ApiResult<T>> SendGuardedAsync<T>(
        HttpMethod method,
        string path,
        object? payload,
        CancellationToken cancellationToken)
    {
        var session = TestSession.ProtectTicket(Factory);
        var (antiforgery, requestToken) = await TestSession.GetAntiforgeryAsync(Client, session, cancellationToken);

        using var request = new HttpRequestMessage(method, path);

        if (payload is not null)
        {
            request.Content = JsonContent.Create(payload);
        }

        request.Headers.Add(
            "Cookie",
            $"{TestSession.SessionCookieName}={session}; {TestSession.AntiforgeryCookieName}={antiforgery}");
        request.Headers.Add("X-CSRF-TOKEN", requestToken);

        return await SendAsync<T>(request, cancellationToken);
    }

    /// <summary>One authenticated safe request.</summary>
    internal async Task<ApiResult<T>> GetAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("Cookie", $"{TestSession.SessionCookieName}={TestSession.ProtectTicket(Factory)}");

        return await SendAsync<T>(request, cancellationToken);
    }

    private async Task<ApiResult<T>> SendAsync<T>(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var response = await Client.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            // A 204 carries nothing to deserialise, and a delete that answers one is a success the
            // caller reads off the status rather than off a body.
            return new ApiResult<T>(
                response.StatusCode,
                string.IsNullOrWhiteSpace(body) ? default : JsonSerializer.Deserialize<T>(body, Json),
                null,
                null);
        }

        using var problem = JsonDocument.Parse(body);

        return new ApiResult<T>(
            response.StatusCode,
            default,
            problem.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null,
            problem.RootElement.TryGetProperty("detail", out var detail) ? detail.GetString() : null);
    }

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

    /// <summary>
    /// Puts a refresh token in the durable cache the way a completed sign-in does, so a background
    /// Change can find one with no request to hang it on — which is also why ADR-0007 has to
    /// grant the write scope at sign-in.
    /// </summary>
    private static async Task SeedRefreshTokenAsync(
        TodoWerkWebApplicationFactory factory,
        EntraAndGraphRecorder cloud,
        FakeTodoTenant tenant,
        CancellationToken cancellationToken)
    {
        using var handler = new FakeEntraAndGraphHandler(cloud, tenant);
        using var httpClient = new HttpClient(handler, disposeHandler: false);

        var confidentialClient = ConfidentialClientApplicationBuilder
            .Create(TodoWerkWebApplicationFactory.ClientId)
            .WithClientSecret(TodoWerkWebApplicationFactory.ClientSecret)
            .WithAuthority(new Uri($"https://login.microsoftonline.com/{TodoWerkWebApplicationFactory.TenantId}"))
            .WithInstanceDiscovery(false)
            .WithHttpClientFactory(new SingleClientMsalHttpFactory(httpClient))
            .Build();

        factory.Services.GetRequiredService<IMsalTokenCacheProvider>()
            .Initialize(confidentialClient.UserTokenCache);

        await confidentialClient
            .AcquireTokenByAuthorizationCode([.. GraphScopes.SignIn], "fake-authorization-code")
            .ExecuteAsync(cancellationToken);
    }

    /// <summary>MSAL asks a factory for its client; the test has exactly one to give.</summary>
    private sealed class SingleClientMsalHttpFactory(HttpClient httpClient) : IMsalHttpClientFactory
    {
        public HttpClient GetHttpClient() => httpClient;
    }
}
