using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Identity.Client;
using Microsoft.Identity.Web.TokenCacheProviders;
using TodoWerk.Domain.Failures;
using TodoWerk.Domain.Indexing;
using TodoWerk.Infrastructure.Authentication;
using TodoWerk.Infrastructure.Indexing;
using TodoWerk.Infrastructure.Indexing.Persistence;
using TodoWerk.Infrastructure.Persistence;
using Xunit;
using TodoWerk.Application.Abstractions.Authentication;

namespace TodoWerk.IntegrationTests;

/// <summary>
/// The scan, against real SQL Server and a fake mailbox: a first pass that pages every task into
/// the index, a delta pass that applies only what changed, and the two things Graph does to a scan
/// in the wild — throttling it, and expiring its delta token.
/// <para>
/// The token comes out of the durable cache with no HTTP request in sight, which is what makes any
/// of this possible in the background. No test contacts a live tenant (CONTRIBUTING § Testing).
/// </para>
/// </summary>
public sealed class IndexScanTests(SqlServerDatabaseFixture database)
    : IClassFixture<SqlServerDatabaseFixture>, IDisposable
{
    private const string ListId = "list-arbeit";
    private const string OtherListId = "list-privat";

    /// <summary>
    /// Two Graph task ids differing in one character's case, in the shape Exchange issues them:
    /// a long shared prefix and a tail that varies.
    /// </summary>
    private const string TaskId = "AAMkAHN5bnRoZXRpYy10YXNrLWlk";

    private const string TaskIdDifferingInCase = "AAMkAHN5bnRoZXRpYy10YXNrLWlK";

    /// <summary>Two list ids of the same shape. Unlike task ids these travel in the request path.</summary>
    private const string ListIdWithCasedTail = "AAMkAGxpc3RlAAWPkADl";

    private const string ListIdDifferingInCase = "AAMkAGxpc3RlAAWPkADL";

    /// <summary>
    /// The projection of Outlook-flagged mail, under an id of the shape Exchange issues for it:
    /// base64url with the padding still on it, so the request path this list is read through
    /// carries a percent escape rather than the tidy identifier a test would otherwise invent.
    /// Synthetic: it decodes to plain text, and no real mailbox's ids belong in a public repository.
    /// </summary>
    private const string FlaggedEmailsListId =
        "AAMkAHN5bnRoZXRpYy1mbGFnZ2VkLWVtYWlscy1saXN0LWlkLW1hZGUtdXAtZm9yLXRlc3RzLW9ubHk=";

    /// <summary>
    /// The same pair of ids again, inside the flagged-mail list: on a real mailbox that is the list
    /// big enough to hold both.
    /// </summary>
    private const string FlaggedTaskId = "AAMkAGZsYWdnZWQAAAWPkADlAAA=";

    private const string FlaggedTaskIdDifferingInCase = "AAMkAGZsYWdnZWQAAAWPkADLAAA=";

    private readonly string _keyRingPath = Directory.CreateDirectory(Path.Combine(
        Path.GetTempPath(),
        "todowerk-index-scan-tests",
        Guid.NewGuid().ToString("N"))).FullName;

    public void Dispose()
    {
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
    /// The worker itself, which every other test here deliberately leaves out of the host: a row
    /// queued and nobody touching it, drained by the real background service on its own timer.
    /// Without this the poll loop, the claim it makes and the runner it hands the row to are
    /// wired together by nothing but reading.
    /// </summary>
    [Fact]
    public async Task TheBackgroundWorker_DrainsAQueuedScanWithoutBeingAsked()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tenant = new FakeTodoTenant();
        tenant.AddList(ListId, "Arbeit");
        tenant.AddTask(ListId, "t1", "Angebot schreiben #Kunde");

        await using var host = await StartHostAsync(tenant, cancellationToken, withWorker: true);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<TodoWerkDbContext>();

            context.Add(IndexScan.Request(
                TodoWerkWebApplicationFactory.TenantId,
                FakeEntraAndGraphHandler.UserObjectId,
                IndexScanMode.Full,
                taskListId: null,
                DateTimeOffset.UtcNow));

            await context.SaveChangesAsync(cancellationToken);
        }

        var completed = await WaitForAsync(
            host,
            async context => await context.Set<IndexScan>()
                .AsNoTracking()
                .AnyAsync(scan => scan.State == IndexScanState.Completed, cancellationToken),
            cancellationToken);

        Assert.True(completed, "the worker never drained the queued scan");

        await using var assertions = host.Services.CreateAsyncScope();
        var indexed = assertions.ServiceProvider.GetRequiredService<TodoWerkDbContext>();

        Assert.Equal(1, await indexed.Set<IndexedTask>().CountAsync(cancellationToken));
        Assert.Equal("Kunde", (await indexed.Set<HashtagOccurrence>().SingleAsync(cancellationToken)).Spelling);
    }

    /// <summary>
    /// A list that <c>GET /me/todo/lists</c> returned, and whose read then answers 404, is not a
    /// list that was deleted: the listing seconds earlier is the only reason it has a state row at
    /// all. The write path's sentence for a 404 says the item is gone, which sends
    /// whoever reads it looking for a list that is still there, and a code of
    /// <see cref="FailureCode.Unknown" /> is one the Workbench cannot switch on.
    /// </summary>
    [Fact]
    public async Task AListGraphListsButWillNotRead_IsNotReportedAsDeleted()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tenant = new FakeTodoTenant();
        // Nothing is added to this list: the read 404s before a task could travel, so a task
        // here would read as load-bearing and is not.
        tenant.AddList(ListId, "Arbeit");
        tenant.MakeListUnreadable(ListId);

        // A healthy list beside it. Not part of the repro — a guard, because the cheapest wrong
        // fix for this is one that fails the whole scan on a 404.
        tenant.AddList(OtherListId, "Privat");
        tenant.AddTask(OtherListId, "task-2", "Einkaufen #privat");

        await using var host = await StartHostAsync(tenant, cancellationToken);
        await RunScanAsync(host, IndexScanMode.Full, cancellationToken);

        await using var scope = host.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TodoWerkDbContext>();

        var states = await context.Set<TaskListIndexState>().ToListAsync(cancellationToken);

        // The row is still here, which is the whole point: ReconcileListsAsync keeps it because
        // Graph still lists the list.
        var unreadable = Assert.Single(states, state => state.TaskListId == ListId);
        Assert.Equal(ListScanState.Failed, unreadable.State);

        // Said as the sentence a person actually reads, not as a code: this is the whole defect.
        // The write path's wording would tell them the list was deleted and send them looking
        // for it.
        Assert.NotEqual(
            "Microsoft To Do no longer has this item.",
            unreadable.FailureReason);

        Assert.Equal(
            "Microsoft To Do returned this list, then could not find it when TodoWerk asked for "
            + "its tasks. TodoWerk will try again.",
            unreadable.FailureReason);

        // Unknown means "nobody worded this, go to the logs", and a clean 404 logs nothing there.
        Assert.Equal(FailureCode.NotFound, unreadable.FailureCode);

        var healthy = Assert.Single(states, state => state.TaskListId == OtherListId);
        Assert.Equal(ListScanState.Indexed, healthy.State);
    }

    /// <summary>
    /// The other half of the same rule: a full read that comes back asking for a resync as well.
    /// The runner records a sentence of its own here rather than leaving the list on "Scanning"
    /// forever, and the code beside it must not be <c>Unknown</c>, which would point whoever reads
    /// it at logs that hold nothing: this path logs at Information, and Graph named what it did.
    /// </summary>
    [Fact]
    public async Task AListGraphKeepsAskingToResync_IsRecordedAsFailedWithAGraphCode()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tenant = new FakeTodoTenant();

        // Nothing on it for the same reason as above: the read never gets far enough to carry one.
        tenant.AddList(ListId, "Arbeit");
        tenant.AlwaysAskForResync(ListId);

        tenant.AddList(OtherListId, "Privat");
        tenant.AddTask(OtherListId, "task-2", "Einkaufen #privat");

        await using var host = await StartHostAsync(tenant, cancellationToken);
        await RunScanAsync(host, IndexScanMode.Full, cancellationToken);

        await using var scope = host.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TodoWerkDbContext>();

        var states = await context.Set<TaskListIndexState>().ToListAsync(cancellationToken);

        var stuck = Assert.Single(states, state => state.TaskListId == ListId);
        Assert.Equal(ListScanState.Failed, stuck.State);

        Assert.Equal(
            "Microsoft To Do keeps asking for a fresh read of this list. TodoWerk will try again.",
            stuck.FailureReason);

        Assert.Equal(FailureCode.Unavailable, stuck.FailureCode);

        var healthy = Assert.Single(states, state => state.TaskListId == OtherListId);
        Assert.Equal(ListScanState.Indexed, healthy.State);
    }

    /// <summary>Polls until the condition holds or the patience runs out.</summary>
    private static async Task<bool> WaitForAsync(
        TodoWerkWebApplicationFactory host,
        Func<TodoWerkDbContext, Task<bool>> condition,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);

        while (DateTimeOffset.UtcNow < deadline)
        {
            await using var scope = host.Services.CreateAsyncScope();

            if (await condition(scope.ServiceProvider.GetRequiredService<TodoWerkDbContext>()))
            {
                return true;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
        }

        return false;
    }

    /// <summary>
    /// The whole of a first scan in one pass: every list enumerated, every task paged, Hashtags
    /// extracted and persisted, progress and completion recorded per list.
    /// </summary>
    [Fact]
    public async Task AFirstScan_PagesEveryTaskIntoTheIndex()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tenant = new FakeTodoTenant();
        tenant.AddList(ListId, "Arbeit");
        tenant.AddTask(ListId, "t1", "Angebot schreiben #Kunde");
        tenant.AddTask(ListId, "t2", "Angebot prüfen #kunde #dringend");
        tenant.AddTask(ListId, "t3", "Nichts zu sehen");
        tenant.AddList(OtherListId, "Privat");
        tenant.AddTask(OtherListId, "t4", "Einkaufen #haushalt");

        await using var host = await StartHostAsync(tenant, cancellationToken);

        await RunScanAsync(host, IndexScanMode.Full, cancellationToken);

        await using var scope = host.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TodoWerkDbContext>();

        // Three pages for the first list at a page size of two, which is the point of the counts.
        Assert.Equal(4, await context.Set<IndexedTask>().CountAsync(cancellationToken));

        var occurrences = await context.Set<HashtagOccurrence>().ToListAsync(cancellationToken);

        Assert.Equal(4, occurrences.Count);
        Assert.Equal(
            ["Kunde", "dringend", "haushalt", "kunde"],
            occurrences.Select(occurrence => occurrence.Spelling).Order(StringComparer.Ordinal));

        // #Kunde and #kunde are two Spellings of one Hashtag (ADR-0005), and the index has to
        // carry both while agreeing they are one.
        Assert.Equal(
            2,
            occurrences.Count(occurrence => string.Equals(occurrence.Key, "KUNDE", StringComparison.Ordinal)));

        var states = await context.Set<TaskListIndexState>().ToListAsync(cancellationToken);

        Assert.Equal(2, states.Count);
        Assert.All(states, state => Assert.Equal(ListScanState.Indexed, state.State));
        Assert.All(states, state => Assert.True(state.IsFullyIndexed));
        Assert.All(states, state => Assert.True(state.CanSyncIncrementally));

        var arbeit = Assert.Single(states, state => state.TaskListId == ListId);
        Assert.Equal("Arbeit", arbeit.DisplayName);
        Assert.Equal(3, arbeit.TasksIndexed);
    }

    /// <summary>
    /// A delta pass applies what changed and nothing else — a new task, a retitled one whose
    /// Occurrences have to be replaced rather than added to, and a deletion.
    /// </summary>
    [Fact]
    public async Task ADeltaSync_AppliesCreationsChangesAndDeletions()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tenant = new FakeTodoTenant();
        tenant.AddList(ListId, "Arbeit");
        tenant.AddTask(ListId, "t1", "Angebot schreiben #Kunde");
        tenant.AddTask(ListId, "t2", "Wird gelöscht #weg");

        await using var host = await StartHostAsync(tenant, cancellationToken);
        await RunScanAsync(host, IndexScanMode.Full, cancellationToken);

        tenant.RetitleTask(ListId, "t1", "Angebot schreiben #Kunde #fertig");
        tenant.DeleteTask(ListId, "t2");
        tenant.AddTask(ListId, "t3", "Neue Aufgabe #neu");

        await RunScanAsync(host, IndexScanMode.Delta, cancellationToken);

        await using var scope = host.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TodoWerkDbContext>();

        var titles = await context.Set<IndexedTask>()
            .Select(task => task.Title)
            .ToListAsync(cancellationToken);

        Assert.Equal(
            ["Angebot schreiben #Kunde #fertig", "Neue Aufgabe #neu"],
            titles.Order(StringComparer.Ordinal));

        var spellings = await context.Set<HashtagOccurrence>()
            .Select(occurrence => occurrence.Spelling)
            .ToListAsync(cancellationToken);

        // "weg" left with its task; "Kunde" was not duplicated by the retitle; "fertig" arrived.
        Assert.Equal(["Kunde", "fertig", "neu"], spellings.Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// Graph expires delta tokens, and the remedy is the scan's own: drop the token, read the list
    /// in full, carry on. Nobody is asked to intervene.
    /// </summary>
    [Fact]
    public async Task WhenGraphExpiresTheDeltaToken_TheListIsReadInFullWithoutAnyoneAsking()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tenant = new FakeTodoTenant();
        tenant.AddList(ListId, "Arbeit");
        tenant.AddTask(ListId, "t1", "Erste Aufgabe #alpha");

        await using var host = await StartHostAsync(tenant, cancellationToken);
        await RunScanAsync(host, IndexScanMode.Full, cancellationToken);

        // Added while the token is being expired: only a full re-read can find it, because the
        // delta request that would have reported it is the one Graph refuses.
        tenant.AddTask(ListId, "t2", "Zweite Aufgabe #beta");
        tenant.ExpireDeltaToken(ListId);

        await RunScanAsync(host, IndexScanMode.Delta, cancellationToken);

        await using var scope = host.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TodoWerkDbContext>();

        Assert.Equal(2, await context.Set<IndexedTask>().CountAsync(cancellationToken));

        var state = await context.Set<TaskListIndexState>().SingleAsync(cancellationToken);

        Assert.Equal(ListScanState.Indexed, state.State);
        Assert.True(state.CanSyncIncrementally);
        Assert.Null(state.FailureReason);
    }

    /// <summary>
    /// Throttling is a normal outcome, not a failure: the scan waits and finishes.
    /// </summary>
    [Fact]
    public async Task AThrottledList_IsRetriedRatherThanAbandoned()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tenant = new FakeTodoTenant();
        tenant.AddList(ListId, "Arbeit");
        tenant.AddTask(ListId, "t1", "Angebot #kunde");
        tenant.ThrottleList(ListId, times: 2);

        await using var host = await StartHostAsync(tenant, cancellationToken);

        await RunScanAsync(host, IndexScanMode.Full, cancellationToken);

        Assert.Equal(2, tenant.ThrottledRequests[ListId]);

        await using var scope = host.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TodoWerkDbContext>();
        var state = await context.Set<TaskListIndexState>().SingleAsync(cancellationToken);

        Assert.Equal(ListScanState.Indexed, state.State);
        Assert.Equal(1, await context.Set<IndexedTask>().CountAsync(cancellationToken));
    }

    /// <summary>
    /// A list that fails, fails alone. One list outlasting the retry budget must not cost the user
    /// every other list in the scan.
    /// </summary>
    [Fact]
    public async Task AListThatKeepsFailing_DoesNotTakeTheOtherListsDownWithIt()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tenant = new FakeTodoTenant();
        tenant.AddList(ListId, "Arbeit");
        tenant.AddTask(ListId, "t1", "Angebot #kunde");
        tenant.AddList(OtherListId, "Privat");
        tenant.AddTask(OtherListId, "t2", "Einkaufen #haushalt");
        tenant.ThrottleList(ListId, times: 99);

        await using var host = await StartHostAsync(tenant, cancellationToken, maxThrottleRetries: 1);

        await RunScanAsync(host, IndexScanMode.Full, cancellationToken);

        await using var scope = host.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TodoWerkDbContext>();
        var states = await context.Set<TaskListIndexState>().ToListAsync(cancellationToken);

        var failed = Assert.Single(states, state => state.TaskListId == ListId);
        Assert.Equal(ListScanState.Failed, failed.State);
        Assert.NotNull(failed.FailureReason);
        Assert.False(failed.IsFullyIndexed);

        var succeeded = Assert.Single(states, state => state.TaskListId == OtherListId);
        Assert.Equal(ListScanState.Indexed, succeeded.State);
        Assert.True(succeeded.IsFullyIndexed);

        // And the healthy list's Hashtags are in the index, which is the point of failing alone.
        var occurrence = await context.Set<HashtagOccurrence>().SingleAsync(cancellationToken);
        Assert.Equal("haushalt", occurrence.Spelling);
    }

    /// <summary>
    /// A list deleted in To Do takes its Occurrences with it, or the inventory keeps counting
    /// tags nobody can reach any more.
    /// </summary>
    [Fact]
    public async Task WhenAListIsDeletedInToDo_ItsTasksLeaveTheIndex()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tenant = new FakeTodoTenant();
        tenant.AddList(ListId, "Arbeit");
        tenant.AddTask(ListId, "t1", "Angebot #kunde");
        tenant.AddList(OtherListId, "Privat");
        tenant.AddTask(OtherListId, "t2", "Einkaufen #haushalt");

        await using var host = await StartHostAsync(tenant, cancellationToken);
        await RunScanAsync(host, IndexScanMode.Full, cancellationToken);

        tenant.RemoveList(ListId);

        await RunScanAsync(host, IndexScanMode.Delta, cancellationToken);

        await using var scope = host.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TodoWerkDbContext>();

        Assert.Equal(1, await context.Set<TaskListIndexState>().CountAsync(cancellationToken));
        var task = await context.Set<IndexedTask>().SingleAsync(cancellationToken);
        Assert.Equal(OtherListId, task.TaskListId);

        var occurrence = await context.Set<HashtagOccurrence>().SingleAsync(cancellationToken);
        Assert.Equal("haushalt", occurrence.Spelling);
    }

    /// <summary>
    /// A scan that finds nothing still finishes: the list is Indexed with zero tasks, not stuck
    /// looking like it never ran.
    /// </summary>
    [Fact]
    public async Task AnEmptyList_CompletesRatherThanStayingUnscanned()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tenant = new FakeTodoTenant();
        tenant.AddList(ListId, "Arbeit");

        await using var host = await StartHostAsync(tenant, cancellationToken);

        await RunScanAsync(host, IndexScanMode.Full, cancellationToken);

        await using var scope = host.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TodoWerkDbContext>();
        var state = await context.Set<TaskListIndexState>().SingleAsync(cancellationToken);

        Assert.Equal(ListScanState.Indexed, state.State);
        Assert.Equal(0, state.TasksIndexed);
        Assert.True(state.IsFullyIndexed);
    }

    /// <summary>
    /// A move is the destination's addition and the source's tombstone, and Graph promises no
    /// order between the two lists' deltas. The dangerous order is destination first: the row
    /// already says the new list when the old list's tombstone lands, and honouring that
    /// tombstone would delete a task that still exists — silently, until the next full re-scan.
    /// </summary>
    [Fact]
    public async Task AMovedTask_SurvivesTheSourceListsLateTombstone()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tenant = new FakeTodoTenant();

        // Named so the destination sorts first: lists are scanned in display-name order, which
        // makes this test the dangerous order by construction.
        tenant.AddList(ListId, "Arbeit");
        tenant.AddList(OtherListId, "Zulieferer");
        tenant.AddTask(OtherListId, "task-wandert", "Umzug planen #projekt");

        await using var host = await StartHostAsync(tenant, cancellationToken);
        await RunScanAsync(host, IndexScanMode.Full, cancellationToken);

        tenant.DeleteTask(OtherListId, "task-wandert");
        tenant.AddTask(ListId, "task-wandert", "Umzug planen #projekt");

        await RunScanAsync(host, IndexScanMode.Delta, cancellationToken);

        await using var scope = host.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TodoWerkDbContext>();

        var task = await context.Set<IndexedTask>().SingleAsync(cancellationToken);
        Assert.Equal(ListId, task.TaskListId);

        var occurrence = await context.Set<HashtagOccurrence>().SingleAsync(cancellationToken);
        Assert.Equal("projekt", occurrence.Spelling);
    }

    /// <summary>
    /// A name longer than the columns is not a Hashtag — and, before the extractor refused it,
    /// it was a poison pill: the page's save failed wholesale and the scan wedged on it forever.
    /// </summary>
    [Fact]
    public async Task AnOversizedHashtag_IsSkippedRatherThanFailingTheScan()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tenant = new FakeTodoTenant();
        tenant.AddList(ListId, "Arbeit");
        tenant.AddTask(ListId, "task-1", $"Rechnung #{new string('a', 300)} #ok");

        await using var host = await StartHostAsync(tenant, cancellationToken);
        await RunScanAsync(host, IndexScanMode.Full, cancellationToken);

        await using var scope = host.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TodoWerkDbContext>();

        Assert.Equal(1, await context.Set<IndexedTask>().CountAsync(cancellationToken));

        var occurrence = await context.Set<HashtagOccurrence>().SingleAsync(cancellationToken);
        Assert.Equal("ok", occurrence.Spelling);
    }

    /// <summary>
    /// The recovery path itself: a list whose page cannot be saved — here, a Graph id too long
    /// for its column — fails alone, with the failure recorded, while the other list completes
    /// and so does the scan. Before the change tracker was cleared in the handler, recording the
    /// failure replayed the very save that had just failed, and the scan stuck.
    /// </summary>
    [Fact]
    public async Task AListWhosePageCannotBeSaved_FailsAloneAndIsRecordedAsFailed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tenant = new FakeTodoTenant();
        tenant.AddList(ListId, "Arbeit");
        tenant.AddTask(ListId, new string('x', 600), "Kaputt #kaputt");
        tenant.AddList(OtherListId, "Privat");
        tenant.AddTask(OtherListId, "task-2", "Einkaufen #privat");

        await using var host = await StartHostAsync(tenant, cancellationToken);
        await RunScanAsync(host, IndexScanMode.Full, cancellationToken);

        await using var scope = host.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TodoWerkDbContext>();

        var states = await context.Set<TaskListIndexState>().ToListAsync(cancellationToken);

        var broken = Assert.Single(states, state => state.TaskListId == ListId);
        Assert.Equal(ListScanState.Failed, broken.State);

        // Nothing here failed to read. Graph answered, and the save is what broke, so the reason
        // must not send whoever sees it to Graph, to permissions, or to the list. This is the one
        // branch where the cause is genuinely unknown, and it has to say so.
        Assert.Equal(
            "TodoWerk hit an unexpected error while scanning this list. It will try again.",
            broken.FailureReason);

        var healthy = Assert.Single(states, state => state.TaskListId == OtherListId);
        Assert.Equal(ListScanState.Indexed, healthy.State);

        var occurrence = await context.Set<HashtagOccurrence>().SingleAsync(cancellationToken);
        Assert.Equal("privat", occurrence.Spelling);
    }

    /// <summary>
    /// Graph does not promise a page mentions a task once. Two sightings of a task already in the
    /// index must leave the Occurrences of the last title and no others — both sets inserted
    /// would count one task twice, and the count is what picks the Canonical Spelling.
    /// </summary>
    [Fact]
    public async Task ATaskAppearingTwiceInOnePage_KeepsOnlyTheLastTitlesHashtags()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tenant = new FakeTodoTenant();
        tenant.AddList(ListId, "Arbeit");
        tenant.AddTask(ListId, "task-1", "Angebot #alpha");

        await using var host = await StartHostAsync(tenant, cancellationToken);
        await RunScanAsync(host, IndexScanMode.Full, cancellationToken);

        // The same task twice in one delta page, with different titles.
        tenant.AddTask(ListId, "task-1", "Angebot #beta");
        tenant.AddTask(ListId, "task-1", "Angebot #gamma");

        await RunScanAsync(host, IndexScanMode.Delta, cancellationToken);

        await using var scope = host.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TodoWerkDbContext>();

        var occurrence = await context.Set<HashtagOccurrence>().SingleAsync(cancellationToken);
        Assert.Equal("gamma", occurrence.Spelling);
    }

    /// <summary>
    /// Microsoft Graph issues task ids as case-sensitive base64, so two live tasks in one list can
    /// differ by nothing but a letter's case. Under the database's default case-insensitive
    /// collation the unique index cannot tell them apart: the second insert comes back as a
    /// duplicate key, the whole list fails, and every retry reads the same page and fails
    /// identically — the list never finishes indexing again.
    /// </summary>
    [Fact]
    public async Task TwoTasksWhoseGraphIdsDifferOnlyInCase_AreBothIndexed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tenant = new FakeTodoTenant();
        tenant.AddList(ListId, "Arbeit");
        tenant.AddTask(ListId, TaskId, "Angebot schreiben #kunde");
        tenant.AddTask(ListId, TaskIdDifferingInCase, "Angebot prüfen #kunde");

        await using var host = await StartHostAsync(tenant, cancellationToken);
        await RunScanAsync(host, IndexScanMode.Full, cancellationToken);

        await using var scope = host.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TodoWerkDbContext>();

        var stored = await context.Set<IndexedTask>()
            .Select(task => task.GraphTaskId)
            .ToListAsync(cancellationToken);

        // Compared ordinally on this side too, or the assertion would pass on the very confusion
        // it is here to catch.
        Assert.Equal(2, stored.Count);
        Assert.Contains(TaskId, stored, StringComparer.Ordinal);
        Assert.Contains(TaskIdDifferingInCase, stored, StringComparer.Ordinal);

        var state = await context.Set<TaskListIndexState>().SingleAsync(cancellationToken);

        Assert.Equal(ListScanState.Indexed, state.State);
        Assert.Null(state.FailureReason);

        // Two tasks, two Occurrences of one Hashtag — the count the Canonical Spelling is picked
        // by, and the thing a swallowed task takes with it.
        Assert.Equal(2, await context.Set<HashtagOccurrence>().CountAsync(cancellationToken));
    }

    /// <summary>
    /// The other half of the same rule, which only shows once one of the two ids is already
    /// stored: the page's rows are pre-loaded by a <c>Contains</c> the database evaluates and read
    /// back through a dictionary C# keys ordinally. Under a case-insensitive column the query
    /// answers with the stored task, the lookup misses it, and the scan concludes the arriving task
    /// is new — a wrong comparison that only becomes visible as a failed insert.
    /// </summary>
    [Fact]
    public async Task ATaskWhoseIdMatchesAStoredOneOnlyCaseInsensitively_IsIndexedAsANewTask()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tenant = new FakeTodoTenant();
        tenant.AddList(ListId, "Arbeit");
        tenant.AddTask(ListId, TaskId, "Angebot schreiben #kunde");

        await using var host = await StartHostAsync(tenant, cancellationToken);
        await RunScanAsync(host, IndexScanMode.Full, cancellationToken);

        // Created after the first pass, so the delta page carries it while its near-twin is
        // already a row in the index.
        tenant.AddTask(ListId, TaskIdDifferingInCase, "Angebot prüfen #kunde");

        await RunScanAsync(host, IndexScanMode.Delta, cancellationToken);

        await using var scope = host.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TodoWerkDbContext>();

        var titles = await context.Set<IndexedTask>()
            .Select(task => task.Title)
            .ToListAsync(cancellationToken);

        // Two rows, not one overwritten by the other: the arriving task is a different task.
        Assert.Equal(["Angebot prüfen #kunde", "Angebot schreiben #kunde"], titles.Order(StringComparer.Ordinal));

        var state = await context.Set<TaskListIndexState>().SingleAsync(cancellationToken);

        Assert.Equal(ListScanState.Indexed, state.State);
        Assert.Null(state.FailureReason);
    }

    /// <summary>
    /// The same exposure one level up: <c>UX_TaskListIndexStates_Tenant_User_TaskList</c> over ids
    /// Graph issues the same way. Here the collision is worse than a failed list — reconciliation
    /// runs before any list is scanned, so the duplicate key takes the whole scan down with it.
    /// </summary>
    [Fact]
    public async Task TwoListsWhoseIdsDifferOnlyInCase_AreBothTracked()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tenant = new FakeTodoTenant();
        tenant.AddList(ListIdWithCasedTail, "Arbeit");
        tenant.AddList(ListIdDifferingInCase, "Privat");
        tenant.AddTask(ListIdWithCasedTail, "task-1", "Angebot schreiben #kunde");
        tenant.AddTask(ListIdDifferingInCase, "task-2", "Einkaufen #haushalt");

        await using var host = await StartHostAsync(tenant, cancellationToken);
        await RunScanAsync(host, IndexScanMode.Full, cancellationToken);

        await using var scope = host.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TodoWerkDbContext>();

        var states = await context.Set<TaskListIndexState>().ToListAsync(cancellationToken);

        Assert.Equal(2, states.Count);
        Assert.All(states, state => Assert.Equal(ListScanState.Indexed, state.State));

        var spellings = await context.Set<HashtagOccurrence>()
            .Select(occurrence => occurrence.Spelling)
            .ToListAsync(cancellationToken);

        Assert.Equal(["haushalt", "kunde"], spellings.Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// Flagged Emails is read like any other list; this pins paging and delta on a projection list.
    /// <para>
    /// What singles the list out on a real mailbox is its size, not its kind: it is often the only
    /// list that pages at all, and the one big enough to hold a pair of ids that differ in one
    /// character's case. So the list here sits under an id of the shape Exchange issues for it,
    /// pages, and carries the mail subjects such a list carries and the colliding pair of ids.
    /// </para>
    /// </summary>
    [Fact]
    public async Task TheFlaggedEmailsList_IsPagedIntoTheIndexLikeAnyOtherList()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tenant = new FakeTodoTenant();

        tenant.AddList(FlaggedEmailsListId, "Flagged Emails", "flaggedEmails");
        tenant.AddTask(FlaggedEmailsListId, FlaggedTaskId, "RE: Angebot Q3 #Kunde");
        tenant.AddTask(FlaggedEmailsListId, FlaggedTaskIdDifferingInCase, "AW: Angebot Q3 #kunde");
        tenant.AddTask(FlaggedEmailsListId, "flagged-3", "Rechnung 2026-0815 #buchhaltung");
        tenant.AddTask(FlaggedEmailsListId, "flagged-4", "Kein Betreff");
        tenant.AddTask(FlaggedEmailsListId, "flagged-5", "Termin bestätigen #dringend");

        // An ordinary list beside it, because the failure to guard against is not a scan
        // that fails: it is one list missing from an inventory the rest of which looks complete.
        tenant.AddList(ListId, "Arbeit");
        tenant.AddTask(ListId, "t1", "Angebot schreiben #Kunde");

        await using var host = await StartHostAsync(tenant, cancellationToken);
        await RunScanAsync(host, IndexScanMode.Full, cancellationToken);

        await using var scope = host.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TodoWerkDbContext>();

        var flagged = await context.Set<TaskListIndexState>()
            .SingleAsync(state => state.TaskListId == FlaggedEmailsListId, cancellationToken);

        // Said first and separately: a list that failed carries a failure sentence, and reading
        // it is worth more to whoever is standing here next than a count that is off.
        Assert.Null(flagged.FailureReason);
        Assert.Equal(ListScanState.Indexed, flagged.State);
        Assert.True(flagged.IsFullyIndexed);

        // Five tasks over three pages at the fake's page size of two, so the delta link at the
        // end is the one the last page carried rather than the first page's absence of one.
        Assert.True(flagged.CanSyncIncrementally);
        Assert.Equal(5, flagged.TasksIndexed);

        var stored = await context.Set<IndexedTask>()
            .Where(task => task.TaskListId == FlaggedEmailsListId)
            .Select(task => task.GraphTaskId)
            .ToListAsync(cancellationToken);

        // Compared ordinally on this side too, or the assertion would pass on the very confusion
        // it is here to catch.
        Assert.Contains(FlaggedTaskId, stored, StringComparer.Ordinal);
        Assert.Contains(FlaggedTaskIdDifferingInCase, stored, StringComparer.Ordinal);

        var spellings = await context.Set<HashtagOccurrence>()
            .Select(occurrence => occurrence.Spelling)
            .ToListAsync(cancellationToken);

        // What matters is the inventory rather than the scan: the Hashtags written in flagged
        // mail are in it, alongside the one from the ordinary list.
        Assert.Equal(
            ["Kunde", "Kunde", "buchhaltung", "dringend", "kunde"],
            spellings.Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// The second pass over the projection list, made with the delta link the first one stored.
    /// A mail flagged since arrives, one unflagged since leaves, and the list's own freshness
    /// moves — none of which is reachable at all if the endpoint refuses a projection list.
    /// </summary>
    [Fact]
    public async Task ADeltaSyncOfTheFlaggedEmailsList_AppliesWhatChangedSince()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tenant = new FakeTodoTenant();

        tenant.AddList(FlaggedEmailsListId, "Flagged Emails", "flaggedEmails");
        tenant.AddTask(FlaggedEmailsListId, FlaggedTaskId, "RE: Angebot Q3 #Kunde");
        tenant.AddTask(FlaggedEmailsListId, "flagged-3", "Rechnung 2026-0815 #buchhaltung");

        await using var host = await StartHostAsync(tenant, cancellationToken);
        await RunScanAsync(host, IndexScanMode.Full, cancellationToken);

        // What the list does between passes: a mail flagged, and one whose flag was cleared.
        tenant.AddTask(FlaggedEmailsListId, "flagged-6", "Vertrag prüfen #recht");
        tenant.DeleteTask(FlaggedEmailsListId, "flagged-3");

        await RunScanAsync(host, IndexScanMode.Delta, cancellationToken);

        await using var scope = host.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TodoWerkDbContext>();

        var state = await context.Set<TaskListIndexState>()
            .SingleAsync(cancellationToken);

        Assert.Null(state.FailureReason);
        Assert.Equal(ListScanState.Indexed, state.State);
        Assert.NotNull(state.LastSuccessfulSyncAt);

        var spellings = await context.Set<HashtagOccurrence>()
            .Select(occurrence => occurrence.Spelling)
            .ToListAsync(cancellationToken);

        // The unflagged mail took its Hashtag with it; the newly flagged one brought its own.
        Assert.Equal(["Kunde", "recht"], spellings.Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// The headline number must survive a delta pass. Progress within a pass is how many tasks
    /// the pass touched; the size of the index is how many rows it holds — conflating them turns
    /// "500 tasks indexed" into "1" half an hour after the first scan.
    /// </summary>
    [Fact]
    public async Task ADeltaSync_DoesNotShrinkTheReportedTaskCount()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tenant = new FakeTodoTenant();
        tenant.AddList(ListId, "Arbeit");
        tenant.AddTask(ListId, "task-1", "Angebot schreiben #kunde");
        tenant.AddTask(ListId, "task-2", "Rechnung stellen #kunde");
        tenant.AddTask(ListId, "task-3", "Ablage sortieren");

        await using var host = await StartHostAsync(tenant, cancellationToken);
        await RunScanAsync(host, IndexScanMode.Full, cancellationToken);

        tenant.RetitleTask(ListId, "task-1", "Angebot schreiben #kunde #dringend");
        await RunScanAsync(host, IndexScanMode.Delta, cancellationToken);

        await using var scope = host.Services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<Application.Indexing.IIndexStatusReader>();

        var status = await reader.GetStatusAsync(
            new IndexUser(
                TodoWerkWebApplicationFactory.TenantId,
                FakeEntraAndGraphHandler.UserObjectId),
            cancellationToken);

        Assert.Equal(3, status.Value.TasksIndexed);
        Assert.Equal(3, Assert.Single(status.Value.Lists).TasksIndexed);
    }

    /// <summary>
    /// Runs one scan the way the worker would: a queued row, claimed and executed out of any
    /// request. The timer is left out of it so the test asserts on the work rather than on when
    /// a background loop got round to it.
    /// </summary>
    private static async Task RunScanAsync(
        TodoWerkWebApplicationFactory host,
        IndexScanMode mode,
        CancellationToken cancellationToken)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TodoWerkDbContext>();
        var scheduler = scope.ServiceProvider.GetRequiredService<IndexScanScheduler>();

        context.Add(IndexScan.Request(
            TodoWerkWebApplicationFactory.TenantId,
            FakeEntraAndGraphHandler.UserObjectId,
            mode,
            taskListId: null,
            DateTimeOffset.UtcNow));
        await context.SaveChangesAsync(cancellationToken);

        // Claimed the way the worker claims, so these tests exercise the real handover — the
        // conditional update and the lease — rather than a shortcut the product never takes.
        var claimed = await scheduler.ClaimNextAsync(TimeSpan.FromHours(1), cancellationToken);
        Assert.NotNull(claimed);

        await scope.ServiceProvider.GetRequiredService<IndexScanRunner>().RunAsync(claimed, cancellationToken);

        var finished = await context.Set<IndexScan>()
            .AsNoTracking()
            .SingleAsync(scan => scan.Id == claimed.Id, cancellationToken);

        Assert.True(
            finished.State is IndexScanState.Completed,
            $"the scan ended {finished.State}: {finished.FailureReason}");
    }

    private async Task<TodoWerkWebApplicationFactory> StartHostAsync(
        FakeTodoTenant tenant,
        CancellationToken cancellationToken,
        int maxThrottleRetries = 5,
        bool withWorker = false)
    {
        var cloud = new EntraAndGraphRecorder();

        var host = new TodoWerkWebApplicationFactory()
            .WithConfigurationOverride("ConnectionStrings:TodoWerk", database.ConnectionString)
            .WithConfigurationOverride("DataProtection:KeyRingPath", _keyRingPath)
            .WithConfigurationOverride("Indexing:MaxThrottleRetries", maxThrottleRetries.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .WithConfigurationOverride("Indexing:MaxRetryDelay", "00:00:00")
            // A second early tick while the test is still seeding costs nothing: the worker logs
            // the failure and comes back a second later.
            .WithConfigurationOverride("Indexing:PollInterval", withWorker ? "00:00:01" : "00:00:15")
            .WithOutboundHttpHandler(() => new FakeEntraAndGraphHandler(cloud, tenant));

        // Every test here but one drives the runner itself, and a second scanner racing them
        // would make the counts a lottery. A long poll interval does not achieve that — the
        // worker ticks once as it starts, which is exactly when a test is setting up.
        if (!withWorker)
        {
            host.WithoutBackgroundWorkers();
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<TodoWerkDbContext>();

            await context.Database.MigrateAsync(cancellationToken);

            // One database serves the whole class, and every test here indexes the same user, so
            // each starts from an empty index rather than from whatever the last one left.
            await context.Set<HashtagOccurrence>().ExecuteDeleteAsync(cancellationToken);
            await context.Set<IndexedTask>().ExecuteDeleteAsync(cancellationToken);
            await context.Set<TaskListIndexState>().ExecuteDeleteAsync(cancellationToken);
            await context.Set<IndexScan>().ExecuteDeleteAsync(cancellationToken);
        }

        await SeedRefreshTokenAsync(host, cloud, tenant, cancellationToken);

        return host;
    }

    /// <summary>
    /// Puts a refresh token in the durable cache the way a completed sign-in does, so the scan can
    /// find one without a request to hang it on.
    /// </summary>
    private static async Task SeedRefreshTokenAsync(
        TodoWerkWebApplicationFactory host,
        EntraAndGraphRecorder cloud,
        FakeTodoTenant tenant,
        CancellationToken cancellationToken)
    {
        using var handler = new FakeEntraAndGraphHandler(cloud, tenant);
        using var httpClient = new HttpClient(handler, disposeHandler: false);

        var confidentialClient = ConfidentialClientApplicationBuilder
            .Create(TodoWerkWebApplicationFactory.ClientId)
            .WithClientSecret(TodoWerkWebApplicationFactory.ClientSecret)
            .WithAuthority(new Uri(
                $"https://login.microsoftonline.com/{TodoWerkWebApplicationFactory.TenantId}"))
            .WithInstanceDiscovery(false)
            .WithHttpClientFactory(new SingleClientMsalHttpFactory(httpClient))
            .Build();

        host.Services.GetRequiredService<IMsalTokenCacheProvider>()
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
