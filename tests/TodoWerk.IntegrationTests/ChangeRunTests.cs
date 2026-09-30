using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TodoWerk.Application.Changes;
using TodoWerk.Domain.Changes;
using TodoWerk.Domain.Failures;
using TodoWerk.Domain.Hashtags;
using TodoWerk.Domain.Indexing;
using TodoWerk.Infrastructure.Changes.Persistence;
using TodoWerk.Infrastructure.Indexing;
using TodoWerk.Infrastructure.Indexing.Persistence;
using TodoWerk.Infrastructure.Persistence;
using Xunit;

namespace TodoWerk.IntegrationTests;

/// <summary>
/// The write path, against real SQL Server and a fake mailbox: a Change that re-reads every task
/// before it writes it, journals what it found, skips what has moved on, and can be taken back
/// afterwards.
/// <para>
/// This is the first thing TodoWerk does that changes somebody's data, so the tests are about the
/// promises rather than about the plumbing: the smallest possible edit, an honest count, and an
/// undo that refuses to overwrite somebody else's work.
/// </para>
/// </summary>
public sealed class ChangeRunTests(SqlServerDatabaseFixture database) : IClassFixture<SqlServerDatabaseFixture>
{
    private const string ListId = "list-arbeit";

    private const string OtherListId = "list-privat";

    [Fact]
    public async Task ARename_RewritesTheHashtagAndNothingElse()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tenant = new FakeTodoTenant();
        tenant.AddList(ListId, "Arbeit");
        tenant.AddTask(ListId, "t1", "Angebot  schreiben #Prio1 — bis Freitag!");
        tenant.AddTask(ListId, "t2", "Nichts zu tun");

        await using var host = await ChangeTestHost.StartAsync(database.ConnectionString, tenant, cancellationToken);
        await host.ScanAsync(cancellationToken);

        var change = await ConfirmAsync(host, [Key("Prio1")], "Priority1", cancellationToken);

        Assert.True(await host.RunQueuedChangeAsync(cancellationToken));

        Assert.Equal("Angebot  schreiben #Priority1 — bis Freitag!", tenant.TitleOf(ListId, "t1"));
        Assert.Equal("Nichts zu tun", tenant.TitleOf(ListId, "t2"));

        // One PATCH, for the one task that carried the tag.
        Assert.Single(tenant.WrittenTitles);

        var stored = await ReadAsync(host, change, cancellationToken);

        Assert.Equal(ChangeState.Completed, stored.State);
        Assert.Equal(ChangeKind.Rename, stored.Kind);
        Assert.Equal(1, stored.WrittenCount);
        Assert.Equal(0, stored.SkippedCount);
        Assert.True(CanUndo(stored));
    }

    /// <summary>
    /// The journal records the title read immediately before the write, never the title the
    /// preview showed — which is what makes undo restore what was really there (ADR-0006).
    /// </summary>
    [Fact]
    public async Task TheJournal_RecordsTheTitleReadRatherThanTheTitlePreviewed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tenant = new FakeTodoTenant();
        tenant.AddList(ListId, "Arbeit");
        tenant.AddTask(ListId, "t1", "Angebot #Prio1");

        await using var host = await ChangeTestHost.StartAsync(database.ConnectionString, tenant, cancellationToken);
        await host.ScanAsync(cancellationToken);

        var change = await ConfirmAsync(host, [Key("Prio1")], "Priority1", cancellationToken);

        // Edited after the plan was fixed and before the run reads it. The rewrite has to be
        // applied to this title, not to the one the preview computed.
        tenant.RetitleTask(ListId, "t1", "Angebot für Meier #Prio1 heute");

        Assert.True(await host.RunQueuedChangeAsync(cancellationToken));

        Assert.Equal("Angebot für Meier #Priority1 heute", tenant.TitleOf(ListId, "t1"));

        await using var scope = host.Scope();
        var context = scope.ServiceProvider.GetRequiredService<TodoWerkDbContext>();
        var entry = await context.Set<ChangeJournalEntry>().SingleAsync(row => row.ChangeId == change, cancellationToken);

        Assert.Equal("Angebot für Meier #Prio1 heute", entry.TitleBefore);
        Assert.Equal("Angebot für Meier #Priority1 heute", entry.TitleAfter);
    }

    /// <summary>
    /// The tag being gone means skip, not fail: the user asked for a tag to change, and there is
    /// no longer a tag there to change. The count differs from the preview's, and the state says so.
    /// </summary>
    [Fact]
    public async Task ATaskWhoseHashtagVanished_IsSkippedRatherThanFailed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tenant = new FakeTodoTenant();
        tenant.AddList(ListId, "Arbeit");
        tenant.AddTask(ListId, "t1", "Angebot #Prio1");
        tenant.AddTask(ListId, "t2", "Rechnung #Prio1");

        await using var host = await ChangeTestHost.StartAsync(database.ConnectionString, tenant, cancellationToken);
        await host.ScanAsync(cancellationToken);

        var change = await ConfirmAsync(host, [Key("Prio1")], "Priority1", cancellationToken);

        tenant.RetitleTask(ListId, "t2", "Rechnung ohne Tag");

        Assert.True(await host.RunQueuedChangeAsync(cancellationToken));

        var stored = await ReadAsync(host, change, cancellationToken);

        Assert.Equal(ChangeState.CompletedWithSkips, stored.State);
        Assert.Equal(1, stored.WrittenCount);
        Assert.Equal(1, stored.SkippedCount);
        Assert.Equal("Rechnung ohne Tag", tenant.TitleOf(ListId, "t2"));
    }

    /// <summary>
    /// A task deleted between the plan and the write is the write path's ordinary 404, and it must
    /// not stop the Change: the plan was made from an index, and an index is allowed to be behind.
    /// </summary>
    [Fact]
    public async Task ATaskDeletedSinceThePlan_IsSkippedAndTheRestStillRun()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tenant = new FakeTodoTenant();
        tenant.AddList(ListId, "Arbeit");
        tenant.AddTask(ListId, "t1", "Angebot #Prio1");
        tenant.AddTask(ListId, "t2", "Rechnung #Prio1");

        await using var host = await ChangeTestHost.StartAsync(database.ConnectionString, tenant, cancellationToken);
        await host.ScanAsync(cancellationToken);

        var change = await ConfirmAsync(host, [Key("Prio1")], "Priority1", cancellationToken);

        tenant.DeleteTask(ListId, "t1");

        Assert.True(await host.RunQueuedChangeAsync(cancellationToken));

        var stored = await ReadAsync(host, change, cancellationToken);

        Assert.Equal(ChangeState.CompletedWithSkips, stored.State);
        Assert.Equal(1, stored.WrittenCount);
        Assert.Equal(1, stored.SkippedCount);
        Assert.Equal("Rechnung #Priority1", tenant.TitleOf(ListId, "t2"));
    }

    /// <summary>
    /// Merging two Hashtags onto one Spelling, including the duplicate ADR-0006 accepts rather
    /// than smooths over — collapsing it would mean deciding which one survives, and it would turn
    /// undo from a stored string into text surgery.
    /// </summary>
    [Fact]
    public async Task AMerge_RewritesEverySourceAndKeepsTheDuplicateItProduces()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tenant = new FakeTodoTenant();
        tenant.AddList(ListId, "Arbeit");
        tenant.AddTask(ListId, "t1", "#kunde and #customer");
        tenant.AddTask(ListId, "t2", "Nur #klient");

        await using var host = await ChangeTestHost.StartAsync(database.ConnectionString, tenant, cancellationToken);
        await host.ScanAsync(cancellationToken);

        var change = await ConfirmAsync(host, [Key("kunde"), Key("klient")], "customer", cancellationToken, merge: true);

        Assert.True(await host.RunQueuedChangeAsync(cancellationToken));

        Assert.Equal("#customer and #customer", tenant.TitleOf(ListId, "t1"));
        Assert.Equal("Nur #customer", tenant.TitleOf(ListId, "t2"));

        var stored = await ReadAsync(host, change, cancellationToken);

        Assert.Equal(ChangeKind.Merge, stored.Kind);
        Assert.Equal(2, stored.WrittenCount);
    }

    /// <summary>
    /// The promise that makes a bulk rewrite safe to confirm: undo runs as a Change in the other
    /// direction and puts every title back.
    /// </summary>
    [Fact]
    public async Task Undo_PutsTheTitlesBack()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tenant = new FakeTodoTenant();
        tenant.AddList(ListId, "Arbeit");
        tenant.AddTask(ListId, "t1", "Angebot #Prio1");
        tenant.AddTask(ListId, "t2", "Rechnung #Prio1 dringend");

        await using var host = await ChangeTestHost.StartAsync(database.ConnectionString, tenant, cancellationToken);
        await host.ScanAsync(cancellationToken);

        var change = await ConfirmAsync(host, [Key("Prio1")], "Priority1", cancellationToken);
        Assert.True(await host.RunQueuedChangeAsync(cancellationToken));

        Guid undoId;

        await using (var scope = host.Scope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IChangeStore>();
            var queued = await store.AddUndoAsync(ChangeTestHost.User, change, cancellationToken);

            Assert.NotNull(queued);
            undoId = queued.Value;
        }

        Assert.True(await host.RunQueuedChangeAsync(cancellationToken));

        Assert.Equal("Angebot #Prio1", tenant.TitleOf(ListId, "t1"));
        Assert.Equal("Rechnung #Prio1 dringend", tenant.TitleOf(ListId, "t2"));

        var stored = await ReadAsync(host, undoId, cancellationToken);

        Assert.Equal(ChangeState.Completed, stored.State);
        Assert.Equal(change, stored.UndoOfChangeId);

        // One level deep: the undo itself cannot be undone, and neither can the Change it reversed.
        Assert.False(CanUndo(stored));
        Assert.False(CanUndo(await ReadAsync(host, change, cancellationToken)));
    }

    /// <summary>
    /// A task somebody edited after TodoWerk changed it is left alone: their edit wins, and undo
    /// says so rather than quietly overwriting it.
    /// </summary>
    [Fact]
    public async Task Undo_LeavesATaskSomebodyEditedSinceAlone()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tenant = new FakeTodoTenant();
        tenant.AddList(ListId, "Arbeit");
        tenant.AddTask(ListId, "t1", "Angebot #Prio1");
        tenant.AddTask(ListId, "t2", "Rechnung #Prio1");

        await using var host = await ChangeTestHost.StartAsync(database.ConnectionString, tenant, cancellationToken);
        await host.ScanAsync(cancellationToken);

        var change = await ConfirmAsync(host, [Key("Prio1")], "Priority1", cancellationToken);
        Assert.True(await host.RunQueuedChangeAsync(cancellationToken));

        tenant.RetitleTask(ListId, "t2", "Rechnung #Priority1 — von Hand ergänzt");

        Guid undoId;

        await using (var scope = host.Scope())
        {
            var queued = await scope.ServiceProvider.GetRequiredService<IChangeStore>()
                .AddUndoAsync(ChangeTestHost.User, change, cancellationToken);

            undoId = queued!.Value;
        }

        Assert.True(await host.RunQueuedChangeAsync(cancellationToken));

        Assert.Equal("Angebot #Prio1", tenant.TitleOf(ListId, "t1"));
        Assert.Equal("Rechnung #Priority1 — von Hand ergänzt", tenant.TitleOf(ListId, "t2"));

        var stored = await ReadAsync(host, undoId, cancellationToken);

        Assert.Equal(ChangeState.CompletedWithSkips, stored.State);
        Assert.Equal(1, stored.WrittenCount);
        Assert.Equal(1, stored.SkippedCount);
    }

    /// <summary>
    /// Cancel stops after the task the runner is on, and what it already wrote stays written — and
    /// undoable, which is the only reason stopping halfway is safe to offer.
    /// </summary>
    [Fact]
    public async Task Cancel_StopsTheRunAndLeavesWhatItWroteUndoable()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tenant = new FakeTodoTenant();
        tenant.AddList(ListId, "Arbeit");
        tenant.AddTask(ListId, "t1", "Angebot #Prio1");

        await using var host = await ChangeTestHost.StartAsync(database.ConnectionString, tenant, cancellationToken);
        await host.ScanAsync(cancellationToken);

        var change = await ConfirmAsync(host, [Key("Prio1")], "Priority1", cancellationToken);

        await using (var scope = host.Scope())
        {
            Assert.True(await scope.ServiceProvider.GetRequiredService<IChangeStore>()
                .RequestCancelAsync(ChangeTestHost.User, change, cancellationToken));
        }

        Assert.True(await host.RunQueuedChangeAsync(cancellationToken));

        // Cancelled before it started, so nothing was written and nothing is left to undo.
        Assert.Equal("Angebot #Prio1", tenant.TitleOf(ListId, "t1"));

        var stored = await ReadAsync(host, change, cancellationToken);

        Assert.Equal(ChangeState.Cancelled, stored.State);
        Assert.Equal(0, stored.WrittenCount);
        Assert.False(CanUndo(stored));
    }

    /// <summary>
    /// A Change interrupted by a deploy resumes at the first row still pending rather than
    /// starting over: the plan rows are the resume point, and a second PATCH per task would put a
    /// second journal entry behind one write.
    /// </summary>
    [Fact]
    public async Task AResumedChange_DoesNotRewriteWhatItAlreadyWrote()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tenant = new FakeTodoTenant();
        tenant.AddList(ListId, "Arbeit");
        tenant.AddTask(ListId, "t1", "Angebot #Prio1");
        tenant.AddTask(ListId, "t2", "Rechnung #Prio1");

        await using var host = await ChangeTestHost.StartAsync(database.ConnectionString, tenant, cancellationToken);
        await host.ScanAsync(cancellationToken);

        var change = await ConfirmAsync(host, [Key("Prio1")], "Priority1", cancellationToken);
        Assert.True(await host.RunQueuedChangeAsync(cancellationToken));

        var writes = tenant.WrittenTitles.Count;

        // Put the Change back in the queue the way a lost lease would, with its plan rows intact.
        await using (var scope = host.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<TodoWerkDbContext>();

            await context.Set<Change>()
                .Where(row => row.Id == change)
                .ExecuteUpdateAsync(
                    set => set
                        .SetProperty(row => row.State, ChangeState.Pending)
                        .SetProperty(row => row.StartedAt, (DateTimeOffset?)null)
                        .SetProperty(row => row.CompletedAt, (DateTimeOffset?)null),
                    cancellationToken);
        }

        Assert.True(await host.RunQueuedChangeAsync(cancellationToken));

        Assert.Equal(writes, tenant.WrittenTitles.Count);

        await using var assertions = host.Scope();
        var journal = assertions.ServiceProvider.GetRequiredService<TodoWerkDbContext>();

        Assert.Equal(2, await journal.Set<ChangeJournalEntry>().CountAsync(row => row.ChangeId == change, cancellationToken));
    }

    /// <summary>
    /// The index catches up through Graph rather than through index writes from the Changes module
    /// (ADR-0006), so a finished Change leaves a scan queued for every list it touched — and for
    /// none of the lists it did not.
    /// </summary>
    [Fact]
    public async Task AFinishedChange_QueuesAScanOfTheListsItTouched()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tenant = new FakeTodoTenant();
        tenant.AddList(ListId, "Arbeit");
        tenant.AddTask(ListId, "t1", "Angebot #Prio1");
        tenant.AddList(OtherListId, "Privat");
        tenant.AddTask(OtherListId, "t2", "Einkaufen #haushalt");

        await using var host = await ChangeTestHost.StartAsync(database.ConnectionString, tenant, cancellationToken);
        await host.ScanAsync(cancellationToken);

        await ConfirmAsync(host, [Key("Prio1")], "Priority1", cancellationToken);
        Assert.True(await host.RunQueuedChangeAsync(cancellationToken));

        await using var scope = host.Scope();
        var context = scope.ServiceProvider.GetRequiredService<TodoWerkDbContext>();

        var queued = await context.Set<IndexScan>()
            .AsNoTracking()
            .Where(scan => scan.State == IndexScanState.Pending)
            .Select(scan => scan.TaskListId)
            .ToListAsync(cancellationToken);

        Assert.Equal(ListId, Assert.Single(queued));
    }

    /// <summary>
    /// One Change per user at a time, and never while a scan is running — enforced in the claim
    /// query rather than in a service method that can be bypassed.
    /// </summary>
    [Fact]
    public async Task AChange_IsNotClaimedWhileThatUserHasAScanRunning()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tenant = new FakeTodoTenant();
        tenant.AddList(ListId, "Arbeit");
        tenant.AddTask(ListId, "t1", "Angebot #Prio1");

        await using var host = await ChangeTestHost.StartAsync(database.ConnectionString, tenant, cancellationToken);
        await host.ScanAsync(cancellationToken);

        await ConfirmAsync(host, [Key("Prio1")], "Priority1", cancellationToken);

        // A scan claimed and still holding its lease, as if a worker were mid-pass.
        await using (var scope = host.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<TodoWerkDbContext>();

            context.Add(IndexScan.Request(
                ChangeTestHost.User.TenantId,
                ChangeTestHost.User.UserId,
                IndexScanMode.Delta,
                taskListId: null,
                DateTimeOffset.UtcNow));
            await context.SaveChangesAsync(cancellationToken);

            var claimed = await scope.ServiceProvider.GetRequiredService<IndexScanScheduler>()
                .ClaimNextAsync(TimeSpan.FromHours(1), cancellationToken);

            Assert.NotNull(claimed);
        }

        Assert.False(await host.RunQueuedChangeAsync(cancellationToken));
        Assert.Equal("Angebot #Prio1", tenant.TitleOf(ListId, "t1"));
    }

    /// <summary>
    /// And the exclusion the other way: a scan is not claimed while that user has a Change running,
    /// or the inventory would move under somebody watching their own rename.
    /// </summary>
    [Fact]
    public async Task AScan_IsNotClaimedWhileThatUserHasAChangeRunning()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tenant = new FakeTodoTenant();
        tenant.AddList(ListId, "Arbeit");
        tenant.AddTask(ListId, "t1", "Angebot #Prio1");

        await using var host = await ChangeTestHost.StartAsync(database.ConnectionString, tenant, cancellationToken);
        await host.ScanAsync(cancellationToken);

        await ConfirmAsync(host, [Key("Prio1")], "Priority1", cancellationToken);

        await using var scope = host.Scope();

        var claimedChange = await scope.ServiceProvider.GetRequiredService<ChangeQueue>()
            .ClaimNextAsync(TimeSpan.FromHours(1), cancellationToken);

        Assert.NotNull(claimedChange);

        var context = scope.ServiceProvider.GetRequiredService<TodoWerkDbContext>();

        context.Add(IndexScan.Request(
            ChangeTestHost.User.TenantId,
            ChangeTestHost.User.UserId,
            IndexScanMode.Delta,
            taskListId: null,
            DateTimeOffset.UtcNow));
        await context.SaveChangesAsync(cancellationToken);

        var claimedScan = await scope.ServiceProvider.GetRequiredService<IndexScanScheduler>()
            .ClaimNextAsync(TimeSpan.FromHours(1), cancellationToken);

        Assert.Null(claimedScan);
    }

    /// <summary>
    /// A confirmed Change is meant to start immediately, and exclusivity alone would make that
    /// false because a first scan is minutes long. So a running scan hands its row back at a page
    /// boundary — once — and resumes after the Change (ADR-0006).
    /// </summary>
    [Fact]
    public async Task ARunningScan_StandsAsideOnceForAQueuedChange()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tenant = new FakeTodoTenant();
        tenant.AddList(ListId, "Arbeit");

        // More tasks than one page, so the scan reaches a page boundary with work still to do.
        for (var number = 0; number < 6; number++)
        {
            tenant.AddTask(ListId, $"t{number}", $"Aufgabe {number} #Prio1");
        }

        await using var host = await ChangeTestHost.StartAsync(database.ConnectionString, tenant, cancellationToken);
        await host.ScanAsync(cancellationToken);

        await ConfirmAsync(host, [Key("Prio1")], "Priority1", cancellationToken);

        // A scan queued and claimed while a Change waits: it must give way rather than hold the
        // Change behind a full pass.
        await using (var scope = host.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<TodoWerkDbContext>();

            context.Add(IndexScan.Request(
                ChangeTestHost.User.TenantId,
                ChangeTestHost.User.UserId,
                IndexScanMode.Full,
                taskListId: null,
                DateTimeOffset.UtcNow));
            await context.SaveChangesAsync(cancellationToken);

            var scheduler = scope.ServiceProvider.GetRequiredService<IndexScanScheduler>();
            var claimed = await scheduler.ClaimNextAsync(TimeSpan.FromHours(1), cancellationToken);

            Assert.NotNull(claimed);

            await scope.ServiceProvider.GetRequiredService<IndexScanRunner>().RunAsync(claimed, cancellationToken);
        }

        await using (var scope = host.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<TodoWerkDbContext>();

            var preempted = await context.Set<IndexScan>()
                .AsNoTracking()
                .SingleAsync(scan => scan.WasPreempted, cancellationToken);

            Assert.Equal(IndexScanState.Pending, preempted.State);
            Assert.Null(preempted.StartedAt);
        }

        // And the Change it stood aside for can now be claimed and run.
        Assert.True(await host.RunQueuedChangeAsync(cancellationToken));
        Assert.Equal("Aufgabe 0 #Priority1", tenant.TitleOf(ListId, "t0"));
    }

    /// <summary>
    /// Standing aside on the last page of a list would throw the whole pass away: the list would
    /// never be completed, never get its delta link, and — on a first scan — be excluded from the
    /// plan of the very Change it made way for, because a plan only covers lists read end to end.
    /// So the scan finishes the list it is on and gives way at the next page that has one after it.
    /// </summary>
    [Fact]
    public async Task AScanOnItsLastPage_FinishesTheListRatherThanStandingAside()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tenant = new FakeTodoTenant();
        tenant.AddList(ListId, "Arbeit");
        tenant.AddTask(ListId, "t1", "Angebot #Prio1");

        await using var host = await ChangeTestHost.StartAsync(database.ConnectionString, tenant, cancellationToken);
        await host.ScanAsync(cancellationToken);

        await ConfirmAsync(host, [Key("Prio1")], "Priority1", cancellationToken);

        // One task, so the very first page is also the last one.
        await using (var scope = host.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<TodoWerkDbContext>();

            context.Add(IndexScan.Request(
                ChangeTestHost.User.TenantId,
                ChangeTestHost.User.UserId,
                IndexScanMode.Full,
                taskListId: null,
                DateTimeOffset.UtcNow));
            await context.SaveChangesAsync(cancellationToken);

            var claimed = await scope.ServiceProvider.GetRequiredService<IndexScanScheduler>()
                .ClaimNextAsync(TimeSpan.FromHours(1), cancellationToken);

            Assert.NotNull(claimed);

            await scope.ServiceProvider.GetRequiredService<IndexScanRunner>().RunAsync(claimed, cancellationToken);
        }

        await using (var scope = host.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<TodoWerkDbContext>();

            var state = await context.Set<TaskListIndexState>().AsNoTracking().SingleAsync(cancellationToken);

            Assert.Equal(ListScanState.Indexed, state.State);
            Assert.True(state.IsFullyIndexed);
            Assert.True(state.CanSyncIncrementally);

            Assert.False(
                await context.Set<IndexScan>().AsNoTracking().AnyAsync(scan => scan.WasPreempted, cancellationToken),
                "the scan stood aside on its last page and threw the pass away");
        }

        // And the Change still runs: exclusivity releases the moment the scan completes.
        Assert.True(await host.RunQueuedChangeAsync(cancellationToken));
        Assert.Equal("Angebot #Priority1", tenant.TitleOf(ListId, "t1"));
    }

    /// <summary>
    /// A failure the next task would meet as well stops the run there rather than spending the
    /// other nine hundred attempts learning the same thing — and the Change carries the code the
    /// client switches on.
    /// </summary>
    [Fact]
    public async Task AThrottleTheGatewayCannotWaitOut_StopsTheRunWithACode()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tenant = new FakeTodoTenant();
        tenant.AddList(ListId, "Arbeit");
        tenant.AddTask(ListId, "t1", "Angebot #Prio1");
        tenant.AddTask(ListId, "t2", "Rechnung #Prio1");
        tenant.ThrottleTask("t1", times: 99);

        await using var host = await ChangeTestHost.StartAsync(database.ConnectionString, tenant, cancellationToken);
        await host.ScanAsync(cancellationToken);

        var change = await ConfirmAsync(host, [Key("Prio1")], "Priority1", cancellationToken);

        Assert.True(await host.RunQueuedChangeAsync(cancellationToken));

        var stored = await ReadAsync(host, change, cancellationToken);

        Assert.Equal(ChangeState.Failed, stored.State);
        Assert.Equal(FailureCode.Throttled, stored.FailureCode);
        Assert.Equal(0, stored.WrittenCount);
        Assert.Equal(1, stored.FailedCount);
        Assert.NotNull(stored.FailureReason);

        // The second task was never attempted, so it is still pending rather than failed.
        await using var scope = host.Scope();
        var context = scope.ServiceProvider.GetRequiredService<TodoWerkDbContext>();

        Assert.Equal(
            1,
            await context.Set<ChangePlanItem>()
                .CountAsync(item => item.ChangeId == change && item.Status == ChangePlanItemStatus.Pending, cancellationToken));
    }

    /// <summary>
    /// To Do silently truncates titles over 255 characters: it keeps 255 characters of a title
    /// and drops the rest, answering success either way. Writing a longer one
    /// leaves a Hashtag cut in half and journals a title that never existed in the mailbox, so
    /// undo compares, fails to match, and skips — the damage outliving the only tool that could
    /// take it back. So the task is left alone instead.
    /// </summary>
    [Fact]
    public async Task ARewriteLongerThanToDoStores_IsSkippedRatherThanSilentlyTruncated()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tenant = new FakeTodoTenant();
        tenant.AddList(ListId, "Arbeit");

        // Exactly at the limit before the rewrite, so only the longer target pushes it past.
        var title = TitleOfLength(TodoTaskLimits.TitleLength, "#Zz");
        tenant.AddTask(ListId, "t1", title);
        tenant.AddTask(ListId, "t2", "Kurz #Zz");

        await using var host = await ChangeTestHost.StartAsync(database.ConnectionString, tenant, cancellationToken);
        await host.ScanAsync(cancellationToken);

        var change = await ConfirmAsync(host, [Key("Zz")], "ZzExtendedTagNameForLengthProbe", cancellationToken);

        Assert.True(await host.RunQueuedChangeAsync(cancellationToken));

        var stored = await ReadAsync(host, change, cancellationToken);

        Assert.Equal(ChangeState.CompletedWithSkips, stored.State);
        Assert.Equal(1, stored.WrittenCount);
        Assert.Equal(1, stored.SkippedCount);

        // Untouched, and — the point — never sent. A PATCH would have been answered with success
        // and stored something else.
        Assert.Equal(title, tenant.TitleOf(ListId, "t1"));
        Assert.DoesNotContain(tenant.WrittenTitles, written => written.TaskId == "t1");

        // The short one still ran: one task's refusal is not the Change's failure.
        Assert.Equal("Kurz #ZzExtendedTagNameForLengthProbe", tenant.TitleOf(ListId, "t2"));
    }

    /// <summary>
    /// The other side of the same boundary. A rewrite landing exactly on the limit is ordinary
    /// work, and refusing it would be this fix overreaching — the failure mode of a bound chosen
    /// by fright rather than by measurement.
    /// </summary>
    [Fact]
    public async Task ARewriteThatLandsExactlyOnTheLimit_IsWritten()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tenant = new FakeTodoTenant();
        tenant.AddList(ListId, "Arbeit");

        // Renaming #Zz to #Zzz adds one character, so the title must start one short of the limit.
        var title = TitleOfLength(TodoTaskLimits.TitleLength - 1, "#Zz");
        tenant.AddTask(ListId, "t1", title);

        await using var host = await ChangeTestHost.StartAsync(database.ConnectionString, tenant, cancellationToken);
        await host.ScanAsync(cancellationToken);

        var change = await ConfirmAsync(host, [Key("Zz")], "Zzz", cancellationToken);

        Assert.True(await host.RunQueuedChangeAsync(cancellationToken));

        var stored = await ReadAsync(host, change, cancellationToken);

        Assert.Equal(ChangeState.Completed, stored.State);
        Assert.Equal(1, stored.WrittenCount);
        Assert.Equal(0, stored.SkippedCount);

        var written = tenant.TitleOf(ListId, "t1");

        Assert.Equal(TodoTaskLimits.TitleLength, written!.Length);
        Assert.EndsWith("#Zzz", written, StringComparison.Ordinal);
    }

    private static string Key(string spelling) => HashtagKey.Fold(spelling);

    /// <summary>A title of exactly <paramref name="length"/> characters, ending in a Hashtag.</summary>
    private static string TitleOfLength(int length, string hashtag) =>
        string.Concat(new string('x', length - hashtag.Length - 1), " ", hashtag);

    /// <summary>
    /// Confirms a Change through the endpoint, so these tests exercise the refusals and the
    /// server-side re-plan rather than writing rows of their own.
    /// </summary>
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

        Assert.True(
            result.StatusCode == HttpStatusCode.Accepted,
            $"confirming the change answered {(int)result.StatusCode}: {result.Detail}");

        return result.Body!.Id;
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

    /// <summary>The rule the Workbench draws its Undo button from, asked of a stored Change.</summary>
    private static bool CanUndo(ChangeRecord change) =>
        ChangeUndoPolicy.CanUndo(change, DateTimeOffset.UtcNow, TimeSpan.FromDays(30));

    /// <summary>All these tests want off the confirmation: which Change to drive next.</summary>
    private sealed record ConfirmedChange(Guid Id);
}
