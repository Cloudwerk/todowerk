using System.Net;
using TodoWerk.Domain.Hashtags;
using Xunit;

namespace TodoWerk.IntegrationTests;

/// <summary>
/// What the three Hashtag Changes do to Marker Rules (ADR-0014). A rule belongs to a Hashtag, so
/// renaming one has to take its Marker along and folding two together has to say which Marker
/// survives — and none of it writes a task title.
/// </summary>
public sealed class MarkerRulesFollowChangesTests(SqlServerDatabaseFixture database)
    : IClassFixture<SqlServerDatabaseFixture>
{
    private const string ListId = "list-arbeit";

    private const string Bread = "🍞";

    private const string Coffee = "☕";

    /// <summary>
    /// The preview says so beforehand, because "your marker moves too" is part of what somebody is
    /// agreeing to and finding out afterwards is finding out too late.
    /// </summary>
    [Fact]
    public async Task ARenamePreview_SaysTheRuleWillMove()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var host = await StartAsync(cancellationToken, ("t1", "Brot kaufen #bread"));

        await CreateRuleAsync(host, "bread", Bread, cancellationToken);
        await host.ScanAsync(cancellationToken);

        var preview = await PreviewAsync(host, [Key("bread")], "loaf", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        Assert.Equal("Rename", preview.Body!.Kind);

        var rules = preview.Body.MarkerRules;

        Assert.Equal(Bread, Assert.Single(rules.SourceRules).Marker);
        Assert.Null(rules.TargetRule);
        Assert.Equal(Bread, rules.SurvivingMarker);
        Assert.False(rules.RequiresSurvivorChoice);
    }

    [Fact]
    public async Task ACompletedRename_MovesTheRuleToTheNewName()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var host = await StartAsync(cancellationToken, ("t1", "Brot kaufen #bread"));

        await CreateRuleAsync(host, "bread", Bread, cancellationToken);
        await host.ScanAsync(cancellationToken);

        await ConfirmAsync(host, [Key("bread")], "loaf", cancellationToken);
        Assert.True(await host.RunQueuedChangeAsync(cancellationToken));

        var rule = Assert.Single(await ListRulesAsync(host, cancellationToken));

        Assert.Equal(Key("loaf"), rule.Key);
        Assert.Equal("loaf", rule.Spelling);
        Assert.Equal(Bread, rule.Marker);
    }

    /// <summary>
    /// A Rename that wrote nothing renamed nothing, so the rule stays where it is — otherwise the
    /// Marker would sit on a name no task carries while every task still carried the old one.
    /// </summary>
    [Fact]
    public async Task ARenameThatWroteNothing_LeavesTheRuleWhereItIs()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tenant = new FakeTodoTenant();
        tenant.AddList(ListId, "Arbeit");
        tenant.AddTask(ListId, "t1", "Brot kaufen #bread");

        await using var host = await ChangeTestHost.StartAsync(database.ConnectionString, tenant, cancellationToken);

        await CreateRuleAsync(host, "bread", Bread, cancellationToken);
        await host.ScanAsync(cancellationToken);

        await ConfirmAsync(host, [Key("bread")], "loaf", cancellationToken);

        // The tag is gone by the time the runner gets there, so every planned task is skipped.
        tenant.RetitleTask(ListId, "t1", "Brot kaufen");

        Assert.True(await host.RunQueuedChangeAsync(cancellationToken));

        var rule = Assert.Single(await ListRulesAsync(host, cancellationToken));

        Assert.Equal(Key("bread"), rule.Key);
    }

    [Fact]
    public async Task UndoingARename_MovesTheRuleBack()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var host = await StartAsync(cancellationToken, ("t1", "Brot kaufen #bread"));

        await CreateRuleAsync(host, "bread", Bread, cancellationToken);
        await host.ScanAsync(cancellationToken);

        var change = await ConfirmAsync(host, [Key("bread")], "loaf", cancellationToken);
        Assert.True(await host.RunQueuedChangeAsync(cancellationToken));

        var undo = await host.PostAsync<ConfirmedChange>($"/api/changes/{change}/undo", new { }, cancellationToken);

        Assert.Equal(HttpStatusCode.Accepted, undo.StatusCode);
        Assert.True(await host.RunQueuedChangeAsync(cancellationToken));

        var rule = Assert.Single(await ListRulesAsync(host, cancellationToken));

        Assert.Equal(Key("bread"), rule.Key);
        Assert.Equal("bread", rule.Spelling);
        Assert.Equal(Bread, rule.Marker);
    }

    /// <summary>
    /// A rule outlives its Occurrences, so a name can carry a rule while no task carries the name —
    /// and a Rename onto it is still a Rename to the classifier, which knows only the index. The
    /// target's own rule wins all the same, the preview says so, and the source rule goes.
    /// </summary>
    [Fact]
    public async Task ARenameOntoANameWhoseRuleOutlivedItsTasks_KeepsThatRule()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var host = await StartAsync(cancellationToken, ("t1", "Brot kaufen #bread"));

        await CreateRuleAsync(host, "bread", Bread, cancellationToken);
        await CreateRuleAsync(host, "loaf", Coffee, cancellationToken);
        await host.ScanAsync(cancellationToken);

        var preview = await PreviewAsync(host, [Key("bread")], "loaf", cancellationToken);

        Assert.Equal("Rename", preview.Body!.Kind);
        Assert.Equal(Coffee, preview.Body.MarkerRules.TargetRule!.Marker);
        Assert.Equal(Coffee, preview.Body.MarkerRules.SurvivingMarker);

        await ConfirmAsync(host, [Key("bread")], "loaf", cancellationToken);
        Assert.True(await host.RunQueuedChangeAsync(cancellationToken));

        var rule = Assert.Single(await ListRulesAsync(host, cancellationToken));

        Assert.Equal(Key("loaf"), rule.Key);
        Assert.Equal(Coffee, rule.Marker);
    }

    /// <summary>Normalise Casing moves nothing, because the key does not change.</summary>
    [Fact]
    public async Task NormaliseCasing_MovesNothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var host = await StartAsync(cancellationToken, ("t1", "Brot kaufen #bread"));

        await CreateRuleAsync(host, "bread", Bread, cancellationToken);
        await host.ScanAsync(cancellationToken);

        var preview = await PreviewAsync(host, [Key("bread")], "Bread", cancellationToken);

        Assert.Equal("NormaliseCasing", preview.Body!.Kind);
        Assert.Empty(preview.Body.MarkerRules.SourceRules);
        Assert.Null(preview.Body.MarkerRules.TargetRule);

        await ConfirmAsync(host, [Key("bread")], "Bread", cancellationToken);
        Assert.True(await host.RunQueuedChangeAsync(cancellationToken));

        var rule = Assert.Single(await ListRulesAsync(host, cancellationToken));

        Assert.Equal(Key("bread"), rule.Key);
        Assert.Equal("bread", rule.Spelling);
    }

    /// <summary>A Merge whose sources hold no rule has nothing to say about markers.</summary>
    [Fact]
    public async Task AMergeWithNoRules_ReportsNone()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var host = await StartAsync(
            cancellationToken,
            ("t1", "Brot #bread"),
            ("t2", "Laib #loaf"));

        await host.ScanAsync(cancellationToken);

        var preview = await PreviewAsync(host, [Key("bread"), Key("loaf")], "loaf", cancellationToken);

        Assert.Equal("Merge", preview.Body!.Kind);
        Assert.Empty(preview.Body.MarkerRules.SourceRules);
        Assert.Null(preview.Body.MarkerRules.SurvivingMarker);
        Assert.False(preview.Body.MarkerRules.RequiresSurvivorChoice);
    }

    /// <summary>Exactly one source rule, and no rule on the target: it follows the Merge.</summary>
    [Fact]
    public async Task AMergeWithOneSourceRule_MovesIt()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var host = await StartAsync(
            cancellationToken,
            ("t1", "Brot #bread"),
            ("t2", "Laib #loaf"));

        await CreateRuleAsync(host, "bread", Bread, cancellationToken);
        await host.ScanAsync(cancellationToken);

        var preview = await PreviewAsync(host, [Key("bread"), Key("loaf")], "loaf", cancellationToken);

        Assert.Equal(Bread, preview.Body!.MarkerRules.SurvivingMarker);
        Assert.False(preview.Body.MarkerRules.RequiresSurvivorChoice);

        await ConfirmAsync(host, [Key("bread"), Key("loaf")], "loaf", cancellationToken, merge: true);
        Assert.True(await host.RunQueuedChangeAsync(cancellationToken));

        var rule = Assert.Single(await ListRulesAsync(host, cancellationToken));

        Assert.Equal(Key("loaf"), rule.Key);
        Assert.Equal(Bread, rule.Marker);
    }

    /// <summary>
    /// The target's own rule wins, because the target is the Hashtag that survives — and the
    /// preview says which rules go.
    /// </summary>
    [Fact]
    public async Task AMergeOntoAHashtagWithItsOwnRule_KeepsThatOneAndDeletesTheRest()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var host = await StartAsync(
            cancellationToken,
            ("t1", "Brot #bread"),
            ("t2", "Laib #loaf"));

        await CreateRuleAsync(host, "bread", Bread, cancellationToken);
        await CreateRuleAsync(host, "loaf", Coffee, cancellationToken);
        await host.ScanAsync(cancellationToken);

        var preview = await PreviewAsync(host, [Key("bread"), Key("loaf")], "loaf", cancellationToken);

        Assert.Equal(Coffee, preview.Body!.MarkerRules.TargetRule!.Marker);
        Assert.Equal(Coffee, preview.Body.MarkerRules.SurvivingMarker);
        Assert.Equal(Bread, Assert.Single(preview.Body.MarkerRules.SourceRules).Marker);
        Assert.False(preview.Body.MarkerRules.RequiresSurvivorChoice);

        await ConfirmAsync(host, [Key("bread"), Key("loaf")], "loaf", cancellationToken, merge: true);
        Assert.True(await host.RunQueuedChangeAsync(cancellationToken));

        var rule = Assert.Single(await ListRulesAsync(host, cancellationToken));

        Assert.Equal(Key("loaf"), rule.Key);
        Assert.Equal(Coffee, rule.Marker);
    }

    /// <summary>
    /// Two source rules and no target rule is the one case nobody but the user can settle, so
    /// confirming without a choice is refused the way an unconfirmed Merge is.
    /// </summary>
    [Fact]
    public async Task AMergeWithTwoSourceRules_AsksWhichMarkerSurvives()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var host = await StartAsync(
            cancellationToken,
            ("t1", "Brot #bread"),
            ("t2", "Kaffee #coffee"),
            ("t3", "Frühstück #breakfast"));

        await CreateRuleAsync(host, "bread", Bread, cancellationToken);
        await CreateRuleAsync(host, "coffee", Coffee, cancellationToken);
        await host.ScanAsync(cancellationToken);

        var sources = new[] { Key("bread"), Key("coffee") };

        var preview = await PreviewAsync(host, sources, "breakfast", cancellationToken);

        Assert.True(preview.Body!.MarkerRules.RequiresSurvivorChoice);
        Assert.Null(preview.Body.MarkerRules.SurvivingMarker);
        Assert.Equal(2, preview.Body.MarkerRules.SourceRules.Count);

        var refused = await host.PostAsync<ConfirmedChange>(
            "/api/changes",
            new { sourceKeys = sources, targetSpelling = "breakfast", confirmMerge = true },
            cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal("Changes.MarkerSurvivorNotChosen", refused.Code);

        var confirmed = await host.PostAsync<ConfirmedChange>(
            "/api/changes",
            new
            {
                sourceKeys = sources,
                targetSpelling = "breakfast",
                confirmMerge = true,
                survivingMarker = Coffee,
            },
            cancellationToken);

        Assert.Equal(HttpStatusCode.Accepted, confirmed.StatusCode);
        Assert.True(await host.RunQueuedChangeAsync(cancellationToken));

        var rule = Assert.Single(await ListRulesAsync(host, cancellationToken));

        Assert.Equal(Key("breakfast"), rule.Key);
        Assert.Equal(Coffee, rule.Marker);
    }

    /// <summary>
    /// The tail of a run is deliberately redone rather than lost: <c>CarryRulesAsync</c> happens
    /// before <c>FinishAsync</c>, so a process replaced between them reaches the tail again. That
    /// makes idempotence a requirement rather than a nicety: a second pass that found nothing on
    /// the source key must not read that as "the rule is still there and lost the Merge" and delete
    /// whatever stood there.
    /// <para>
    /// In the crash window, what stands there can be a rule the person has just created at the old
    /// name. This runs one claim twice with exactly that in between: a second pass that
    /// soft-deleted the new rule would cost the person a row to a retry they never saw.
    /// </para>
    /// </summary>
    [Fact]
    public async Task ARerunOfAFinishedRename_LeavesARuleCreatedAtTheOldNameAlone()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var host = await StartAsync(cancellationToken, ("t1", "Brot kaufen #bread"));

        await CreateRuleAsync(host, "bread", Bread, cancellationToken);
        await host.ScanAsync(cancellationToken);

        await ConfirmAsync(host, [Key("bread")], "loaf", cancellationToken);

        var claimed = await host.ClaimQueuedChangeAsync(cancellationToken);
        Assert.NotNull(claimed);

        await host.RunClaimedChangeAsync(claimed, cancellationToken);

        // The rule followed the Rename, and the person now uses the freed name for something else.
        Assert.Equal(Key("loaf"), Assert.Single(await ListRulesAsync(host, cancellationToken)).Key);
        await CreateRuleAsync(host, "bread", Coffee, cancellationToken);

        // The crash: the row never reached FinishAsync, so it is still Running under this lease.
        await host.ReopenClaimedChangeAsync(claimed, cancellationToken);
        await host.RunClaimedChangeAsync(claimed, cancellationToken);

        var rules = await ListRulesAsync(host, cancellationToken);

        Assert.Equal(2, rules.Count);
        Assert.Equal(Bread, Assert.Single(rules, rule => rule.Key == Key("loaf")).Marker);
        Assert.Equal(Coffee, Assert.Single(rules, rule => rule.Key == Key("bread")).Marker);
    }

    /// <summary>
    /// The other half of the same distinction, and the one that must keep working: a Merge onto a
    /// Hashtag that has its own rule really does leave the source's rule behind to be deleted. A
    /// source key that is empty and a source key whose rule lost are not the same thing, which is
    /// why the carry answers with which of them happened rather than with a bool.
    /// </summary>
    [Fact]
    public async Task ARerunOfAMergeOntoARuleOfItsOwn_StillDeletesTheLoser()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var host = await StartAsync(
            cancellationToken,
            ("t1", "Brot kaufen #bread"),
            ("t2", "Kaffee holen #coffee"));

        await CreateRuleAsync(host, "bread", Bread, cancellationToken);
        await CreateRuleAsync(host, "coffee", Coffee, cancellationToken);
        await host.ScanAsync(cancellationToken);

        await ConfirmAsync(host, [Key("bread"), Key("coffee")], "coffee", cancellationToken, merge: true);

        var claimed = await host.ClaimQueuedChangeAsync(cancellationToken);
        Assert.NotNull(claimed);

        await host.RunClaimedChangeAsync(claimed, cancellationToken);

        await host.ReopenClaimedChangeAsync(claimed, cancellationToken);
        await host.RunClaimedChangeAsync(claimed, cancellationToken);

        var rule = Assert.Single(await ListRulesAsync(host, cancellationToken));

        Assert.Equal(Key("coffee"), rule.Key);
        Assert.Equal(Coffee, rule.Marker);
    }

    private async Task<ChangeTestHost> StartAsync(
        CancellationToken cancellationToken,
        params (string Id, string Title)[] tasks)
    {
        var tenant = new FakeTodoTenant();
        tenant.AddList(ListId, "Arbeit");

        foreach (var (id, title) in tasks)
        {
            tenant.AddTask(ListId, id, title);
        }

        return await ChangeTestHost.StartAsync(database.ConnectionString, tenant, cancellationToken);
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

    private static async Task<Guid> ConfirmAsync(
        ChangeTestHost host,
        IReadOnlyList<string> sourceKeys,
        string target,
        CancellationToken cancellationToken,
        bool merge = false)
    {
        var result = await host.PostAsync<ConfirmedChange>(
            "/api/changes",
            new { sourceKeys, targetSpelling = target, confirmMerge = merge },
            cancellationToken);

        Assert.Equal(HttpStatusCode.Accepted, result.StatusCode);

        return result.Body!.Id;
    }

    private static async Task CreateRuleAsync(
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
    }

    private static async Task<IReadOnlyList<MarkerRuleResponse>> ListRulesAsync(
        ChangeTestHost host,
        CancellationToken cancellationToken)
    {
        var listed = await host.GetAsync<MarkerRuleListResponse>("/api/marker-rules", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, listed.StatusCode);

        return listed.Body!.Rules;
    }

    private sealed record ChangePreviewResponse(string Kind, MarkerRulesResponse MarkerRules);

    private sealed record MarkerRulesResponse(
        IReadOnlyList<MarkerRuleSummaryResponse> SourceRules,
        MarkerRuleSummaryResponse? TargetRule,
        string? SurvivingMarker,
        bool RequiresSurvivorChoice);

    private sealed record MarkerRuleSummaryResponse(string Key, string Spelling, string Marker);

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
