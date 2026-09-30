using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TodoWerk.Application.Changes;
using TodoWerk.Domain.Changes;
using TodoWerk.Domain.Markers;
using TodoWerk.Infrastructure.Persistence;
using Xunit;

namespace TodoWerk.IntegrationTests;

/// <summary>
/// The fifth Change, end to end against SQL Server and a fake mailbox: the only operation in the
/// product that takes text out of somebody's task.
/// <para>
/// It shares everything the other four have — the plan table, the journal, the queue, cancel, undo
/// and the ceiling — and the tests here are about what makes it different. It finds its tasks by
/// what is written in their titles rather than by Hashtag, because a stale Marker is stale
/// precisely because the Hashtag has gone; it never adds or reorders; and it is what finally
/// empties the rows a deleted rule leaves behind (ADR-0014).
/// </para>
/// </summary>
public sealed class RemoveMarkersTests(SqlServerDatabaseFixture database) : IClassFixture<SqlServerDatabaseFixture>
{
    private const string ListId = "list-arbeit";

    private const string Bread = "🍞";

    private const string Coffee = "☕";

    /// <summary>
    /// The plain case, and the one no existing reader could find: the task carries the Marker and
    /// no longer carries the Hashtag, so nothing keyed on Occurrences would ever reach it.
    /// </summary>
    [Fact]
    public async Task AMarkerWhoseHashtagHasGone_IsTakenOut()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tenant = new FakeTodoTenant();
        tenant.AddList(ListId, "Arbeit");
        tenant.AddTask(ListId, "t1", "🍞 Brot kaufen");
        tenant.AddTask(ListId, "t2", "🍞 Mehl kaufen #bread");

        await using var host = await ChangeTestHost.StartAsync(database.ConnectionString, tenant, cancellationToken);

        await CreateRuleAsync(host, "bread", Bread, cancellationToken);
        await host.ScanAsync(cancellationToken);

        var preview = await PreviewAsync(host, marker: null, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        Assert.Equal("RemoveMarkers", preview.Body!.Kind);
        Assert.Equal(1, preview.Body.TaskCount);
        Assert.Equal("Brot kaufen", Assert.Single(preview.Body.Items).NewTitle);

        await ConfirmAsync(host, marker: null, cancellationToken);

        Assert.True(await host.RunQueuedChangeAsync(cancellationToken));

        Assert.Equal("Brot kaufen", tenant.TitleOf(ListId, "t1"));

        // And the one that still carries its Hashtag is left exactly as it was.
        Assert.Equal("🍞 Mehl kaufen #bread", tenant.TitleOf(ListId, "t2"));
        Assert.Single(tenant.WrittenTitles);
    }

    /// <summary>
    /// Removing removes. It never adds a Marker a rule asks for and the block does not hold, and
    /// never reorders what is left — both of those are an Apply, and a Remove that quietly did one
    /// would write a title nobody previewed as that.
    /// </summary>
    [Fact]
    public async Task RemovingNeitherAddsNorReorders()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tenant = new FakeTodoTenant();
        tenant.AddList(ListId, "Arbeit");

        // The block is in the wrong order and short of a Marker #coffee should have, and one of the
        // three is stale. Only the stale one may move.
        tenant.AddTask(ListId, "t1", "☕🍞 Frühstück #coffee");

        await using var host = await ChangeTestHost.StartAsync(database.ConnectionString, tenant, cancellationToken);

        await CreateRuleAsync(host, "bread", Bread, cancellationToken);
        await CreateRuleAsync(host, "coffee", Coffee, cancellationToken);
        await host.ScanAsync(cancellationToken);

        await ConfirmAsync(host, marker: null, cancellationToken);

        Assert.True(await host.RunQueuedChangeAsync(cancellationToken));

        Assert.Equal("☕ Frühstück #coffee", tenant.TitleOf(ListId, "t1"));
    }

    /// <summary>
    /// The scope: one Marker rather than one Hashtag, because the tasks being looked for are the
    /// ones whose Hashtag has gone and a Marker a deleted rule left behind has no Hashtag at all.
    /// </summary>
    [Fact]
    public async Task RemovingOneMarker_LeavesTheOtherStaleOneAlone()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tenant = new FakeTodoTenant();
        tenant.AddList(ListId, "Arbeit");
        tenant.AddTask(ListId, "t1", "🍞☕ Etwas anderes");

        await using var host = await ChangeTestHost.StartAsync(database.ConnectionString, tenant, cancellationToken);

        await CreateRuleAsync(host, "bread", Bread, cancellationToken);
        await CreateRuleAsync(host, "coffee", Coffee, cancellationToken);
        await host.ScanAsync(cancellationToken);

        await ConfirmAsync(host, Coffee, cancellationToken);

        Assert.True(await host.RunQueuedChangeAsync(cancellationToken));

        Assert.Equal("🍞 Etwas anderes", tenant.TitleOf(ListId, "t1"));
    }

    /// <summary>
    /// An emoji nobody made a rule about was never in a block, so no Remove can reach it — the
    /// candidate definition of "stale" that reads as the certain one is the one this operation is
    /// structurally unable to act on. Text TodoWerk never wrote is not TodoWerk's to take.
    /// </summary>
    [Fact]
    public async Task AnEmojiNobodyMadeARuleAbout_IsNeverTouched()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tenant = new FakeTodoTenant();
        tenant.AddList(ListId, "Arbeit");
        tenant.AddTask(ListId, "t1", "🎉 Party");
        tenant.AddTask(ListId, "t2", "🍞 🎉 Party");

        await using var host = await ChangeTestHost.StartAsync(database.ConnectionString, tenant, cancellationToken);

        await CreateRuleAsync(host, "bread", Bread, cancellationToken);
        await host.ScanAsync(cancellationToken);

        await ConfirmAsync(host, marker: null, cancellationToken);

        Assert.True(await host.RunQueuedChangeAsync(cancellationToken));

        Assert.Equal("🎉 Party", tenant.TitleOf(ListId, "t1"));
        Assert.Equal("🎉 Party", tenant.TitleOf(ListId, "t2"));
        Assert.Single(tenant.WrittenTitles);
    }

    /// <summary>
    /// A task titled with nothing but the Markers being removed. Microsoft To Do has no task
    /// without a title, and a Change is not the place to invent one — so it is named in the preview
    /// and left alone.
    /// </summary>
    [Fact]
    public async Task ATaskTitledOnlyWithItsMarkers_IsSkippedByName()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tenant = new FakeTodoTenant();
        tenant.AddList(ListId, "Arbeit");
        tenant.AddTask(ListId, "t1", "🍞");
        tenant.AddTask(ListId, "t2", "🍞 Brot kaufen");

        await using var host = await ChangeTestHost.StartAsync(database.ConnectionString, tenant, cancellationToken);

        await CreateRuleAsync(host, "bread", Bread, cancellationToken);
        await host.ScanAsync(cancellationToken);

        var preview = await PreviewAsync(host, marker: null, cancellationToken);

        Assert.Equal(1, preview.Body!.TaskCount);
        Assert.Equal(ChangeSkips.TitleWouldBeEmpty, Assert.Single(preview.Body.Skips).Reason);

        await ConfirmAsync(host, marker: null, cancellationToken);

        Assert.True(await host.RunQueuedChangeAsync(cancellationToken));

        Assert.Equal("🍞", tenant.TitleOf(ListId, "t1"));
        Assert.Equal("Brot kaufen", tenant.TitleOf(ListId, "t2"));
    }

    /// <summary>Undo is the same promise it is for every other Change: the title as it was found.</summary>
    [Fact]
    public async Task ARemoveCanBeUndone()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tenant = new FakeTodoTenant();
        tenant.AddList(ListId, "Arbeit");
        tenant.AddTask(ListId, "t1", "🍞 Brot kaufen");

        await using var host = await ChangeTestHost.StartAsync(database.ConnectionString, tenant, cancellationToken);

        await CreateRuleAsync(host, "bread", Bread, cancellationToken);
        await host.ScanAsync(cancellationToken);

        var change = await ConfirmAsync(host, marker: null, cancellationToken);

        Assert.True(await host.RunQueuedChangeAsync(cancellationToken));
        Assert.Equal("Brot kaufen", tenant.TitleOf(ListId, "t1"));

        var stored = await ReadAsync(host, change, cancellationToken);

        Assert.Equal(ChangeKind.RemoveMarkers, stored.Kind);
        Assert.Equal(ChangeState.Completed, stored.State);
        Assert.True(ChangeUndoPolicy.CanUndo(stored, DateTimeOffset.UtcNow, TimeSpan.FromDays(30)));

        var undone = await host.PostAsync<ConfirmedChange>(
            $"/api/changes/{change}/undo",
            new { },
            cancellationToken);

        Assert.Equal(HttpStatusCode.Accepted, undone.StatusCode);
        Assert.True(await host.RunQueuedChangeAsync(cancellationToken));

        Assert.Equal("🍞 Brot kaufen", tenant.TitleOf(ListId, "t1"));
    }

    /// <summary>
    /// The row a deleted rule leaves behind exists for one reason — the emoji is at the front of
    /// real titles and the block reader has to recognise it. A Remove that takes it out of all of
    /// them is what finally empties that row.
    /// </summary>
    [Fact]
    public async Task ADeletedRulesMarker_IsRemovedAndItsRowWithIt()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tenant = new FakeTodoTenant();
        tenant.AddList(ListId, "Arbeit");
        tenant.AddTask(ListId, "t1", "🍞 Brot kaufen #bread");

        await using var host = await ChangeTestHost.StartAsync(database.ConnectionString, tenant, cancellationToken);

        var bread = await CreateRuleAsync(host, "bread", Bread, cancellationToken);
        await host.ScanAsync(cancellationToken);

        await host.DeleteAsync<MarkerRuleResponse>($"/api/marker-rules/{bread.Id}", cancellationToken);

        // Deleting wrote nothing, so the emoji is still there and the row is still keeping it known.
        Assert.Equal("🍞 Brot kaufen #bread", tenant.TitleOf(ListId, "t1"));
        Assert.Equal(1, await DeletedRuleCountAsync(host, cancellationToken));

        var listed = await ListAsync(host, cancellationToken);

        Assert.Empty(listed.Rules);
        Assert.Equal(Bread, Assert.Single(listed.Abandoned).Marker);
        Assert.Equal(1, listed.Abandoned[0].StaleTaskCount);

        await ConfirmAsync(host, Bread, cancellationToken);

        Assert.True(await host.RunQueuedChangeAsync(cancellationToken));

        Assert.Equal("Brot kaufen #bread", tenant.TitleOf(ListId, "t1"));

        // Nothing carries it any more, so nothing has to go on recognising it.
        Assert.Equal(0, await DeletedRuleCountAsync(host, cancellationToken));
        Assert.Empty((await ListAsync(host, cancellationToken)).Abandoned);
    }

    /// <summary>
    /// And the other direction, which is what makes dropping the row safe at all: undo puts the
    /// emoji back into titles the product has stopped recognising it in, so the row comes back with
    /// it. Without this the next Apply would read the block as ending in front of the emoji and
    /// write a second block — the artefact those rows exist to prevent.
    /// </summary>
    [Fact]
    public async Task UndoingThatRemove_PutsTheRowBack()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tenant = new FakeTodoTenant();
        tenant.AddList(ListId, "Arbeit");
        tenant.AddTask(ListId, "t1", "🍞 Brot kaufen #bread");

        await using var host = await ChangeTestHost.StartAsync(database.ConnectionString, tenant, cancellationToken);

        var bread = await CreateRuleAsync(host, "bread", Bread, cancellationToken);
        await host.ScanAsync(cancellationToken);
        await host.DeleteAsync<MarkerRuleResponse>($"/api/marker-rules/{bread.Id}", cancellationToken);

        var change = await ConfirmAsync(host, Bread, cancellationToken);

        Assert.True(await host.RunQueuedChangeAsync(cancellationToken));
        Assert.Equal(0, await DeletedRuleCountAsync(host, cancellationToken));

        await host.PostAsync<ConfirmedChange>($"/api/changes/{change}/undo", new { }, cancellationToken);

        Assert.True(await host.RunQueuedChangeAsync(cancellationToken));

        Assert.Equal("🍞 Brot kaufen #bread", tenant.TitleOf(ListId, "t1"));
        Assert.Equal(1, await DeletedRuleCountAsync(host, cancellationToken));

        // And the emoji is known again, so a later Apply reads one block rather than writing a
        // second in front of it.
        var listed = await ListAsync(host, cancellationToken);

        Assert.Equal(Bread, Assert.Single(listed.Abandoned).Marker);
    }

    /// <summary>
    /// The row is kept when the run could not have reached every task carrying the emoji. A task
    /// titled with nothing but the Marker is passed over, so the emoji is still out there — and a
    /// row dropped over it would leave the block reader stopping in front of an emoji in a real
    /// title, which is the artefact those rows exist to prevent.
    /// </summary>
    [Fact]
    public async Task ARowIsKept_WhenATaskTheRunPassedOverStillCarriesTheMarker()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tenant = new FakeTodoTenant();
        tenant.AddList(ListId, "Arbeit");
        tenant.AddTask(ListId, "t1", "🍞 Brot kaufen");
        tenant.AddTask(ListId, "t2", "🍞");

        await using var host = await ChangeTestHost.StartAsync(database.ConnectionString, tenant, cancellationToken);

        var bread = await CreateRuleAsync(host, "bread", Bread, cancellationToken);
        await host.ScanAsync(cancellationToken);
        await host.DeleteAsync<MarkerRuleResponse>($"/api/marker-rules/{bread.Id}", cancellationToken);

        await ConfirmAsync(host, Bread, cancellationToken);

        Assert.True(await host.RunQueuedChangeAsync(cancellationToken));

        Assert.Equal("Brot kaufen", tenant.TitleOf(ListId, "t1"));
        Assert.Equal("🍞", tenant.TitleOf(ListId, "t2"));

        // The emoji is still at the front of t2, so TodoWerk goes on knowing it as a marker — and
        // goes on offering to take it off.
        Assert.Equal(1, await DeletedRuleCountAsync(host, cancellationToken));

        await host.ScanAsync(cancellationToken);

        var listed = await ListAsync(host, cancellationToken);

        Assert.Equal(Bread, Assert.Single(listed.Abandoned).Marker);
        Assert.Equal(1, listed.Abandoned[0].StaleTaskCount);
    }

    /// <summary>
    /// A rule that still stands keeps its Marker where the Hashtag is, and the stale count says how
    /// much of it is not. The count ships with the action, which is the condition ADR-0014 put on
    /// it: "a count without the action only nags".
    /// </summary>
    [Fact]
    public async Task TheStaleCount_CountsTasksThatLostTheHashtag()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tenant = new FakeTodoTenant();
        tenant.AddList(ListId, "Arbeit");
        tenant.AddTask(ListId, "t1", "🍞 Brot kaufen #bread");
        tenant.AddTask(ListId, "t2", "🍞 Etwas anderes");
        tenant.AddTask(ListId, "t3", "🍞 Noch etwas");

        await using var host = await ChangeTestHost.StartAsync(database.ConnectionString, tenant, cancellationToken);

        await CreateRuleAsync(host, "bread", Bread, cancellationToken);
        await host.ScanAsync(cancellationToken);

        var rule = Assert.Single((await ListAsync(host, cancellationToken)).Rules);

        Assert.Equal(1, rule.TaggedTaskCount);
        Assert.Equal(1, rule.MarkedTaskCount);
        Assert.Equal(2, rule.StaleTaskCount);

        await ConfirmAsync(host, marker: null, cancellationToken);

        Assert.True(await host.RunQueuedChangeAsync(cancellationToken));
        await host.ScanAsync(cancellationToken);

        var after = Assert.Single((await ListAsync(host, cancellationToken)).Rules);

        Assert.Equal(1, after.MarkedTaskCount);
        Assert.Equal(0, after.StaleTaskCount);
    }

    /// <summary>
    /// A Remove with nothing stale to take is refused rather than queued as a Change that would
    /// write nothing — the same refusal the other four give.
    /// </summary>
    [Fact]
    public async Task ARemoveWithNothingStale_IsRefused()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tenant = new FakeTodoTenant();
        tenant.AddList(ListId, "Arbeit");
        tenant.AddTask(ListId, "t1", "🍞 Brot kaufen #bread");

        await using var host = await ChangeTestHost.StartAsync(database.ConnectionString, tenant, cancellationToken);

        await CreateRuleAsync(host, "bread", Bread, cancellationToken);
        await host.ScanAsync(cancellationToken);

        var preview = await PreviewAsync(host, marker: null, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, preview.StatusCode);
        Assert.Equal("Changes.NothingToChange", preview.Code);
    }

    /// <summary>An emoji that is not one of this person's Markers is named, not silently empty.</summary>
    [Fact]
    public async Task ARemoveScopedToAnEmojiTheyDoNotHave_IsRefused()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tenant = new FakeTodoTenant();
        tenant.AddList(ListId, "Arbeit");
        tenant.AddTask(ListId, "t1", "🍞 Brot kaufen");

        await using var host = await ChangeTestHost.StartAsync(database.ConnectionString, tenant, cancellationToken);

        await CreateRuleAsync(host, "bread", Bread, cancellationToken);
        await host.ScanAsync(cancellationToken);

        var preview = await PreviewAsync(host, "🥐", cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, preview.StatusCode);
        Assert.Equal("Changes.MarkerNotFound", preview.Code);
    }

    /// <summary>Somebody with no markers at all has nothing to remove, and is told so.</summary>
    [Fact]
    public async Task ARemoveWithNoMarkersAtAll_IsRefused()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tenant = new FakeTodoTenant();
        tenant.AddList(ListId, "Arbeit");
        tenant.AddTask(ListId, "t1", "Brot kaufen #bread");

        await using var host = await ChangeTestHost.StartAsync(database.ConnectionString, tenant, cancellationToken);
        await host.ScanAsync(cancellationToken);

        var preview = await PreviewAsync(host, marker: null, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, preview.StatusCode);
        Assert.Equal("Changes.NoMarkersToRemove", preview.Code);
    }

    /// <summary>
    /// Past the ceiling a Remove is refused with the count, like any other Change — and the count
    /// is of every task whose title holds one of the person's Markers, stale or not, because
    /// whether one is stale is grammar the database cannot run. Doing one emoji at a time is the
    /// way through it, as it is for an Apply (ADR-0014).
    /// </summary>
    [Fact]
    public async Task ARemovePastTheCeiling_IsRefusedWithTheCount()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tenant = new FakeTodoTenant();
        tenant.AddList(ListId, "Arbeit");
        tenant.AddTask(ListId, "t1", "🍞 Brot kaufen");
        tenant.AddTask(ListId, "t2", "🍞 Mehl kaufen");

        await using var host = await ChangeTestHost.StartAsync(
            database.ConnectionString,
            tenant,
            cancellationToken,
            maxTasksPerChange: 1);

        await CreateRuleAsync(host, "bread", Bread, cancellationToken);
        await host.ScanAsync(cancellationToken);

        var preview = await PreviewAsync(host, marker: null, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, preview.StatusCode);
        Assert.Equal("Changes.PlanTooLarge", preview.Code);
    }

    /// <summary>
    /// One Change at a time, whichever direction it goes in (ADR-0006). A Remove is a Change like
    /// any other and takes its turn.
    /// </summary>
    [Fact]
    public async Task ARemoveIsRefused_WhileAnotherChangeIsInFlight()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tenant = new FakeTodoTenant();
        tenant.AddList(ListId, "Arbeit");
        tenant.AddTask(ListId, "t1", "🍞 Brot kaufen");

        await using var host = await ChangeTestHost.StartAsync(database.ConnectionString, tenant, cancellationToken);

        await CreateRuleAsync(host, "bread", Bread, cancellationToken);
        await host.ScanAsync(cancellationToken);

        await ConfirmAsync(host, marker: null, cancellationToken);

        var second = await PreviewAsync(host, marker: null, cancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("Changes.AlreadyInFlight", second.Code);
    }

    /// <summary>
    /// A task edited between the plan and the write is re-read and asked again. The Marker is gone
    /// already, so there is nothing to do and it is a skip rather than a failure.
    /// </summary>
    [Fact]
    public async Task AMarkerRemovedByHandBeforeTheRunReachesIt_IsSkipped()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tenant = new FakeTodoTenant();
        tenant.AddList(ListId, "Arbeit");
        tenant.AddTask(ListId, "t1", "🍞 Brot kaufen");

        await using var host = await ChangeTestHost.StartAsync(database.ConnectionString, tenant, cancellationToken);

        await CreateRuleAsync(host, "bread", Bread, cancellationToken);
        await host.ScanAsync(cancellationToken);

        var change = await ConfirmAsync(host, marker: null, cancellationToken);

        // Somebody takes it out in Microsoft To Do before the runner gets there.
        tenant.RetitleTask(ListId, "t1", "Brot kaufen");

        Assert.True(await host.RunQueuedChangeAsync(cancellationToken));

        var stored = await ReadAsync(host, change, cancellationToken);

        Assert.Equal(ChangeState.CompletedWithSkips, stored.State);
        Assert.Equal(0, stored.WrittenCount);
        Assert.Equal(1, stored.SkippedCount);
    }

    private static Task<ApiResult<ChangePreviewResponse>> PreviewAsync(
        ChangeTestHost host,
        string? marker,
        CancellationToken cancellationToken) =>
        host.PostAsync<ChangePreviewResponse>(
            "/api/changes/preview",
            new { removeMarkers = true, marker },
            cancellationToken);

    private static async Task<Guid> ConfirmAsync(
        ChangeTestHost host,
        string? marker,
        CancellationToken cancellationToken)
    {
        var result = await host.PostAsync<ConfirmedChange>(
            "/api/changes",
            new { removeMarkers = true, marker },
            cancellationToken);

        Assert.Equal(HttpStatusCode.Accepted, result.StatusCode);

        return result.Body!.Id;
    }

    private static async Task<MarkerRuleResponse> CreateRuleAsync(
        ChangeTestHost host,
        string spelling,
        string marker,
        CancellationToken cancellationToken)
    {
        var created = await host.PostAsync<MarkerRuleResponse>(
            "/api/marker-rules",
            new { spelling, marker },
            cancellationToken);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        return created.Body!;
    }

    private static async Task<MarkerRuleListResponse> ListAsync(
        ChangeTestHost host,
        CancellationToken cancellationToken)
    {
        var listed = await host.GetAsync<MarkerRuleListResponse>("/api/marker-rules", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, listed.StatusCode);

        return listed.Body!;
    }

    /// <summary>
    /// How many rows are being kept only to keep an emoji known. Read from the database rather than
    /// from an endpoint, because nothing lists a deleted rule — which is the whole reason nothing
    /// emptied these until now.
    /// </summary>
    private static async Task<int> DeletedRuleCountAsync(ChangeTestHost host, CancellationToken cancellationToken)
    {
        await using var scope = host.Scope();

        return await scope.ServiceProvider.GetRequiredService<TodoWerkDbContext>()
            .Set<MarkerRule>()
            .CountAsync(rule => rule.DeletedAt != null, cancellationToken);
    }

    private static async Task<ChangeRecord> ReadAsync(
        ChangeTestHost host,
        Guid changeId,
        CancellationToken cancellationToken)
    {
        await using var scope = host.Scope();

        var record = await scope.ServiceProvider.GetRequiredService<IChangeStore>()
            .FindAsync(ChangeTestHost.User, changeId, cancellationToken);

        Assert.NotNull(record);

        return record;
    }

    private sealed record ChangePreviewResponse(
        string Kind,
        IReadOnlyList<string> SourceKeys,
        string TargetSpelling,
        IReadOnlyList<ChangePreviewItemResponse> Items,
        int TaskCount,
        bool RequiresMergeConfirmation,
        IReadOnlyList<ExcludedListResponse> ExcludedLists,
        IReadOnlyList<ChangePreviewSkipResponse> Skips);

    private sealed record ChangePreviewItemResponse(
        string TaskListId,
        string ListDisplayName,
        string CurrentTitle,
        string NewTitle);

    private sealed record ChangePreviewSkipResponse(
        string TaskListId,
        string ListDisplayName,
        string CurrentTitle,
        string Reason);

    private sealed record ExcludedListResponse(string TaskListId, string DisplayName, string Reason);

    private sealed record MarkerRuleResponse(
        Guid Id,
        string Key,
        string Spelling,
        string Marker,
        string? RetiredMarker,
        int Position,
        int TaggedTaskCount,
        int MarkedTaskCount,
        int StaleTaskCount);

    private sealed record MarkerRuleListResponse(
        IReadOnlyList<MarkerRuleResponse> Rules,
        IReadOnlyList<AbandonedMarkerResponse> Abandoned);

    private sealed record AbandonedMarkerResponse(string Marker, int StaleTaskCount);

    private sealed record ConfirmedChange(Guid Id);
}
