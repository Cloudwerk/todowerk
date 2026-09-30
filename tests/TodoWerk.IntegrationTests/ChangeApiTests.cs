using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TodoWerk.Domain.Changes;
using TodoWerk.Domain.Hashtags;
using TodoWerk.Domain.Indexing;
using TodoWerk.Infrastructure.Persistence;
using Xunit;

namespace TodoWerk.IntegrationTests;

/// <summary>
/// What a user actually confirms, over the real pipeline: a preview of specific tasks, the
/// refusals each with its own reason, and the second confirmation a Merge asks for.
/// <para>
/// The background workers are parked. These tests are about what the API says before anything is
/// written; what happens afterwards is <see cref="ChangeRunTests"/>.
/// </para>
/// </summary>
public sealed class ChangeApiTests(SqlServerDatabaseFixture database) : IClassFixture<SqlServerDatabaseFixture>
{
    private const string ListId = "list-arbeit";

    private const string HalfScannedListId = "list-halb";

    [Fact]
    public async Task ThePreview_IsTheExactPairsAndNamesTheListsItLeftOut()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tenant = new FakeTodoTenant();
        tenant.AddList(ListId, "Arbeit");
        tenant.AddTask(ListId, "t1", "Angebot #Prio1 schreiben");
        tenant.AddTask(ListId, "t2", "Ohne Tag");

        await using var host = await ChangeTestHost.StartAsync(database.ConnectionString, tenant, cancellationToken);
        await host.ScanAsync(cancellationToken);

        // A list the scan has never finished. ADR-0003 forbids writing against one, so it
        // contributes no rows — and the preview has to say so rather than be silently short.
        await SeedHalfScannedListAsync(host, cancellationToken);

        var preview = await PreviewAsync(host, [Key("Prio1")], "Priority1", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        Assert.Equal("Rename", preview.Body!.Kind);
        Assert.Equal(1, preview.Body.TaskCount);
        Assert.False(preview.Body.RequiresMergeConfirmation);

        var item = Assert.Single(preview.Body.Items);
        Assert.Equal("Angebot #Prio1 schreiben", item.CurrentTitle);
        Assert.Equal("Angebot #Priority1 schreiben", item.NewTitle);
        Assert.Equal("Arbeit", item.ListDisplayName);

        var excluded = Assert.Single(preview.Body.ExcludedLists);
        Assert.Equal("Halb gescannt", excluded.DisplayName);
        Assert.False(string.IsNullOrWhiteSpace(excluded.Reason));
    }

    /// <summary>
    /// A target that does not round-trip through the extractor is refused: it would write a title
    /// the To Do clients no longer highlight, or one the index reads as a different tag (ADR-0006).
    /// </summary>
    [Theory]
    [InlineData("kunde nord")]
    [InlineData("#kunde")]
    [InlineData("")]
    public async Task ATargetThatDoesNotRoundTrip_IsRefusedWithItsOwnReason(string target)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tenant = new FakeTodoTenant();
        tenant.AddList(ListId, "Arbeit");
        tenant.AddTask(ListId, "t1", "Angebot #Prio1");

        await using var host = await ChangeTestHost.StartAsync(database.ConnectionString, tenant, cancellationToken);
        await host.ScanAsync(cancellationToken);

        var preview = await PreviewAsync(host, [Key("Prio1")], target, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, preview.StatusCode);
        Assert.Equal("Changes.InvalidTarget", preview.Code);
    }

    /// <summary>
    /// Renaming onto a Hashtag that already exists folds two into one. That is a Merge however the
    /// user reached it, and it is the only operation that asks twice.
    /// </summary>
    [Fact]
    public async Task ARenameOntoAnExistingHashtag_IsAMergeAndAsksTwice()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tenant = new FakeTodoTenant();
        tenant.AddList(ListId, "Arbeit");
        tenant.AddTask(ListId, "t1", "Angebot #kunde");
        tenant.AddTask(ListId, "t2", "Rechnung #customer");

        await using var host = await ChangeTestHost.StartAsync(database.ConnectionString, tenant, cancellationToken);
        await host.ScanAsync(cancellationToken);

        var preview = await PreviewAsync(host, [Key("kunde")], "customer", cancellationToken);

        Assert.Equal("Merge", preview.Body!.Kind);
        Assert.True(preview.Body.RequiresMergeConfirmation);

        var refused = await ConfirmAsync(host, [Key("kunde")], "customer", confirmMerge: false, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal("Changes.MergeNotConfirmed", refused.Code);

        var accepted = await ConfirmAsync(host, [Key("kunde")], "customer", confirmMerge: true, cancellationToken);

        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        Assert.Equal("Pending", accepted.Body!.State);
        Assert.Equal(1, accepted.Body.PlannedTaskCount);
    }

    /// <summary>
    /// One Change per user at a time. The second is refused with a reason, rather than queued to
    /// run against a plan the first is about to invalidate.
    /// </summary>
    [Fact]
    public async Task ASecondChangeWhileOneIsPending_IsRefused()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tenant = new FakeTodoTenant();
        tenant.AddList(ListId, "Arbeit");
        tenant.AddTask(ListId, "t1", "Angebot #Prio1");
        tenant.AddTask(ListId, "t2", "Rechnung #Prio2");

        await using var host = await ChangeTestHost.StartAsync(database.ConnectionString, tenant, cancellationToken);
        await host.ScanAsync(cancellationToken);

        var first = await ConfirmAsync(host, [Key("Prio1")], "Priority1", confirmMerge: false, cancellationToken);
        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);

        var second = await ConfirmAsync(host, [Key("Prio2")], "Priority2", confirmMerge: false, cancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("Changes.AlreadyInFlight", second.Code);
    }

    /// <summary>
    /// Nothing to do is refused rather than queued: a Change that writes no task is a run somebody
    /// would watch finish having achieved nothing, and an undo entry for it would be empty.
    /// </summary>
    [Fact]
    public async Task AChangeThatWouldWriteNothing_IsRefused()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tenant = new FakeTodoTenant();
        tenant.AddList(ListId, "Arbeit");
        tenant.AddTask(ListId, "t1", "Angebot #Priority1");

        await using var host = await ChangeTestHost.StartAsync(database.ConnectionString, tenant, cancellationToken);
        await host.ScanAsync(cancellationToken);

        // Already spelled the way the Change asks for, so every task would be rewritten to itself.
        var preview = await PreviewAsync(host, [Key("Priority1")], "Priority1", cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, preview.StatusCode);
        Assert.Equal("Changes.NothingToChange", preview.Code);
    }

    /// <summary>
    /// The ceiling exists because a Change holds the user's exclusivity for its whole run. Past it
    /// the Change is refused with the count, never chunked into Changes nobody confirmed.
    /// </summary>
    [Fact]
    public async Task APlanPastTheCeiling_IsRefusedWithTheCount()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tenant = new FakeTodoTenant();
        tenant.AddList(ListId, "Arbeit");

        for (var number = 0; number < 4; number++)
        {
            tenant.AddTask(ListId, $"t{number}", $"Aufgabe {number} #Prio1");
        }

        await using var host = await ChangeTestHost.StartAsync(
            database.ConnectionString,
            tenant,
            cancellationToken,
            maxTasksPerChange: 3);
        await host.ScanAsync(cancellationToken);

        var preview = await PreviewAsync(host, [Key("Prio1")], "Priority1", cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, preview.StatusCode);
        Assert.Equal("Changes.PlanTooLarge", preview.Code);
        Assert.Contains("4 tasks", preview.Detail, StringComparison.Ordinal);
    }

    /// <summary>
    /// The source keys arrive from the browser, so their length is the caller's to choose and the
    /// column that stores them is bounded. A key longer than a Hashtag name can be matches nothing
    /// in the index by construction, and must come back as a refusal rather than as a failed
    /// insert five layers down.
    /// </summary>
    [Fact]
    public async Task ASourceKeyLongerThanAHashtagCanBe_IsRefusedRatherThanStored()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tenant = new FakeTodoTenant();
        tenant.AddList(ListId, "Arbeit");
        tenant.AddTask(ListId, "t1", "Angebot #Prio1");

        await using var host = await ChangeTestHost.StartAsync(database.ConnectionString, tenant, cancellationToken);
        await host.ScanAsync(cancellationToken);

        var confirmed = await ConfirmAsync(
            host,
            [new string('A', 4000), Key("Prio1")],
            "Priority1",
            confirmMerge: true,
            cancellationToken);

        // The over-long key is dropped, so what is left is an ordinary one-source rename — which
        // needs no merge confirmation and is accepted on its own terms.
        Assert.Equal(HttpStatusCode.Accepted, confirmed.StatusCode);

        await using var scope = host.Scope();
        var stored = await scope.ServiceProvider.GetRequiredService<TodoWerkDbContext>()
            .Set<Change>()
            .AsNoTracking()
            .SingleAsync(cancellationToken);

        Assert.Equal([Key("Prio1")], stored.SourceKeys);
    }

    /// <summary>
    /// Every mutating endpoint sits behind the antiforgery gate. Confirming a bulk rewrite of
    /// somebody's tasks from another origin is the request this exists to stop.
    /// </summary>
    [Fact]
    public async Task ConfirmingWithoutAnAntiforgeryToken_IsRefused()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tenant = new FakeTodoTenant();
        tenant.AddList(ListId, "Arbeit");
        tenant.AddTask(ListId, "t1", "Angebot #Prio1");

        await using var host = await ChangeTestHost.StartAsync(database.ConnectionString, tenant, cancellationToken);

        // Session cookie but no antiforgery pair, which is exactly what a cross-site form has.
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/changes")
        {
            Content = JsonContent.Create(new
            {
                sourceKeys = new[] { Key("Prio1") },
                targetSpelling = "Priority1",
                confirmMerge = false,
            }),
        };
        request.Headers.Add("Cookie", $"{TestSession.SessionCookieName}={TestSession.ProtectTicket(host.Factory)}");

        using var response = await host.Client.SendAsync(request, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task TheChangeQueue_IsEmptyBeforeAnythingIsConfirmed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tenant = new FakeTodoTenant();
        tenant.AddList(ListId, "Arbeit");

        await using var host = await ChangeTestHost.StartAsync(database.ConnectionString, tenant, cancellationToken);

        var queue = await host.GetAsync<ChangeQueueResponse>("/api/changes", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, queue.StatusCode);
        Assert.Null(queue.Body!.Active);
        Assert.Empty(queue.Body.History);
    }

    private static string Key(string spelling) => HashtagKey.Fold(spelling);

    private static Task<ApiResult<ChangePreviewResponse>> PreviewAsync(
        ChangeTestHost host,
        IReadOnlyList<string> sourceKeys,
        string target,
        CancellationToken cancellationToken) =>
        host.PostAsync<ChangePreviewResponse>(
            "/api/changes/preview",
            new { sourceKeys, targetSpelling = target },
            cancellationToken);

    private static Task<ApiResult<ChangeResponse>> ConfirmAsync(
        ChangeTestHost host,
        IReadOnlyList<string> sourceKeys,
        string target,
        bool confirmMerge,
        CancellationToken cancellationToken) =>
        host.PostAsync<ChangeResponse>(
            "/api/changes",
            new { sourceKeys, targetSpelling = target, confirmMerge },
            cancellationToken);

    /// <summary>
    /// A list that has been seen but never read end to end. Written directly, because making the
    /// fake mailbox fail a list halfway is a different test's subject.
    /// </summary>
    private static async Task SeedHalfScannedListAsync(ChangeTestHost host, CancellationToken cancellationToken)
    {
        await using var scope = host.Scope();
        var context = scope.ServiceProvider.GetRequiredService<TodoWerkDbContext>();

        context.Add(TaskListIndexState.Create(
            ChangeTestHost.User.TenantId,
            ChangeTestHost.User.UserId,
            HalfScannedListId,
            "Halb gescannt"));

        await context.SaveChangesAsync(cancellationToken);
    }

    private sealed record ChangePreviewResponse(
        string Kind,
        IReadOnlyList<string> SourceKeys,
        string TargetSpelling,
        IReadOnlyList<ChangePreviewItemResponse> Items,
        int TaskCount,
        bool RequiresMergeConfirmation,
        IReadOnlyList<ExcludedListResponse> ExcludedLists);

    private sealed record ChangePreviewItemResponse(
        string TaskListId,
        string ListDisplayName,
        string CurrentTitle,
        string NewTitle);

    private sealed record ExcludedListResponse(string TaskListId, string DisplayName, string Reason);

    private sealed record ChangeResponse(
        Guid Id,
        string Kind,
        string State,
        int PlannedTaskCount,
        bool CanUndo);

    private sealed record ChangeQueueResponse(ChangeResponse? Active, IReadOnlyList<ChangeResponse> History);
}
