using System.Net;
using Xunit;

namespace TodoWerk.IntegrationTests;

/// <summary>
/// "n of m tagged tasks carry 🍞", against SQL Server and an index built from the fake
/// mailbox rather than from rows written by hand.
/// <para>
/// The figure is about the block, not about the title. An emoji somebody typed mid-sentence is
/// text, and counting it would tell them a rule had been applied to a task it never touched.
/// </para>
/// </summary>
public sealed class MarkerCoverageTests(SqlServerDatabaseFixture database) : IClassFixture<SqlServerDatabaseFixture>
{
    private const string ListId = "list-arbeit";

    private const string Bread = "🍞";

    [Fact]
    public async Task CoverageCountsTheBlockedTasksAndNotTheOthers()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tenant = new FakeTodoTenant();
        tenant.AddList(ListId, "Arbeit");
        tenant.AddTask(ListId, "t1", "🍞 Brot kaufen #bread");
        tenant.AddTask(ListId, "t2", "Mehl kaufen #bread");
        tenant.AddTask(ListId, "t3", "Brot 🍞 kaufen #bread");
        tenant.AddTask(ListId, "t4", "Ohne Tag");

        await using var host = await ChangeTestHost.StartAsync(database.ConnectionString, tenant, cancellationToken);

        await CreateRuleAsync(host, "bread", Bread, cancellationToken);
        await host.ScanAsync(cancellationToken);

        var rule = Assert.Single(await ListRulesAsync(host, cancellationToken));

        // Three tasks carry #bread; only the one whose block opens with 🍞 counts as marked.
        Assert.Equal(3, rule.TaggedTaskCount);
        Assert.Equal(1, rule.MarkedTaskCount);
    }

    /// <summary>
    /// A rule outlives its Occurrences, and its coverage says so rather than being absent: nought
    /// of nought is an honest answer about a Hashtag nobody uses any more.
    /// </summary>
    [Fact]
    public async Task ARuleWithNoTaggedTasks_ReportsNought()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tenant = new FakeTodoTenant();
        tenant.AddList(ListId, "Arbeit");
        tenant.AddTask(ListId, "t1", "Ohne Tag");

        await using var host = await ChangeTestHost.StartAsync(database.ConnectionString, tenant, cancellationToken);

        await CreateRuleAsync(host, "bread", Bread, cancellationToken);
        await host.ScanAsync(cancellationToken);

        var rule = Assert.Single(await ListRulesAsync(host, cancellationToken));

        Assert.Equal(0, rule.TaggedTaskCount);
        Assert.Equal(0, rule.MarkedTaskCount);
    }

    /// <summary>
    /// The figure a user actually watches: it goes from nought to all of them when the Apply runs,
    /// once the scan behind it has caught up.
    /// </summary>
    [Fact]
    public async Task CoverageReachesEveryTaggedTask_AfterAnApply()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tenant = new FakeTodoTenant();
        tenant.AddList(ListId, "Arbeit");
        tenant.AddTask(ListId, "t1", "Brot kaufen #bread");
        tenant.AddTask(ListId, "t2", "Mehl kaufen #bread");

        await using var host = await ChangeTestHost.StartAsync(database.ConnectionString, tenant, cancellationToken);

        await CreateRuleAsync(host, "bread", Bread, cancellationToken);
        await host.ScanAsync(cancellationToken);

        Assert.Equal(0, (await ListRulesAsync(host, cancellationToken))[0].MarkedTaskCount);

        var confirmed = await host.PostAsync<ConfirmedChange>(
            "/api/changes",
            new { applyMarkers = true },
            cancellationToken);

        Assert.Equal(HttpStatusCode.Accepted, confirmed.StatusCode);
        Assert.True(await host.RunQueuedChangeAsync(cancellationToken));

        // The index catches up through Graph, which is what the Workbench's freshness indicator is
        // about — until the scan lands, coverage is behind by one.
        await host.ScanAsync(cancellationToken);

        var rule = Assert.Single(await ListRulesAsync(host, cancellationToken));

        Assert.Equal(2, rule.TaggedTaskCount);
        Assert.Equal(2, rule.MarkedTaskCount);
    }

    /// <summary>
    /// A deleted rule's Marker is still in the blocks it was applied to, so coverage of the rules
    /// beside it has to read a block with it in.
    /// </summary>
    [Fact]
    public async Task ADeletedRulesMarker_DoesNotEndTheBlockForCoverage()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tenant = new FakeTodoTenant();
        tenant.AddList(ListId, "Arbeit");
        tenant.AddTask(ListId, "t1", "🍞☕ Frühstück #bread #coffee");

        await using var host = await ChangeTestHost.StartAsync(database.ConnectionString, tenant, cancellationToken);

        var bread = await CreateRuleAsync(host, "bread", Bread, cancellationToken);
        await CreateRuleAsync(host, "coffee", "☕", cancellationToken);
        await host.ScanAsync(cancellationToken);

        await host.DeleteAsync<MarkerRuleResponse>($"/api/marker-rules/{bread.Id}", cancellationToken);

        var rule = Assert.Single(await ListRulesAsync(host, cancellationToken));

        Assert.Equal("coffee", rule.Spelling);
        Assert.Equal(1, rule.MarkedTaskCount);
    }

    /// <summary>
    /// One emoji written two ways is one Marker and two byte sequences. The index is asked for
    /// both spellings, or a rule written the other way from its titles would read as never applied.
    /// </summary>
    [Fact]
    public async Task ARuleInOnePresentation_CountsTitlesInTheOther()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tenant = new FakeTodoTenant();
        tenant.AddList(ListId, "Arbeit");

        // The title is typed bare; the rule is what a picker emits, with the presentation selector.
        tenant.AddTask(ListId, "t1", "\u2709 Post #mail");

        await using var host = await ChangeTestHost.StartAsync(database.ConnectionString, tenant, cancellationToken);

        await CreateRuleAsync(host, "mail", "\u2709\uFE0F", cancellationToken);
        await host.ScanAsync(cancellationToken);

        var rule = Assert.Single(await ListRulesAsync(host, cancellationToken));

        Assert.Equal(1, rule.MarkedTaskCount);
    }

    /// <summary>
    /// Two tasks with one title are two tasks. A count that read titles rather than tasks would
    /// fold them into one, and "1 of 2" would be a lie about a rule that reached both.
    /// </summary>
    [Fact]
    public async Task TwoTasksWithTheSameTitle_AreCountedTwice()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tenant = new FakeTodoTenant();
        tenant.AddList(ListId, "Arbeit");
        tenant.AddTask(ListId, "t1", "🍞 Brot kaufen #bread");
        tenant.AddTask(ListId, "t2", "🍞 Brot kaufen #bread");

        await using var host = await ChangeTestHost.StartAsync(database.ConnectionString, tenant, cancellationToken);

        await CreateRuleAsync(host, "bread", Bread, cancellationToken);
        await host.ScanAsync(cancellationToken);

        var rule = Assert.Single(await ListRulesAsync(host, cancellationToken));

        Assert.Equal(2, rule.TaggedTaskCount);
        Assert.Equal(2, rule.MarkedTaskCount);
    }

    /// <summary>
    /// A block ends at the first grapheme that is not one of this person's Markers, retired ones
    /// included. Without them, one rule's pending swap would read as "no block" on every task it
    /// shares with another rule, and that rule's count would collapse to nought.
    /// </summary>
    [Fact]
    public async Task ARetiredMarkerOnOneRule_DoesNotZeroAnothersCoverage()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tenant = new FakeTodoTenant();
        tenant.AddList(ListId, "Arbeit");
        tenant.AddTask(ListId, "t1", "🍞☕ Frühstück #bread #coffee");

        await using var host = await ChangeTestHost.StartAsync(database.ConnectionString, tenant, cancellationToken);

        var bread = await CreateRuleAsync(host, "bread", Bread, cancellationToken);
        await CreateRuleAsync(host, "coffee", "☕", cancellationToken);
        await host.ScanAsync(cancellationToken);

        await host.PatchAsync<MarkerRuleResponse>($"/api/marker-rules/{bread.Id}", new { marker = "🥐" }, cancellationToken);

        var rules = await ListRulesAsync(host, cancellationToken);

        // #bread is not yet marked the way its rule now says; #coffee is, and still counts.
        Assert.Equal(0, rules.Single(rule => rule.Spelling == "bread").MarkedTaskCount);
        Assert.Equal(1, rules.Single(rule => rule.Spelling == "coffee").MarkedTaskCount);
    }

    /// <summary>
    /// A title opening with somebody else's emoji has no block, and — since the database's default
    /// collation calls every emoji equal to every other — this is also the case that says the
    /// narrowing behind the count compares bytes rather than weights.
    /// </summary>
    [Fact]
    public async Task ATitleOpeningWithAnotherEmoji_IsNotMarked()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tenant = new FakeTodoTenant();
        tenant.AddList(ListId, "Arbeit");
        tenant.AddTask(ListId, "t1", "🎉 Party #bread");
        tenant.AddTask(ListId, "t2", "🍞 Brot #bread");

        await using var host = await ChangeTestHost.StartAsync(database.ConnectionString, tenant, cancellationToken);

        await CreateRuleAsync(host, "bread", Bread, cancellationToken);
        await host.ScanAsync(cancellationToken);

        var rule = Assert.Single(await ListRulesAsync(host, cancellationToken));

        Assert.Equal(2, rule.TaggedTaskCount);
        Assert.Equal(1, rule.MarkedTaskCount);
    }

    /// <summary>
    /// The pending swap counted rather than merely remembered. A rule keeps the Marker it
    /// replaced so a later Apply can reach it; what the rules view says is how much of that reach
    /// is left, which is nought once every task has been swapped.
    /// </summary>
    [Fact]
    public async Task ThePendingSwap_IsCountedAndFallsToNoughtOnceApplied()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tenant = new FakeTodoTenant();
        tenant.AddList(ListId, "Arbeit");
        tenant.AddTask(ListId, "t1", "Brot kaufen #bread");
        tenant.AddTask(ListId, "t2", "Mehl kaufen #bread");

        await using var host = await ChangeTestHost.StartAsync(database.ConnectionString, tenant, cancellationToken);

        var bread = await CreateRuleAsync(host, "bread", Bread, cancellationToken);
        await host.ScanAsync(cancellationToken);

        // Applied everywhere, then given a new Marker: both titles now carry the old one.
        await host.PostAsync<ConfirmedChange>(
            "/api/changes",
            new { applyMarkers = true, markerRuleKey = (string?)null },
            cancellationToken);

        Assert.True(await host.RunQueuedChangeAsync(cancellationToken));
        await host.ScanAsync(cancellationToken);

        await host.PatchAsync<MarkerRuleResponse>(
            $"/api/marker-rules/{bread.Id}",
            new { marker = "🥐" },
            cancellationToken);

        var waiting = Assert.Single(await ListRulesAsync(host, cancellationToken));

        Assert.Equal(Bread, waiting.RetiredMarker);
        Assert.Equal(2, waiting.RetiredTaskCount);

        // And once the swap has been applied, there is nothing left for it to reach.
        await host.PostAsync<ConfirmedChange>(
            "/api/changes",
            new { applyMarkers = true, markerRuleKey = (string?)null },
            cancellationToken);

        Assert.True(await host.RunQueuedChangeAsync(cancellationToken));
        await host.ScanAsync(cancellationToken);

        var settled = Assert.Single(await ListRulesAsync(host, cancellationToken));

        Assert.Equal(0, settled.RetiredTaskCount);
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

    private sealed record MarkerRuleResponse(
        Guid Id,
        string Key,
        string Spelling,
        string Marker,
        string? RetiredMarker,
        int Position,
        int TaggedTaskCount,
        int MarkedTaskCount,
        int StaleTaskCount,
        int RetiredTaskCount);

    private sealed record MarkerRuleListResponse(
        IReadOnlyList<MarkerRuleResponse> Rules,
        IReadOnlyList<AbandonedMarkerResponse> Abandoned);

    private sealed record AbandonedMarkerResponse(string Marker, int StaleTaskCount);

    private sealed record ConfirmedChange(Guid Id);
}
