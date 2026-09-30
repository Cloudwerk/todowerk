using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TodoWerk.Domain.Indexing;
using TodoWerk.Infrastructure.Persistence;
using Xunit;
using TodoWerk.Domain.Hashtags;
using TodoWerk.Domain.Failures;

namespace TodoWerk.IntegrationTests;

/// <summary>
/// The freshness surface: what the Workbench asks for to draw its chrome, and the re-scan control
/// it offers. Both against the real pipeline, with the background worker parked so the assertions
/// are about the API rather than about a race with a timer.
/// </summary>
public sealed class IndexApiTests(SqlServerDatabaseFixture database) : IClassFixture<SqlServerDatabaseFixture>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Status_BeforeAnythingIsScanned_SaysSoRatherThanLookingEmpty()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var host = await StartHostAsync(cancellationToken);
        using var client = CreateClient(host);

        var status = await GetStatusAsync(client, TestSession.ProtectTicket(host), cancellationToken);

        Assert.Equal("Idle", status.Activity);
        Assert.False(status.HasCompletedFirstScan);
        Assert.Null(status.CurrentAsOf);
        Assert.Equal(0, status.ListCount);
        Assert.Empty(status.Lists);
    }

    /// <summary>
    /// Asking twice is asking once. A user who presses re-scan again because nothing visibly
    /// happened must not double the work they are waiting on.
    /// </summary>
    [Fact]
    public async Task RequestingAScanTwice_QueuesItOnce()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var host = await StartHostAsync(cancellationToken);
        using var client = CreateClient(host);
        var session = TestSession.ProtectTicket(host);

        var first = await RequestScanAsync(client, session, cancellationToken);
        var second = await RequestScanAsync(client, session, cancellationToken);

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.True(first.Queued);

        // Still Accepted: the caller asked for the index to be current, and it is on its way.
        Assert.Equal(HttpStatusCode.Accepted, second.StatusCode);
        Assert.False(second.Queued);

        await using var scope = host.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TodoWerkDbContext>();

        Assert.Equal(1, await context.Set<IndexScan>().CountAsync(cancellationToken));
    }

    [Fact]
    public async Task AQueuedScan_ShowsUpInTheStatusBeforeItRuns()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var host = await StartHostAsync(cancellationToken);
        using var client = CreateClient(host);
        var session = TestSession.ProtectTicket(host);

        await RequestScanAsync(client, session, cancellationToken);

        var status = await GetStatusAsync(client, session, cancellationToken);

        Assert.Equal("Queued", status.Activity);
    }

    /// <summary>
    /// Freshness after a scan: the index knows when it was last current, how many lists are done,
    /// and how many tasks it has read — the three numbers the chrome shows.
    /// </summary>
    [Fact]
    public async Task Status_AfterAScan_ReportsPerListFreshness()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var host = await StartHostAsync(cancellationToken);
        using var client = CreateClient(host);

        await SeedIndexedListAsync(host, cancellationToken);

        var status = await GetStatusAsync(client, TestSession.ProtectTicket(host), cancellationToken);

        Assert.Equal("Idle", status.Activity);
        Assert.True(status.HasCompletedFirstScan);
        Assert.Equal(1, status.ListCount);
        Assert.Equal(1, status.ListsIndexed);
        Assert.Equal(12, status.TasksIndexed);
        Assert.NotNull(status.CurrentAsOf);

        var list = Assert.Single(status.Lists);
        Assert.Equal("Arbeit", list.DisplayName);
        Assert.Equal("Indexed", list.State);
        Assert.Equal(12, list.TasksIndexed);
        Assert.Null(list.FailureReason);
    }

    /// <summary>
    /// One stale list makes the whole index stale: freshness is the oldest list's, never the
    /// newest, or the number on screen flatters what the user is actually looking at.
    /// </summary>
    [Fact]
    public async Task Status_ReportsTheOldestListsFreshness_NotTheNewest()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var host = await StartHostAsync(cancellationToken);
        using var client = CreateClient(host);

        var stale = DateTimeOffset.UtcNow.AddHours(-6);
        await SeedIndexedListAsync(host, cancellationToken, syncedAt: stale, listId: "list-alt", name: "Alt");
        await SeedIndexedListAsync(host, cancellationToken);

        var status = await GetStatusAsync(client, TestSession.ProtectTicket(host), cancellationToken);

        Assert.Equal(2, status.ListCount);
        Assert.NotNull(status.CurrentAsOf);
        Assert.Equal(stale, status.CurrentAsOf.Value, TimeSpan.FromSeconds(1));
    }

    /// <summary>
    /// A pass writes "Scanning" before it starts and the state that replaces it when it ends, so
    /// a process that died mid-pass leaves the row saying Scanning forever. With nothing running,
    /// the honest answer is what the list was before that pass — not a spinner nobody is behind.
    /// </summary>
    [Fact]
    public async Task AListLeftScanningByADeadProcess_IsNotReportedAsStillScanning()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var host = await StartHostAsync(cancellationToken);
        using var client = CreateClient(host);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<TodoWerkDbContext>();

            var interrupted = TaskListIndexState.Create(
                TodoWerkWebApplicationFactory.TenantId,
                FakeEntraAndGraphHandler.UserObjectId,
                "list-abgebrochen",
                "Abgebrochen");
            interrupted.BeginScan(DateTimeOffset.UtcNow.AddMinutes(-90));

            context.Add(interrupted);
            await context.SaveChangesAsync(cancellationToken);
        }

        var status = await GetStatusAsync(client, TestSession.ProtectTicket(host), cancellationToken);

        Assert.Equal("Idle", status.Activity);
        Assert.Equal("NeverScanned", Assert.Single(status.Lists).State);
    }

    /// <summary>
    /// A scan can fail before it reaches a single list — no token, no list of lists — and there
    /// are no per-list rows to carry that failure. Without it in the status the screen reports an
    /// account that has simply never been indexed, which is what somebody watching a scan they
    /// asked for do nothing would be told.
    /// </summary>
    [Fact]
    public async Task AScanThatFailedBeforeAnyList_SaysSoInTheStatus()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var host = await StartHostAsync(cancellationToken);
        using var client = CreateClient(host);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<TodoWerkDbContext>();

            var scan = IndexScan.Request(
                TodoWerkWebApplicationFactory.TenantId,
                FakeEntraAndGraphHandler.UserObjectId,
                IndexScanMode.Delta,
                taskListId: null,
                DateTimeOffset.UtcNow);
            scan.Fail(
                "The connection to Microsoft To Do has expired. Sign in again to reconnect.",
                FailureCode.ReconnectRequired,
                DateTimeOffset.UtcNow);

            context.Add(scan);
            await context.SaveChangesAsync(cancellationToken);
        }

        var status = await GetStatusAsync(client, TestSession.ProtectTicket(host), cancellationToken);

        Assert.Equal("Idle", status.Activity);
        Assert.Equal(0, status.ListCount);
        Assert.Contains("Sign in again", status.LastScanFailure ?? string.Empty, StringComparison.Ordinal);
    }

    /// <summary>And a scan that succeeded afterwards makes that failure history.</summary>
    [Fact]
    public async Task ASuccessfulScanAfterAFailure_ClearsTheReportedFailure()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var host = await StartHostAsync(cancellationToken);
        using var client = CreateClient(host);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<TodoWerkDbContext>();

            var failed = IndexScan.Request(
                TodoWerkWebApplicationFactory.TenantId,
                FakeEntraAndGraphHandler.UserObjectId,
                IndexScanMode.Delta,
                taskListId: null,
                DateTimeOffset.UtcNow.AddMinutes(-5));
            failed.Fail("Something went wrong.", FailureCode.Unknown, DateTimeOffset.UtcNow.AddMinutes(-5));

            var succeeded = IndexScan.Request(
                TodoWerkWebApplicationFactory.TenantId,
                FakeEntraAndGraphHandler.UserObjectId,
                IndexScanMode.Delta,
                taskListId: null,
                DateTimeOffset.UtcNow);
            succeeded.Complete(DateTimeOffset.UtcNow);

            context.AddRange(failed, succeeded);
            await context.SaveChangesAsync(cancellationToken);
        }

        var status = await GetStatusAsync(client, TestSession.ProtectTicket(host), cancellationToken);

        Assert.Null(status.LastScanFailure);
    }

    [Theory]
    [InlineData("/api/index/status")]
    [InlineData("/api/index/scan")]
    public async Task TheIndexEndpoints_RefuseAnAnonymousCaller(string path)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var host = await StartHostAsync(cancellationToken);
        using var client = CreateClient(host);

        using var request = new HttpRequestMessage(
            path.EndsWith("scan", StringComparison.Ordinal) ? HttpMethod.Post : HttpMethod.Get,
            path);

        using var response = await client.SendAsync(request, cancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// Starting minutes of background work against someone's mailbox is a state change, so it
    /// carries the same antiforgery requirement as signing out.
    /// </summary>
    [Fact]
    public async Task RequestingAScanWithoutAnAntiforgeryToken_IsRefused()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var host = await StartHostAsync(cancellationToken);
        using var client = CreateClient(host);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/index/scan")
        {
            Content = JsonContent.Create(new { taskListId = (string?)null, fullRescan = false }),
        };
        request.Headers.Add("Cookie", $"{TestSession.SessionCookieName}={TestSession.ProtectTicket(host)}");

        using var response = await client.SendAsync(request, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// A page number nobody can reach by clicking must still be an empty page: unbounded, the
    /// offset arithmetic wraps negative and SQL Server answers with an error instead.
    /// </summary>
    [Fact]
    public async Task AnAbsurdPageNumber_IsAnEmptyPageNotAnError()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var host = await StartHostAsync(cancellationToken);
        using var client = CreateClient(host);

        await SeedHashtagAsync(host, FakeEntraAndGraphHandler.UserObjectId, "PROJEKT", "Projekt", cancellationToken);

        var page = await GetInventoryAsync(
            client,
            TestSession.ProtectTicket(host),
            "/api/hashtags?page=20000000&pageSize=200",
            cancellationToken);

        Assert.Empty(page.Rows);
        Assert.Equal(1, page.TotalCount);
    }

    [Fact]
    public async Task AnOverlongSearch_FindsNothingRatherThanFailing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var host = await StartHostAsync(cancellationToken);
        using var client = CreateClient(host);

        await SeedHashtagAsync(host, FakeEntraAndGraphHandler.UserObjectId, "PROJEKT", "Projekt", cancellationToken);

        var page = await GetInventoryAsync(
            client,
            TestSession.ProtectTicket(host),
            $"/api/hashtags?search={new string('a', 300)}",
            cancellationToken);

        Assert.Empty(page.Rows);
        Assert.Equal(0, page.TotalCount);
    }

    /// <summary>
    /// The one identifier a caller may send. Longer than any Graph id can be means it is not an
    /// id, and the answer is a validation error — not a SQL exception from the insert it will
    /// not fit into.
    /// </summary>
    [Fact]
    public async Task AScanForAnImpossibleListId_IsRejectedAsInvalid()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var host = await StartHostAsync(cancellationToken);
        using var client = CreateClient(host);
        var session = TestSession.ProtectTicket(host);

        var (antiforgeryCookie, requestToken) =
            await TestSession.GetAntiforgeryAsync(client, session, cancellationToken);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/index/scan")
        {
            Content = JsonContent.Create(new { taskListId = new string('x', 600), fullRescan = true }),
        };
        request.Headers.Add(
            "Cookie",
            $"{TestSession.SessionCookieName}={session}; {TestSession.AntiforgeryCookieName}={antiforgeryCookie}");
        request.Headers.Add("X-CSRF-TOKEN", requestToken);

        using var response = await client.SendAsync(request, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// The negative half of tenant isolation, which no other test states: another user's rows in
    /// the same tables, and an inventory that must not contain a trace of them.
    /// </summary>
    [Fact]
    public async Task TheInventory_NeverShowsAnotherUsersHashtags()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var host = await StartHostAsync(cancellationToken);
        using var client = CreateClient(host);

        await SeedHashtagAsync(host, FakeEntraAndGraphHandler.UserObjectId, "EIGEN", "Eigen", cancellationToken);
        await SeedHashtagAsync(host, "00000000-0000-0000-0000-00000000feed", "FREMD", "Fremd", cancellationToken);

        var page = await GetInventoryAsync(
            client,
            TestSession.ProtectTicket(host),
            "/api/hashtags",
            cancellationToken);

        var row = Assert.Single(page.Rows);
        Assert.Equal("EIGEN", row.Key);
    }

    private static async Task SeedHashtagAsync(
        TodoWerkWebApplicationFactory host,
        string userId,
        string key,
        string spelling,
        CancellationToken cancellationToken)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TodoWerkDbContext>();

        var task = IndexedTask.Create(
            TodoWerkWebApplicationFactory.TenantId,
            userId,
            "list-arbeit",
            $"task-{userId}-{key}",
            $"Aufgabe #{spelling}",
            DateTimeOffset.UtcNow);

        context.Add(task);
        context.Add(HashtagOccurrence.Create(
            TodoWerkWebApplicationFactory.TenantId,
            userId,
            task.Id,
            new ExtractedHashtag(key, spelling)));

        await context.SaveChangesAsync(cancellationToken);
    }

    private static async Task<InventoryPageResponse> GetInventoryAsync(
        HttpClient client,
        string session,
        string path,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("Cookie", $"{TestSession.SessionCookieName}={session}");

        using var response = await client.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        Assert.True(response.IsSuccessStatusCode, $"inventory read failed: {(int)response.StatusCode} {payload}");

        return JsonSerializer.Deserialize<InventoryPageResponse>(payload, Json)!;
    }

    private static HttpClient CreateClient(TodoWerkWebApplicationFactory host) =>
        host.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
        });

    private static async Task<IndexStatusResponse> GetStatusAsync(
        HttpClient client,
        string session,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/index/status");
        request.Headers.Add("Cookie", $"{TestSession.SessionCookieName}={session}");

        using var response = await client.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        Assert.True(response.IsSuccessStatusCode, $"status read failed: {(int)response.StatusCode} {payload}");

        return JsonSerializer.Deserialize<IndexStatusResponse>(payload, Json)!;
    }

    private static async Task<(HttpStatusCode StatusCode, bool Queued)> RequestScanAsync(
        HttpClient client,
        string session,
        CancellationToken cancellationToken)
    {
        var (antiforgeryCookie, requestToken) =
            await TestSession.GetAntiforgeryAsync(client, session, cancellationToken);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/index/scan")
        {
            Content = JsonContent.Create(new { taskListId = (string?)null, fullRescan = false }),
        };
        request.Headers.Add(
            "Cookie",
            $"{TestSession.SessionCookieName}={session}; {TestSession.AntiforgeryCookieName}={antiforgeryCookie}");
        request.Headers.Add("X-CSRF-TOKEN", requestToken);

        using var response = await client.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        Assert.True(response.IsSuccessStatusCode, $"scan request failed: {(int)response.StatusCode} {payload}");

        return (response.StatusCode, JsonSerializer.Deserialize<ScanRequestedResponse>(payload, Json)!.Queued);
    }

    /// <summary>
    /// A list the scan already finished, written straight to the index — including its tasks,
    /// because the status counts what is actually indexed rather than trusting the progress
    /// field a delta pass would have overwritten.
    /// </summary>
    private static async Task SeedIndexedListAsync(
        TodoWerkWebApplicationFactory host,
        CancellationToken cancellationToken,
        DateTimeOffset? syncedAt = null,
        string listId = "list-arbeit",
        string name = "Arbeit")
    {
        await using var scope = host.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TodoWerkDbContext>();

        var state = TaskListIndexState.Create(
            TodoWerkWebApplicationFactory.TenantId,
            FakeEntraAndGraphHandler.UserObjectId,
            listId,
            name);

        state.BeginScan(syncedAt ?? DateTimeOffset.UtcNow);
        state.RecordProgress(12);
        state.CompleteFullScan("delta-token", syncedAt ?? DateTimeOffset.UtcNow);

        context.Add(state);

        for (var number = 0; number < 12; number++)
        {
            context.Add(IndexedTask.Create(
                TodoWerkWebApplicationFactory.TenantId,
                FakeEntraAndGraphHandler.UserObjectId,
                listId,
                $"{listId}-task-{number}",
                $"Aufgabe {number}",
                syncedAt ?? DateTimeOffset.UtcNow));
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    private async Task<TodoWerkWebApplicationFactory> StartHostAsync(CancellationToken cancellationToken)
    {
        var host = new TodoWerkWebApplicationFactory()
            .WithConfigurationOverride("ConnectionStrings:TodoWerk", database.ConnectionString)
            // Left out: these tests are about what the API says, and a worker draining the queue
            // underneath them would turn "Queued" into a race. Parking it with a long poll
            // interval does not work — it ticks once at startup before it ever waits.
            .WithoutBackgroundWorkers()
            .WithOutboundHttpHandler(() => new FakeEntraAndGraphHandler(new EntraAndGraphRecorder()));

        await using var scope = host.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TodoWerkDbContext>();

        await context.Database.MigrateAsync(cancellationToken);

        await context.Set<TaskListIndexState>().ExecuteDeleteAsync(cancellationToken);
        await context.Set<IndexScan>().ExecuteDeleteAsync(cancellationToken);
        await context.Set<IndexedTask>().ExecuteDeleteAsync(cancellationToken);

        return host;
    }

    private sealed record IndexStatusResponse(
        string Activity,
        DateTimeOffset? CurrentAsOf,
        bool HasCompletedFirstScan,
        int ListCount,
        int ListsIndexed,
        int TasksIndexed,
        IReadOnlyList<TaskListStatusResponse> Lists,
        string? LastScanFailure);

    private sealed record TaskListStatusResponse(
        string TaskListId,
        string DisplayName,
        string State,
        int TasksIndexed,
        DateTimeOffset? LastSuccessfulSyncAt,
        string? FailureReason);

    private sealed record ScanRequestedResponse(bool Queued);

    private sealed record InventoryPageResponse(IReadOnlyList<InventoryRowResponse> Rows, int TotalCount);

    private sealed record InventoryRowResponse(string Key, string CanonicalSpelling);
}
