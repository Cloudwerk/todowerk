using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TodoWerk.Domain.Markers;
using TodoWerk.Infrastructure.Persistence;
using Xunit;

namespace TodoWerk.IntegrationTests;

/// <summary>
/// Marker Rules over the real pipeline and against SQL Server: created, changed, reordered,
/// deleted and listed — and refused, in the server's own words, wherever two of them would collide.
/// <para>
/// Nothing here writes a task. A rule declares; only an Apply writes (ADR-0014).
/// </para>
/// </summary>
public sealed class MarkerRuleApiTests(SqlServerDatabaseFixture database) : IClassFixture<SqlServerDatabaseFixture>
{
    private const string ListId = "list-arbeit";

    [Fact]
    public async Task ARuleIsCreated_AndComesBackInTheList()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var host = await StartAsync(database, cancellationToken);

        var created = await CreateAsync(host, "bread", "🍞", cancellationToken);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal("🍞", created.Body!.Marker);
        Assert.Equal("bread", created.Body.Spelling);
        Assert.Null(created.Body.RetiredMarker);

        var rules = await ListAsync(host, cancellationToken);

        Assert.Equal("🍞", Assert.Single(rules).Marker);
    }

    /// <summary>
    /// One rule per Hashtag, matched by Hashtag identity — so a second rule written with a
    /// different casing is the same Hashtag and is refused naming the one that exists.
    /// </summary>
    [Fact]
    public async Task ASecondRuleForTheSameHashtag_IsRefusedNamingTheFirst()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var host = await StartAsync(database, cancellationToken);

        await CreateAsync(host, "Bread", "🍞", cancellationToken);

        var second = await CreateAsync(host, "bread", "🥐", cancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("Markers.RuleAlreadyExists", second.Code);
        Assert.Contains("#Bread", second.Detail, StringComparison.Ordinal);
    }

    /// <summary>
    /// One Marker, one Hashtag (ADR-0014). The refusal names the Hashtag holding it, because "that
    /// emoji is taken" on its own leaves somebody hunting through their own rules.
    /// </summary>
    [Fact]
    public async Task AMarkerAlreadyInUse_IsRefusedNamingTheHashtagThatHoldsIt()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var host = await StartAsync(database, cancellationToken);

        await CreateAsync(host, "bread", "🍞", cancellationToken);

        var second = await CreateAsync(host, "loaf", "🍞", cancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("Markers.MarkerInUse", second.Code);
        Assert.Contains("#bread", second.Detail, StringComparison.Ordinal);
    }

    /// <summary>
    /// U+2709 with and without U+FE0F are one emoji to a reader, so they are one Marker to a rule:
    /// the second is refused as in use, naming the Hashtag that holds the first.
    /// </summary>
    [Fact]
    public async Task TheOtherPresentationOfAMarkerInUse_IsRefusedAsInUse()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var host = await StartAsync(database, cancellationToken);

        await CreateAsync(host, "mail", "\u2709\uFE0F", cancellationToken);

        var second = await CreateAsync(host, "post", "\u2709", cancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("Markers.MarkerInUse", second.Code);
        Assert.Contains("#mail", second.Detail, StringComparison.Ordinal);
    }

    /// <summary>A deleted rule frees its Hashtag and its emoji for a new rule.</summary>
    [Fact]
    public async Task ADeletedRule_FreesItsHashtagAndItsMarker()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var host = await StartAsync(database, cancellationToken);

        var bread = await CreateAsync(host, "bread", "🍞", cancellationToken);

        await host.DeleteAsync<MarkerRuleResponse>($"/api/marker-rules/{bread.Body!.Id}", cancellationToken);

        Assert.Equal(HttpStatusCode.Created, (await CreateAsync(host, "bread", "🥐", cancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await CreateAsync(host, "loaf", "🍞", cancellationToken)).StatusCode);
    }

    /// <summary>
    /// A deleted rule is kept for its Marker's sake and for nothing else — so once a standing rule
    /// keeps that Marker known, the deleted one is forgotten, and the set of abandoned Markers is
    /// bounded by the emoji a person has abandoned and not taken up again.
    /// </summary>
    [Fact]
    public async Task ADeletedRuleWhoseMarkerIsTakenUpAgain_IsForgotten()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var host = await StartAsync(database, cancellationToken);

        var bread = await CreateAsync(host, "bread", "🍞", cancellationToken);
        await host.DeleteAsync<MarkerRuleResponse>($"/api/marker-rules/{bread.Body!.Id}", cancellationToken);
        await CreateAsync(host, "loaf", "🍞", cancellationToken);

        await using var scope = host.Scope();
        var context = scope.ServiceProvider.GetRequiredService<TodoWerkDbContext>();

        Assert.Single(await context.Set<MarkerRule>().ToListAsync(cancellationToken));
    }

    /// <summary>
    /// The other half of the same bound. A deleted rule can be keeping two Markers known — its own
    /// and the one it retired — and a standing rule can take up either. A row that kept remembering
    /// the retired one after a standing rule took it up would leave the abandoned set wider than
    /// the bound described above.
    /// <para>
    /// The row itself has to stay: its own Marker is still at the front of titles nothing else
    /// remembers, and deleting it would leave a block reader stopping short of an emoji that is
    /// really there. So the retirement is forgotten and the row is not.
    /// </para>
    /// </summary>
    [Fact]
    public async Task ADeletedRulesRetiredMarkerBeingTakenUp_IsForgottenWithoutLosingTheRule()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var host = await StartAsync(database, cancellationToken);

        // 🍞 written, then swapped for 🥐 — so the rule retires 🍞 — and then deleted.
        var bread = await CreateAsync(host, "bread", "🍞", cancellationToken);

        var patched = await host.PatchAsync<MarkerRuleResponse>(
            $"/api/marker-rules/{bread.Body!.Id}",
            new { marker = "🥐" },
            cancellationToken);

        Assert.Equal(HttpStatusCode.OK, patched.StatusCode);
        await host.DeleteAsync<MarkerRuleResponse>($"/api/marker-rules/{bread.Body.Id}", cancellationToken);

        // Somebody takes up the retired one, not the deleted rule's own.
        Assert.Equal(HttpStatusCode.Created, (await CreateAsync(host, "loaf", "🍞", cancellationToken)).StatusCode);

        await using var scope = host.Scope();
        var context = scope.ServiceProvider.GetRequiredService<TodoWerkDbContext>();
        var rules = await context.Set<MarkerRule>().ToListAsync(cancellationToken);

        Assert.Equal(2, rules.Count);

        var kept = Assert.Single(rules, rule => rule.IsDeleted);

        Assert.Equal("🥐", kept.Marker.ToString());
        Assert.Null(kept.RetiredMarker);
    }

    [Fact]
    public async Task APatchThatAsksForBothAMarkerAndAMove_IsRefused()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var host = await StartAsync(database, cancellationToken);

        var bread = await CreateAsync(host, "bread", "🍞", cancellationToken);

        var patched = await host.PatchAsync<MarkerRuleResponse>(
            $"/api/marker-rules/{bread.Body!.Id}",
            new { marker = "🥐", move = "Down" },
            cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, patched.StatusCode);
        Assert.Equal("Markers.OneChangeAtATime", patched.Code);
    }

    [Theory]
    [InlineData("🍞🥐")]
    [InlineData("x")]
    [InlineData("")]
    public async Task SomethingThatIsNotOneEmoji_IsRefused(string marker)
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var host = await StartAsync(database, cancellationToken);

        var created = await CreateAsync(host, "bread", marker, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, created.StatusCode);
        Assert.Equal("Markers.InvalidMarker", created.Code);
    }

    /// <summary>
    /// One emoji by every other measure, and the one the Hashtag grammar would read as a tag — so
    /// the refusal says that, rather than the "not one emoji" that would be untrue of it.
    /// </summary>
    [Fact]
    public async Task TheHashKeycap_IsRefusedAsAHashtag()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var host = await StartAsync(database, cancellationToken);

        var created = await CreateAsync(host, "bread", "#️⃣", cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, created.StatusCode);
        Assert.Equal("Markers.MarkerOpensAHashtag", created.Code);
    }

    [Theory]
    [InlineData("brot kaufen")]
    [InlineData("#brot")]
    [InlineData("")]
    public async Task AHashtagNameThatDoesNotRoundTrip_IsRefused(string spelling)
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var host = await StartAsync(database, cancellationToken);

        var created = await CreateAsync(host, spelling, "🍞", cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, created.StatusCode);
        Assert.Equal("Markers.InvalidHashtag", created.Code);
    }

    /// <summary>
    /// Changing a Marker records the one blocks are still carrying, so the next Apply can swap it
    /// rather than leaving two emoji where the user meant one.
    /// </summary>
    [Fact]
    public async Task ChangingTheMarker_RecordsTheRetiredOne()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var host = await StartAsync(database, cancellationToken);

        var created = await CreateAsync(host, "bread", "🍞", cancellationToken);
        var changed = await ChangeMarkerAsync(host, created.Body!.Id, "🥐", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        Assert.Equal("🥐", changed.Body!.Marker);
        Assert.Equal("🍞", changed.Body.RetiredMarker);
    }

    /// <summary>
    /// The Marker the rule last had written out, not simply the previous value: 🥐 never reached a
    /// title, so 🍞 is what an Apply has to swap.
    /// </summary>
    [Fact]
    public async Task ChangingTheMarkerTwice_StillRetiresTheOneTitlesCarry()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var host = await StartAsync(database, cancellationToken);

        var created = await CreateAsync(host, "bread", "🍞", cancellationToken);

        await ChangeMarkerAsync(host, created.Body!.Id, "🥐", cancellationToken);
        var second = await ChangeMarkerAsync(host, created.Body.Id, "☕", cancellationToken);

        Assert.Equal("☕", second.Body!.Marker);
        Assert.Equal("🍞", second.Body.RetiredMarker);
    }

    [Fact]
    public async Task ChangingTheMarkerBackAgain_CancelsTheSwap()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var host = await StartAsync(database, cancellationToken);

        var created = await CreateAsync(host, "bread", "🍞", cancellationToken);

        await ChangeMarkerAsync(host, created.Body!.Id, "🥐", cancellationToken);
        var back = await ChangeMarkerAsync(host, created.Body.Id, "🍞", cancellationToken);

        Assert.Equal("🍞", back.Body!.Marker);
        Assert.Null(back.Body.RetiredMarker);
    }

    [Fact]
    public async Task AMarkerAnotherRuleHolds_IsRefusedOnAChangeToo()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var host = await StartAsync(database, cancellationToken);

        await CreateAsync(host, "coffee", "☕", cancellationToken);
        var bread = await CreateAsync(host, "bread", "🍞", cancellationToken);

        var changed = await ChangeMarkerAsync(host, bread.Body!.Id, "☕", cancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
        Assert.Equal("Markers.MarkerInUse", changed.Code);
        Assert.Contains("#coffee", changed.Detail, StringComparison.Ordinal);
    }

    /// <summary>The order shown is the order the block is written in, and it is the server's.</summary>
    [Fact]
    public async Task ARuleMovesUpAndDown()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var host = await StartAsync(database, cancellationToken);

        await CreateAsync(host, "bread", "🍞", cancellationToken);
        var coffee = await CreateAsync(host, "coffee", "☕", cancellationToken);

        var moved = await MoveAsync(host, coffee.Body!.Id, "Up", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
        Assert.Equal(["☕", "🍞"], (await ListAsync(host, cancellationToken)).Select(rule => rule.Marker));

        await MoveAsync(host, coffee.Body.Id, "Down", cancellationToken);

        Assert.Equal(["🍞", "☕"], (await ListAsync(host, cancellationToken)).Select(rule => rule.Marker));
    }

    [Fact]
    public async Task TheFirstRuleCannotMoveUp()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var host = await StartAsync(database, cancellationToken);

        var bread = await CreateAsync(host, "bread", "🍞", cancellationToken);

        var moved = await MoveAsync(host, bread.Body!.Id, "Up", cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, moved.StatusCode);
        Assert.Equal("Markers.RuleCannotMove", moved.Code);
    }

    [Fact]
    public async Task APatchThatAsksForNothing_IsRefused()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var host = await StartAsync(database, cancellationToken);

        var bread = await CreateAsync(host, "bread", "🍞", cancellationToken);

        var patched = await host.PatchAsync<MarkerRuleResponse>(
            $"/api/marker-rules/{bread.Body!.Id}",
            new { },
            cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, patched.StatusCode);
        Assert.Equal("Markers.NothingToUpdate", patched.Code);
    }

    [Fact]
    public async Task ARuleIsDeleted_AndAskingAgainSaysSo()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var host = await StartAsync(database, cancellationToken);

        var bread = await CreateAsync(host, "bread", "🍞", cancellationToken);

        var deleted = await host.DeleteAsync<MarkerRuleResponse>(
            $"/api/marker-rules/{bread.Body!.Id}",
            cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Empty(await ListAsync(host, cancellationToken));

        var again = await host.DeleteAsync<MarkerRuleResponse>(
            $"/api/marker-rules/{bread.Body.Id}",
            cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
        Assert.Equal("Markers.RuleNotFound", again.Code);
    }

    /// <summary>
    /// A rule outlives its Occurrences: the Spelling is kept on the rule for exactly this, so the
    /// rules view can still name the Hashtag when nothing in the index mentions it.
    /// </summary>
    [Fact]
    public async Task ARuleForAHashtagNobodyUses_IsStillListed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var host = await StartAsync(database, cancellationToken);

        await CreateAsync(host, "nobodyUsesThis", "🍞", cancellationToken);

        var rule = Assert.Single(await ListAsync(host, cancellationToken));

        Assert.Equal("nobodyUsesThis", rule.Spelling);
    }

    private static Task<ChangeTestHost> StartAsync(
        SqlServerDatabaseFixture database,
        CancellationToken cancellationToken)
    {
        var tenant = new FakeTodoTenant();
        tenant.AddList(ListId, "Arbeit");

        return ChangeTestHost.StartAsync(database.ConnectionString, tenant, cancellationToken);
    }

    private static Task<ApiResult<MarkerRuleResponse>> CreateAsync(
        ChangeTestHost host,
        string spelling,
        string marker,
        CancellationToken cancellationToken) =>
        host.PostAsync<MarkerRuleResponse>("/api/marker-rules", new { spelling, marker }, cancellationToken);

    private static Task<ApiResult<MarkerRuleResponse>> ChangeMarkerAsync(
        ChangeTestHost host,
        Guid ruleId,
        string marker,
        CancellationToken cancellationToken) =>
        host.PatchAsync<MarkerRuleResponse>($"/api/marker-rules/{ruleId}", new { marker }, cancellationToken);

    private static Task<ApiResult<MarkerRuleResponse>> MoveAsync(
        ChangeTestHost host,
        Guid ruleId,
        string move,
        CancellationToken cancellationToken) =>
        host.PatchAsync<MarkerRuleResponse>($"/api/marker-rules/{ruleId}", new { move }, cancellationToken);

    private static async Task<IReadOnlyList<MarkerRuleResponse>> ListAsync(
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
        int Position);

    private sealed record MarkerRuleListResponse(IReadOnlyList<MarkerRuleResponse> Rules);
}
