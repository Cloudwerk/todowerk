using System.Net;
using Microsoft.Extensions.DependencyInjection;
using TodoWerk.Application.Changes;
using TodoWerk.Domain.Changes;
using TodoWerk.Domain.Hashtags;
using Xunit;

namespace TodoWerk.IntegrationTests;

/// <summary>
/// The fourth Change, end to end against SQL Server and a fake mailbox: previewed, confirmed, run,
/// skipped where it should be, and taken back.
/// <para>
/// It shares everything ADR-0006 built for the other three — the plan table, the journal, the
/// queue, cancel, undo and the ceiling — and the tests here are about what makes it different: it
/// writes outside a Hashtag, it carries its own Markers rather than reading the live rules, and it
/// puts the same block on a task whether it was started from one rule or from all of them.
/// </para>
/// </summary>
public sealed class ApplyMarkersTests(SqlServerDatabaseFixture database) : IClassFixture<SqlServerDatabaseFixture>
{
    private const string ListId = "list-arbeit";

    private const string Bread = "🍞";

    private const string Coffee = "☕";

    private const string Croissant = "🥐";

    [Fact]
    public async Task ThePreview_ShowsTheBlockEachTaskWouldGain()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tenant = new FakeTodoTenant();
        tenant.AddList(ListId, "Arbeit");
        tenant.AddTask(ListId, "t1", "Brot kaufen #bread");
        tenant.AddTask(ListId, "t2", "Kaffee mahlen #coffee");
        tenant.AddTask(ListId, "t3", "Ohne Tag");

        await using var host = await ChangeTestHost.StartAsync(database.ConnectionString, tenant, cancellationToken);

        await CreateRuleAsync(host, "bread", Bread, cancellationToken);
        await CreateRuleAsync(host, "coffee", Coffee, cancellationToken);
        await host.ScanAsync(cancellationToken);

        var preview = await PreviewAsync(host, markerRuleKey: null, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        Assert.Equal("ApplyMarkers", preview.Body!.Kind);
        Assert.Equal(2, preview.Body.TaskCount);
        Assert.False(preview.Body.RequiresMergeConfirmation);
        Assert.Empty(preview.Body.Skips);

        Assert.Contains(preview.Body.Items, item => item.NewTitle == "🍞 Brot kaufen #bread");
        Assert.Contains(preview.Body.Items, item => item.NewTitle == "☕ Kaffee mahlen #coffee");
    }

    /// <summary>
    /// Scope and write are different things. Applying one rule covers only that Hashtag's tasks,
    /// and writes the whole block into each of them — so the task that also carries #coffee comes
    /// out in rule order rather than with ☕ shoved behind ​🍞 by accident (ADR-0014).
    /// </summary>
    [Fact]
    public async Task ApplyingOneRule_TouchesOnlyItsTasksAndStillWritesTheWholeBlock()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tenant = new FakeTodoTenant();
        tenant.AddList(ListId, "Arbeit");
        tenant.AddTask(ListId, "t1", "Frühstück #coffee #bread");
        tenant.AddTask(ListId, "t2", "Nur Kaffee #coffee");

        await using var host = await ChangeTestHost.StartAsync(database.ConnectionString, tenant, cancellationToken);

        await CreateRuleAsync(host, "bread", Bread, cancellationToken);
        await CreateRuleAsync(host, "coffee", Coffee, cancellationToken);
        await host.ScanAsync(cancellationToken);

        await ConfirmAsync(host, HashtagKey.Fold("bread"), cancellationToken);

        Assert.True(await host.RunQueuedChangeAsync(cancellationToken));

        // Both Markers, in rule order, on the task the scoped Hashtag selected.
        Assert.Equal("🍞☕ Frühstück #coffee #bread", tenant.TitleOf(ListId, "t1"));

        // And nothing at all on the one that carries only the rule that was not in scope.
        Assert.Equal("Nur Kaffee #coffee", tenant.TitleOf(ListId, "t2"));
        Assert.Single(tenant.WrittenTitles);
    }

    [Fact]
    public async Task ApplyingAllRules_MarksEveryTaggedTaskAndLeavesTheRestAlone()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tenant = new FakeTodoTenant();
        tenant.AddList(ListId, "Arbeit");
        tenant.AddTask(ListId, "t1", "Brot kaufen #bread");
        tenant.AddTask(ListId, "t2", "Ohne Tag");

        await using var host = await ChangeTestHost.StartAsync(database.ConnectionString, tenant, cancellationToken);

        await CreateRuleAsync(host, "bread", Bread, cancellationToken);
        await host.ScanAsync(cancellationToken);

        var change = await ConfirmAsync(host, markerRuleKey: null, cancellationToken);

        Assert.True(await host.RunQueuedChangeAsync(cancellationToken));

        Assert.Equal("🍞 Brot kaufen #bread", tenant.TitleOf(ListId, "t1"));
        Assert.Equal("Ohne Tag", tenant.TitleOf(ListId, "t2"));

        var stored = await ReadAsync(host, change, cancellationToken);

        Assert.Equal(ChangeKind.ApplyMarkers, stored.Kind);
        Assert.Equal(ChangeState.Completed, stored.State);
        Assert.Equal(1, stored.WrittenCount);
        Assert.True(CanUndo(stored));
    }

    /// <summary>
    /// The Hashtag behind a block still indexes, which is the property that lets an Apply run twice
    /// without the second run losing the tasks the first one marked.
    /// </summary>
    [Fact]
    public async Task ApplyingTwice_ChangesNothingTheSecondTime()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tenant = new FakeTodoTenant();
        tenant.AddList(ListId, "Arbeit");
        tenant.AddTask(ListId, "t1", "Brot kaufen #bread");

        await using var host = await ChangeTestHost.StartAsync(database.ConnectionString, tenant, cancellationToken);

        await CreateRuleAsync(host, "bread", Bread, cancellationToken);
        await host.ScanAsync(cancellationToken);

        await ConfirmAsync(host, markerRuleKey: null, cancellationToken);
        Assert.True(await host.RunQueuedChangeAsync(cancellationToken));

        Assert.Equal("🍞 Brot kaufen #bread", tenant.TitleOf(ListId, "t1"));

        // The index still knows the tag is there, so a second preview finds the task and answers
        // that there is nothing left to do to it.
        await host.ScanAsync(cancellationToken);

        var again = await PreviewAsync(host, markerRuleKey: null, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
        Assert.Equal("Changes.NothingToChange", again.Code);
    }

    /// <summary>
    /// The Markers are copied into the Change at confirmation, so a rule edited while it waits has
    /// no effect on the run and takes effect at the next Apply (ADR-0014).
    /// </summary>
    [Fact]
    public async Task ARuleChangedAfterConfirmation_HasNoEffectOnTheRun()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tenant = new FakeTodoTenant();
        tenant.AddList(ListId, "Arbeit");
        tenant.AddTask(ListId, "t1", "Brot kaufen #bread");

        await using var host = await ChangeTestHost.StartAsync(database.ConnectionString, tenant, cancellationToken);

        var rule = await CreateRuleAsync(host, "bread", Bread, cancellationToken);
        await host.ScanAsync(cancellationToken);

        await ConfirmAsync(host, markerRuleKey: null, cancellationToken);

        // Between the confirmation and the run.
        await host.PatchAsync<MarkerRuleResponse>(
            $"/api/marker-rules/{rule.Id}",
            new { marker = Croissant },
            cancellationToken);

        Assert.True(await host.RunQueuedChangeAsync(cancellationToken));

        Assert.Equal("🍞 Brot kaufen #bread", tenant.TitleOf(ListId, "t1"));

        // The rule now says 🥐, and what the run wrote was 🍞 — so 🍞 is what the rule has to
        // remember as retired, or the next Apply would read it as text and write "🥐 🍞".
        var afterwards = Assert.Single(await ListRulesAsync(host, cancellationToken));

        Assert.Equal(Croissant, afterwards.Marker);
        Assert.Equal(Bread, afterwards.RetiredMarker);

        await host.ScanAsync(cancellationToken);
        await ConfirmAsync(host, markerRuleKey: null, cancellationToken);
        Assert.True(await host.RunQueuedChangeAsync(cancellationToken));

        Assert.Equal("🥐 Brot kaufen #bread", tenant.TitleOf(ListId, "t1"));
    }

    /// <summary>
    /// Undoing an Apply puts the retired Marker back into the titles, so the rule has to remember
    /// it again — or the Apply after the undo would write "🥐 🍞", two emoji where the user meant
    /// one and the stale one outside the block where nothing can reach it.
    /// </summary>
    [Fact]
    public async Task UndoingAnApplyThatSwappedAMarker_ReArmsTheSwap()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tenant = new FakeTodoTenant();
        tenant.AddList(ListId, "Arbeit");
        tenant.AddTask(ListId, "t1", "🍞 Brot kaufen #bread");

        await using var host = await ChangeTestHost.StartAsync(database.ConnectionString, tenant, cancellationToken);

        var rule = await CreateRuleAsync(host, "bread", Bread, cancellationToken);
        await host.ScanAsync(cancellationToken);

        await host.PatchAsync<MarkerRuleResponse>($"/api/marker-rules/{rule.Id}", new { marker = Croissant }, cancellationToken);

        var change = await ConfirmAsync(host, markerRuleKey: null, cancellationToken);
        Assert.True(await host.RunQueuedChangeAsync(cancellationToken));

        Assert.Equal("🥐 Brot kaufen #bread", tenant.TitleOf(ListId, "t1"));
        Assert.Null(Assert.Single(await ListRulesAsync(host, cancellationToken)).RetiredMarker);

        var undo = await host.PostAsync<ConfirmedChange>($"/api/changes/{change}/undo", new { }, cancellationToken);

        Assert.Equal(HttpStatusCode.Accepted, undo.StatusCode);
        Assert.True(await host.RunQueuedChangeAsync(cancellationToken));

        Assert.Equal("🍞 Brot kaufen #bread", tenant.TitleOf(ListId, "t1"));
        Assert.Equal(Bread, Assert.Single(await ListRulesAsync(host, cancellationToken)).RetiredMarker);

        await host.ScanAsync(cancellationToken);
        await ConfirmAsync(host, markerRuleKey: null, cancellationToken);
        Assert.True(await host.RunQueuedChangeAsync(cancellationToken));

        Assert.Equal("🥐 Brot kaufen #bread", tenant.TitleOf(ListId, "t1"));
    }

    /// <summary>
    /// A task passed over for being too long still carries the old Marker, so the rule keeps it
    /// retired: forgetting it would leave that title with an emoji no later Apply can reach.
    /// </summary>
    [Fact]
    public async Task ASkipForLength_KeepsTheRetiredMarkerArmed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tenant = new FakeTodoTenant();
        tenant.AddList(ListId, "Arbeit");
        tenant.AddTask(ListId, "t1", "🍞 Brot kaufen #bread");
        tenant.AddTask(ListId, "t2", "Kurz #bread");

        await using var host = await ChangeTestHost.StartAsync(database.ConnectionString, tenant, cancellationToken);

        var rule = await CreateRuleAsync(host, "bread", Bread, cancellationToken);
        await host.ScanAsync(cancellationToken);

        await host.PatchAsync<MarkerRuleResponse>($"/api/marker-rules/{rule.Id}", new { marker = Croissant }, cancellationToken);

        var change = await ConfirmAsync(host, markerRuleKey: null, cancellationToken);

        // Between the preview and the run, the first title grows past what To Do stores.
        tenant.RetitleTask(ListId, "t1", "🍞 " + TitleOfLength(TodoTaskLimits.TitleLength, "#bread"));

        Assert.True(await host.RunQueuedChangeAsync(cancellationToken));

        var stored = await ReadAsync(host, change, cancellationToken);

        Assert.Equal(ChangeState.CompletedWithSkips, stored.State);
        Assert.Equal(Bread, Assert.Single(await ListRulesAsync(host, cancellationToken)).RetiredMarker);
    }

    /// <summary>
    /// A task whose tag had gone by the time it was read is left as it was — old Marker and all —
    /// so the rule keeps that Marker retired; forgetting it would leave the title with an emoji no
    /// later Apply could reach, and the one after that would put a second block in front of it.
    /// </summary>
    [Fact]
    public async Task ASkipForATagThatHadGone_KeepsTheRetiredMarkerArmed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tenant = new FakeTodoTenant();
        tenant.AddList(ListId, "Arbeit");
        tenant.AddTask(ListId, "t1", "🍞 Brot kaufen #bread");
        tenant.AddTask(ListId, "t2", "Kurz #bread");

        await using var host = await ChangeTestHost.StartAsync(database.ConnectionString, tenant, cancellationToken);

        var rule = await CreateRuleAsync(host, "bread", Bread, cancellationToken);
        await host.ScanAsync(cancellationToken);

        await host.PatchAsync<MarkerRuleResponse>($"/api/marker-rules/{rule.Id}", new { marker = Croissant }, cancellationToken);

        await ConfirmAsync(host, markerRuleKey: null, cancellationToken);

        // Between the preview and the run, somebody takes the tag off the first task.
        tenant.RetitleTask(ListId, "t1", "🍞 Brot kaufen");

        Assert.True(await host.RunQueuedChangeAsync(cancellationToken));

        Assert.Equal("🍞 Brot kaufen", tenant.TitleOf(ListId, "t1"));
        Assert.Equal("🥐 Kurz #bread", tenant.TitleOf(ListId, "t2"));
        Assert.Equal(Bread, Assert.Single(await ListRulesAsync(host, cancellationToken)).RetiredMarker);
    }

    /// <summary>
    /// What a rule is told is about its own tasks. An all-rules Apply that reached every #coffee
    /// and none of the #bread tasks has written nothing about bread, and bread keeps its swap armed
    /// while coffee is settled.
    /// </summary>
    [Fact]
    public async Task ARuleIsToldOnlyAboutItsOwnTasks()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tenant = new FakeTodoTenant();
        tenant.AddList(ListId, "Arbeit");
        tenant.AddTask(ListId, "t1", "🍞 Brot kaufen #bread");
        tenant.AddTask(ListId, "t2", "🔴 Kaffee #coffee");

        await using var host = await ChangeTestHost.StartAsync(database.ConnectionString, tenant, cancellationToken);

        var bread = await CreateRuleAsync(host, "bread", Bread, cancellationToken);
        var coffee = await CreateRuleAsync(host, "coffee", "🔴", cancellationToken);
        await host.ScanAsync(cancellationToken);

        await host.PatchAsync<MarkerRuleResponse>($"/api/marker-rules/{bread.Id}", new { marker = Croissant }, cancellationToken);
        await host.PatchAsync<MarkerRuleResponse>($"/api/marker-rules/{coffee.Id}", new { marker = Coffee }, cancellationToken);

        await ConfirmAsync(host, markerRuleKey: null, cancellationToken);

        tenant.RetitleTask(ListId, "t1", "🍞 Brot kaufen");

        Assert.True(await host.RunQueuedChangeAsync(cancellationToken));

        var rules = await ListRulesAsync(host, cancellationToken);

        Assert.Equal(Bread, rules.Single(rule => rule.Spelling == "bread").RetiredMarker);
        Assert.Null(rules.Single(rule => rule.Spelling == "coffee").RetiredMarker);
    }

    /// <summary>
    /// Deleting a rule writes nothing, so its Marker stays at the front of every title it was
    /// applied to — and the next Apply has to go on reading it as part of the block, or it would
    /// put a second block in front of the first.
    /// </summary>
    [Fact]
    public async Task ADeletedRulesMarker_IsStillReadAsPartOfTheBlock()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tenant = new FakeTodoTenant();
        tenant.AddList(ListId, "Arbeit");
        tenant.AddTask(ListId, "t1", "🍞☕ Frühstück #bread #coffee");
        tenant.AddTask(ListId, "t2", "🍞 Brot #bread #coffee");

        await using var host = await ChangeTestHost.StartAsync(database.ConnectionString, tenant, cancellationToken);

        var bread = await CreateRuleAsync(host, "bread", Bread, cancellationToken);
        await CreateRuleAsync(host, "coffee", Coffee, cancellationToken);
        await host.ScanAsync(cancellationToken);

        var deleted = await host.DeleteAsync<MarkerRuleResponse>($"/api/marker-rules/{bread.Id}", cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal("coffee", Assert.Single(await ListRulesAsync(host, cancellationToken)).Spelling);

        await ConfirmAsync(host, markerRuleKey: null, cancellationToken);
        Assert.True(await host.RunQueuedChangeAsync(cancellationToken));

        // Both keep the 🍞 nobody asked about, behind the ☕ the standing rule asks for — one block,
        // in rule order, with the stale Marker where every stale Marker goes.
        Assert.Equal("☕🍞 Frühstück #bread #coffee", tenant.TitleOf(ListId, "t1"));
        Assert.Equal("☕🍞 Brot #bread #coffee", tenant.TitleOf(ListId, "t2"));
    }

    /// <summary>
    /// A task that lost the Hashtag before the plan was drawn is never planned, never reached and
    /// never swapped — and still carries the old Marker after the rule has stopped remembering it.
    /// The Marker stays known all the same, so the next Apply reads it as part of the block rather
    /// than putting a second block in front of it.
    /// </summary>
    [Fact]
    public async Task AMarkerARuleStoppedRemembering_IsStillReadAsPartOfTheBlock()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tenant = new FakeTodoTenant();
        tenant.AddList(ListId, "Arbeit");
        tenant.AddTask(ListId, "t1", "🍞🍳 Altes Brot #kitchen");
        tenant.AddTask(ListId, "t2", "🍞 Brot #bread");

        await using var host = await ChangeTestHost.StartAsync(database.ConnectionString, tenant, cancellationToken);

        var bread = await CreateRuleAsync(host, "bread", Bread, cancellationToken);
        await CreateRuleAsync(host, "kitchen", "🍳", cancellationToken);
        await host.ScanAsync(cancellationToken);

        await host.PatchAsync<MarkerRuleResponse>($"/api/marker-rules/{bread.Id}", new { marker = Croissant }, cancellationToken);

        await ConfirmAsync(host, HashtagKey.Fold("bread"), cancellationToken);
        Assert.True(await host.RunQueuedChangeAsync(cancellationToken));

        Assert.Equal("🥐 Brot #bread", tenant.TitleOf(ListId, "t2"));
        Assert.Null(Assert.Single(await ListRulesAsync(host, cancellationToken), rule => rule.Spelling == "bread").RetiredMarker);

        await host.ScanAsync(cancellationToken);
        await ConfirmAsync(host, HashtagKey.Fold("kitchen"), cancellationToken);
        Assert.True(await host.RunQueuedChangeAsync(cancellationToken));

        // 🍞 is nobody's now, and stays where every stale Marker stays: behind the ones a rule wants.
        Assert.Equal("🍳🍞 Altes Brot #kitchen", tenant.TitleOf(ListId, "t1"));
    }

    /// <summary>
    /// A rule whose Marker the user changed is the one case where an Apply takes an emoji away: the
    /// retired Marker is swapped for the new one inside the block, and the rule stops carrying it
    /// once the Apply has completed.
    /// </summary>
    [Fact]
    public async Task ARetiredMarkerIsSwapped_AndTheRuleStopsCarryingItOnCompletion()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tenant = new FakeTodoTenant();
        tenant.AddList(ListId, "Arbeit");
        tenant.AddTask(ListId, "t1", "🍞 Brot kaufen #bread");

        await using var host = await ChangeTestHost.StartAsync(database.ConnectionString, tenant, cancellationToken);

        var rule = await CreateRuleAsync(host, "bread", Bread, cancellationToken);
        await host.ScanAsync(cancellationToken);

        var changed = await host.PatchAsync<MarkerRuleResponse>(
            $"/api/marker-rules/{rule.Id}",
            new { marker = Croissant },
            cancellationToken);

        Assert.Equal(Bread, changed.Body!.RetiredMarker);

        await ConfirmAsync(host, markerRuleKey: null, cancellationToken);
        Assert.True(await host.RunQueuedChangeAsync(cancellationToken));

        Assert.Equal("🥐 Brot kaufen #bread", tenant.TitleOf(ListId, "t1"));

        var rules = await ListRulesAsync(host, cancellationToken);

        Assert.Null(Assert.Single(rules).RetiredMarker);
    }

    /// <summary>
    /// The Hashtag left the task between the preview and the run — the same skip the other three
    /// Changes report, for the same reason.
    /// </summary>
    [Fact]
    public async Task ATaskThatLostTheHashtag_IsSkipped()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tenant = new FakeTodoTenant();
        tenant.AddList(ListId, "Arbeit");
        tenant.AddTask(ListId, "t1", "Brot kaufen #bread");

        await using var host = await ChangeTestHost.StartAsync(database.ConnectionString, tenant, cancellationToken);

        await CreateRuleAsync(host, "bread", Bread, cancellationToken);
        await host.ScanAsync(cancellationToken);

        var change = await ConfirmAsync(host, markerRuleKey: null, cancellationToken);

        tenant.RetitleTask(ListId, "t1", "Brot kaufen");

        Assert.True(await host.RunQueuedChangeAsync(cancellationToken));

        var stored = await ReadAsync(host, change, cancellationToken);

        Assert.Equal(ChangeState.CompletedWithSkips, stored.State);
        Assert.Equal(0, stored.WrittenCount);
        Assert.Equal(1, stored.SkippedCount);
        Assert.Equal("Brot kaufen", tenant.TitleOf(ListId, "t1"));
    }

    /// <summary>Somebody marked it by hand in the meantime, so there is nothing left to write.</summary>
    [Fact]
    public async Task ATaskThatAlreadyReadsThatWay_IsSkipped()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tenant = new FakeTodoTenant();
        tenant.AddList(ListId, "Arbeit");
        tenant.AddTask(ListId, "t1", "Brot kaufen #bread");

        await using var host = await ChangeTestHost.StartAsync(database.ConnectionString, tenant, cancellationToken);

        await CreateRuleAsync(host, "bread", Bread, cancellationToken);
        await host.ScanAsync(cancellationToken);

        var change = await ConfirmAsync(host, markerRuleKey: null, cancellationToken);

        tenant.RetitleTask(ListId, "t1", "🍞 Brot kaufen #bread");

        Assert.True(await host.RunQueuedChangeAsync(cancellationToken));

        var stored = await ReadAsync(host, change, cancellationToken);

        Assert.Equal(ChangeState.CompletedWithSkips, stored.State);
        Assert.Equal(0, stored.WrittenCount);
        Assert.Equal(1, stored.SkippedCount);
        Assert.Empty(tenant.WrittenTitles);
    }

    /// <summary>
    /// The skip this Change adds: a block pushes a title past what Microsoft To Do stores. Named in
    /// the preview, because how close a title already sits to the limit is not the user's to guess,
    /// and again at run time against the title Graph returned.
    /// </summary>
    [Fact]
    public async Task ATitleTheBlockWouldPushPastTheLimit_IsNamedInThePreviewAndSkippedInTheRun()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tenant = new FakeTodoTenant();
        tenant.AddList(ListId, "Arbeit");
        tenant.AddTask(ListId, "t1", TitleOfLength(TodoTaskLimits.TitleLength, "#bread"));
        tenant.AddTask(ListId, "t2", "Kurz #bread");

        await using var host = await ChangeTestHost.StartAsync(database.ConnectionString, tenant, cancellationToken);

        await CreateRuleAsync(host, "bread", Bread, cancellationToken);
        await host.ScanAsync(cancellationToken);

        var preview = await PreviewAsync(host, markerRuleKey: null, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        Assert.Equal(1, preview.Body!.TaskCount);

        var skip = Assert.Single(preview.Body.Skips);
        Assert.Equal(ChangeSkips.TitleTooLongForToDo, skip.Reason);

        var change = await ConfirmAsync(host, markerRuleKey: null, cancellationToken);

        Assert.True(await host.RunQueuedChangeAsync(cancellationToken));

        // The long one was never in the plan, so the outcome counts the one task that was.
        var stored = await ReadAsync(host, change, cancellationToken);

        Assert.Equal(ChangeState.Completed, stored.State);
        Assert.Equal(1, stored.WrittenCount);
        Assert.Equal("🍞 Kurz #bread", tenant.TitleOf(ListId, "t2"));
    }

    /// <summary>
    /// And when the title grew past the limit after the preview, the runner is the one that finds
    /// out — and leaves the task alone rather than journalling a title that never existed.
    /// </summary>
    [Fact]
    public async Task ATitleThatGrewPastTheLimitAfterThePreview_IsSkippedAtRunTime()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tenant = new FakeTodoTenant();
        tenant.AddList(ListId, "Arbeit");
        tenant.AddTask(ListId, "t1", "Kurz #bread");

        await using var host = await ChangeTestHost.StartAsync(database.ConnectionString, tenant, cancellationToken);

        await CreateRuleAsync(host, "bread", Bread, cancellationToken);
        await host.ScanAsync(cancellationToken);

        var change = await ConfirmAsync(host, markerRuleKey: null, cancellationToken);

        tenant.RetitleTask(ListId, "t1", TitleOfLength(TodoTaskLimits.TitleLength, "#bread"));

        Assert.True(await host.RunQueuedChangeAsync(cancellationToken));

        var stored = await ReadAsync(host, change, cancellationToken);

        Assert.Equal(ChangeState.CompletedWithSkips, stored.State);
        Assert.Equal(0, stored.WrittenCount);
        Assert.Equal(1, stored.SkippedCount);
        Assert.Empty(tenant.WrittenTitles);
    }

    /// <summary>
    /// Undo restores the journaled titles, one level deep, and refuses where somebody edited the
    /// task since — exactly as for the other three.
    /// </summary>
    [Fact]
    public async Task UndoRestoresTheTitles_AndLeavesATaskSomebodyEditedAlone()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tenant = new FakeTodoTenant();
        tenant.AddList(ListId, "Arbeit");
        tenant.AddTask(ListId, "t1", "Brot kaufen #bread");
        tenant.AddTask(ListId, "t2", "Mehl kaufen #bread");

        await using var host = await ChangeTestHost.StartAsync(database.ConnectionString, tenant, cancellationToken);

        await CreateRuleAsync(host, "bread", Bread, cancellationToken);
        await host.ScanAsync(cancellationToken);

        var change = await ConfirmAsync(host, markerRuleKey: null, cancellationToken);
        Assert.True(await host.RunQueuedChangeAsync(cancellationToken));

        Assert.Equal("🍞 Brot kaufen #bread", tenant.TitleOf(ListId, "t1"));
        Assert.Equal("🍞 Mehl kaufen #bread", tenant.TitleOf(ListId, "t2"));

        // Somebody works on one of them afterwards.
        tenant.RetitleTask(ListId, "t2", "🍞 Mehl kaufen #bread — heute");

        var undo = await host.PostAsync<ConfirmedChange>(
            $"/api/changes/{change}/undo",
            new { },
            cancellationToken);

        Assert.Equal(HttpStatusCode.Accepted, undo.StatusCode);
        Assert.True(await host.RunQueuedChangeAsync(cancellationToken));

        Assert.Equal("Brot kaufen #bread", tenant.TitleOf(ListId, "t1"));
        Assert.Equal("🍞 Mehl kaufen #bread — heute", tenant.TitleOf(ListId, "t2"));

        var stored = await ReadAsync(host, undo.Body!.Id, cancellationToken);

        Assert.Equal(ChangeKind.ApplyMarkers, stored.Kind);
        Assert.Equal(ChangeState.CompletedWithSkips, stored.State);
        Assert.Equal(1, stored.WrittenCount);
        Assert.Equal(1, stored.SkippedCount);
    }

    /// <summary>
    /// An all-rules Apply records no scope keys at all. Fifty folded keys would not fit the bounded
    /// column the scope is stored in, and "all of them" stays true after a rule is added or deleted
    /// — the rules the Change actually applies are carried beside it either way.
    /// </summary>
    [Fact]
    public async Task AnAllRulesApply_RecordsNoScopeKeysAndStillCoversEveryRule()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tenant = new FakeTodoTenant();
        tenant.AddList(ListId, "Arbeit");
        tenant.AddTask(ListId, "t1", "Brot kaufen #bread");
        tenant.AddTask(ListId, "t2", "Kaffee mahlen #coffee");

        await using var host = await ChangeTestHost.StartAsync(database.ConnectionString, tenant, cancellationToken);

        await CreateRuleAsync(host, "bread", Bread, cancellationToken);
        await CreateRuleAsync(host, "coffee", Coffee, cancellationToken);
        await host.ScanAsync(cancellationToken);

        var change = await ConfirmAsync(host, markerRuleKey: null, cancellationToken);

        Assert.Empty((await ReadAsync(host, change, cancellationToken)).SourceKeys);

        Assert.True(await host.RunQueuedChangeAsync(cancellationToken));

        Assert.Equal("🍞 Brot kaufen #bread", tenant.TitleOf(ListId, "t1"));
        Assert.Equal("☕ Kaffee mahlen #coffee", tenant.TitleOf(ListId, "t2"));
        Assert.Equal(2, (await ReadAsync(host, change, cancellationToken)).WrittenCount);
    }

    /// <summary>
    /// When every task an Apply covers would be pushed past the limit, "no task needs this change"
    /// is false and would send somebody looking in their rules for a problem that is in their
    /// titles.
    /// </summary>
    [Fact]
    public async Task AnApplyWhereEveryTaskIsTooLong_SaysThatRatherThanNothingToChange()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tenant = new FakeTodoTenant();
        tenant.AddList(ListId, "Arbeit");
        tenant.AddTask(ListId, "t1", TitleOfLength(TodoTaskLimits.TitleLength, "#bread"));

        await using var host = await ChangeTestHost.StartAsync(database.ConnectionString, tenant, cancellationToken);

        await CreateRuleAsync(host, "bread", Bread, cancellationToken);
        await host.ScanAsync(cancellationToken);

        var preview = await PreviewAsync(host, markerRuleKey: null, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, preview.StatusCode);
        Assert.Equal("Changes.EveryTaskWouldBeTooLong", preview.Code);
    }

    [Fact]
    public async Task AnApplyWithNoRulesAtAll_IsRefusedWithItsOwnReason()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tenant = new FakeTodoTenant();
        tenant.AddList(ListId, "Arbeit");
        tenant.AddTask(ListId, "t1", "Brot kaufen #bread");

        await using var host = await ChangeTestHost.StartAsync(database.ConnectionString, tenant, cancellationToken);
        await host.ScanAsync(cancellationToken);

        var preview = await PreviewAsync(host, markerRuleKey: null, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, preview.StatusCode);
        Assert.Equal("Changes.NoMarkerRules", preview.Code);
    }

    [Fact]
    public async Task AnApplyScopedToAHashtagWithNoRule_IsRefused()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tenant = new FakeTodoTenant();
        tenant.AddList(ListId, "Arbeit");
        tenant.AddTask(ListId, "t1", "Brot kaufen #bread");

        await using var host = await ChangeTestHost.StartAsync(database.ConnectionString, tenant, cancellationToken);

        await CreateRuleAsync(host, "bread", Bread, cancellationToken);
        await host.ScanAsync(cancellationToken);

        var preview = await PreviewAsync(host, HashtagKey.Fold("coffee"), cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, preview.StatusCode);
        Assert.Equal("Changes.MarkerRuleNotFound", preview.Code);
    }

    /// <summary>Past the ceiling an Apply is refused with the count, like any other Change.</summary>
    [Fact]
    public async Task AnApplyPastTheCeiling_IsRefusedWithTheCount()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tenant = new FakeTodoTenant();
        tenant.AddList(ListId, "Arbeit");
        tenant.AddTask(ListId, "t1", "Brot kaufen #bread");
        tenant.AddTask(ListId, "t2", "Mehl kaufen #bread");

        await using var host = await ChangeTestHost.StartAsync(
            database.ConnectionString,
            tenant,
            cancellationToken,
            maxTasksPerChange: 1);

        await CreateRuleAsync(host, "bread", Bread, cancellationToken);
        await host.ScanAsync(cancellationToken);

        var preview = await PreviewAsync(host, markerRuleKey: null, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, preview.StatusCode);
        Assert.Equal("Changes.PlanTooLarge", preview.Code);
    }

    /// <summary>One Change at a time, whichever shape either of them has (ADR-0006).</summary>
    [Fact]
    public async Task AnApplyWhileAnotherChangeIsInFlight_IsRefused()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tenant = new FakeTodoTenant();
        tenant.AddList(ListId, "Arbeit");
        tenant.AddTask(ListId, "t1", "Brot kaufen #bread");

        await using var host = await ChangeTestHost.StartAsync(database.ConnectionString, tenant, cancellationToken);

        await CreateRuleAsync(host, "bread", Bread, cancellationToken);
        await host.ScanAsync(cancellationToken);

        await host.PostAsync<ConfirmedChange>(
            "/api/changes",
            new { sourceKeys = new[] { HashtagKey.Fold("bread") }, targetSpelling = "Bread", confirmMerge = false },
            cancellationToken);

        var preview = await PreviewAsync(host, markerRuleKey: null, cancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, preview.StatusCode);
        Assert.Equal("Changes.AlreadyInFlight", preview.Code);
    }

    private static Task<ApiResult<ChangePreviewResponse>> PreviewAsync(
        ChangeTestHost host,
        string? markerRuleKey,
        CancellationToken cancellationToken) =>
        host.PostAsync<ChangePreviewResponse>(
            "/api/changes/preview",
            new { applyMarkers = true, markerRuleKey },
            cancellationToken);

    private static async Task<Guid> ConfirmAsync(
        ChangeTestHost host,
        string? markerRuleKey,
        CancellationToken cancellationToken)
    {
        var result = await host.PostAsync<ConfirmedChange>(
            "/api/changes",
            new { applyMarkers = true, markerRuleKey },
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

    private static async Task<IReadOnlyList<MarkerRuleResponse>> ListRulesAsync(
        ChangeTestHost host,
        CancellationToken cancellationToken)
    {
        var listed = await host.GetAsync<MarkerRuleListResponse>("/api/marker-rules", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, listed.StatusCode);

        return listed.Body!.Rules;
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

    private static bool CanUndo(ChangeRecord change) =>
        ChangeUndoPolicy.CanUndo(change, DateTimeOffset.UtcNow, TimeSpan.FromDays(30));

    /// <summary>A title of exactly <paramref name="length"/> characters, ending in a Hashtag.</summary>
    private static string TitleOfLength(int length, string hashtag) =>
        string.Concat(new string('x', length - hashtag.Length - 1), " ", hashtag);

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
        int Position);

    private sealed record MarkerRuleListResponse(IReadOnlyList<MarkerRuleResponse> Rules);

    private sealed record ConfirmedChange(Guid Id);
}
