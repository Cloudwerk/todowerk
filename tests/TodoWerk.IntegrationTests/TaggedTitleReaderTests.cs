using Microsoft.Extensions.DependencyInjection;
using TodoWerk.Application.Abstractions.Indexing;
using TodoWerk.Domain.Hashtags;
using Xunit;

namespace TodoWerk.IntegrationTests;

/// <summary>
/// The one read behind both marker figures, asked directly — because its whole point is to return
/// fewer rows than there are, and no test of the counts above it can tell a narrowing that works
/// from one that hands back every task.
/// <para>
/// The narrowing is a column rather than a needle list: the scan stores the run of emoji each
/// title opens with, and this asks for the tasks where that is not empty. The comparison is binary
/// because the column is — under the database's default collation an emoji weighs nothing and
/// every run would equal the empty string, so the filter would exclude every row rather than the
/// unmarked ones.
/// </para>
/// </summary>
public sealed class TaggedTitleReaderTests(SqlServerDatabaseFixture database) : IClassFixture<SqlServerDatabaseFixture>
{
    private const string ListId = "list-arbeit";

    [Fact]
    public async Task OnlyTasksThatOpenWithAnEmoji_ComeBack()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tenant = new FakeTodoTenant();
        tenant.AddList(ListId, "Arbeit");
        tenant.AddTask(ListId, "t1", "🍞 Brot kaufen #bread");
        tenant.AddTask(ListId, "t2", "Mehl kaufen #bread");

        // An emoji in the middle of a title is text, and is not a run.
        tenant.AddTask(ListId, "t3", "Kaffee ☕ mahlen #bread");

        await using var host = await ChangeTestHost.StartAsync(database.ConnectionString, tenant, cancellationToken);
        await host.ScanAsync(cancellationToken);

        var marked = await ReadAsync(host, cancellationToken);

        Assert.Equal("🍞", Assert.Single(marked).LeadingEmoji);
    }

    /// <summary>
    /// The run is every emoji at the front, whether or not anybody made a rule about it — the
    /// index writes it long before any question about a person's rules, so it cannot leave out the
    /// ones that are not Markers yet.
    /// </summary>
    [Fact]
    public async Task TheWholeRunComesBack_IncludingEmojiNobodyMadeARuleAbout()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tenant = new FakeTodoTenant();
        tenant.AddList(ListId, "Arbeit");
        tenant.AddTask(ListId, "t1", "🎉🍞 Party #bread");

        await using var host = await ChangeTestHost.StartAsync(database.ConnectionString, tenant, cancellationToken);
        await host.ScanAsync(cancellationToken);

        Assert.Equal("🎉🍞", Assert.Single(await ReadAsync(host, cancellationToken)).LeadingEmoji);
    }

    /// <summary>
    /// Each task once, with the Hashtags it carries hanging off the row — one row per task and not
    /// per (key, task), because the caller counts tasks.
    /// </summary>
    [Fact]
    public async Task ATaskCarryingTwoKeys_ComesBackOnceWithBoth()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tenant = new FakeTodoTenant();
        tenant.AddList(ListId, "Arbeit");
        tenant.AddTask(ListId, "t1", "🍞 Frühstück #bread #Bread #coffee");

        await using var host = await ChangeTestHost.StartAsync(database.ConnectionString, tenant, cancellationToken);
        await host.ScanAsync(cancellationToken);

        var task = Assert.Single(await ReadAsync(host, cancellationToken));

        // #bread and #Bread are one Hashtag, so the key comes back once.
        Assert.Equal(
            [HashtagKey.Fold("bread"), HashtagKey.Fold("coffee")],
            task.Keys.Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// The row the stale figure lives on: a task carrying a Marker and no Hashtag at all. A join on
    /// Occurrences is exactly the filter that would hide the population being measured.
    /// </summary>
    [Fact]
    public async Task ATaskWithAnEmojiAndNoHashtag_ComesBackWithNoKeys()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tenant = new FakeTodoTenant();
        tenant.AddList(ListId, "Arbeit");
        tenant.AddTask(ListId, "t1", "🍞 Brot kaufen");

        await using var host = await ChangeTestHost.StartAsync(database.ConnectionString, tenant, cancellationToken);
        await host.ScanAsync(cancellationToken);

        var task = Assert.Single(await ReadAsync(host, cancellationToken));

        Assert.Equal("🍞", task.LeadingEmoji);
        Assert.Empty(task.Keys);
    }

    /// <summary>
    /// The run follows the title. A task retitled between scans has its run rewritten with it, or
    /// coverage would go on counting a Marker the person has taken off by hand.
    /// </summary>
    [Fact]
    public async Task RetitlingATask_RewritesItsRun()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tenant = new FakeTodoTenant();
        tenant.AddList(ListId, "Arbeit");
        tenant.AddTask(ListId, "t1", "🍞 Brot kaufen #bread");

        await using var host = await ChangeTestHost.StartAsync(database.ConnectionString, tenant, cancellationToken);
        await host.ScanAsync(cancellationToken);

        Assert.Equal("🍞", Assert.Single(await ReadAsync(host, cancellationToken)).LeadingEmoji);

        tenant.RetitleTask(ListId, "t1", "Brot kaufen #bread");
        await host.ScanAsync(cancellationToken);

        Assert.Empty(await ReadAsync(host, cancellationToken));
    }

    /// <summary>
    /// A list nobody has read end to end contributes nothing, the same rule the count beside this
    /// follows — so the two figures are drawn from one set of tasks (ADR-0003).
    /// </summary>
    [Fact]
    public async Task ATaskInAListNeverReadEndToEnd_DoesNotComeBack()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tenant = new FakeTodoTenant();
        tenant.AddList(ListId, "Arbeit");
        tenant.AddTask(ListId, "t1", "🍞 Brot kaufen #bread");

        await using var host = await ChangeTestHost.StartAsync(database.ConnectionString, tenant, cancellationToken);

        // No scan at all, so the list has never been read end to end.
        Assert.Empty(await ReadAsync(host, cancellationToken));
    }

    private static async Task<IReadOnlyList<MarkedTitle>> ReadAsync(
        ChangeTestHost host,
        CancellationToken cancellationToken)
    {
        await using var scope = host.Scope();

        return await scope.ServiceProvider.GetRequiredService<ITaggedTitleReader>()
            .ReadMarkedTasksAsync(ChangeTestHost.User, cancellationToken);
    }
}
