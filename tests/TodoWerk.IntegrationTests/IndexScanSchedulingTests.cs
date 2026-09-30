using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TodoWerk.Domain.Indexing;
using TodoWerk.Infrastructure.Indexing.Persistence;
using TodoWerk.Infrastructure.Persistence;
using Xunit;
using TodoWerk.Application.Abstractions.Authentication;
using TodoWerk.Application.Onboarding;
using TodoWerk.Domain.Failures;
using TodoWerk.Domain.Onboarding;

namespace TodoWerk.IntegrationTests;

/// <summary>
/// When a scan gets queued and when it does not. Both directions matter: swallowing a request the
/// user made is as wrong as queueing the same work twice, and a schedule that re-queues a failing
/// list on every poll is a retry loop against Graph wearing a schedule's clothes.
/// <para>
/// The scheduled sync is for people who are present. Every test of it here signs its person in
/// first — through the store, the way the sign-in paths do — so that a test expecting nothing to
/// be queued is passing because of the timestamps it set up, not because nobody was there.
/// </para>
/// </summary>
public sealed class IndexScanSchedulingTests(SqlServerDatabaseFixture database)
    : IClassFixture<SqlServerDatabaseFixture>
{
    private static readonly IndexUserPair User =
        new(TodoWerkWebApplicationFactory.TenantId, FakeEntraAndGraphHandler.UserObjectId);

    private static readonly TimeSpan SyncInterval = TimeSpan.FromMinutes(30);

    private static readonly TimeSpan IdleAfter = TimeSpan.FromDays(14);

    [Fact]
    public async Task AskingTwiceForTheSameScan_QueuesItOnce()
    {
        await using var host = await StartHostAsync();

        Assert.True(await RequestAsync(host, IndexScanMode.Delta));
        Assert.False(await RequestAsync(host, IndexScanMode.Delta));

        Assert.Equal(1, await CountScansAsync(host));
    }

    /// <summary>
    /// The scheduled delta sync runs every half hour, so a full re-scan asked for inside that
    /// window would be swallowed if any in-flight scan counted as covering it — and the button is
    /// disabled while a scan runs, so the user would never get a second chance to ask.
    /// </summary>
    [Fact]
    public async Task AFullRescan_IsNotSwallowedByAQueuedDeltaSync()
    {
        await using var host = await StartHostAsync();

        Assert.True(await RequestAsync(host, IndexScanMode.Delta));
        Assert.True(await RequestAsync(host, IndexScanMode.Full));

        Assert.Equal(2, await CountScansAsync(host));
    }

    /// <summary>The other direction: a full pass already covers everything a delta would read.</summary>
    [Fact]
    public async Task ADeltaSync_IsCoveredByAQueuedFullRescan()
    {
        await using var host = await StartHostAsync();

        Assert.True(await RequestAsync(host, IndexScanMode.Full));
        Assert.False(await RequestAsync(host, IndexScanMode.Delta));
    }

    [Fact]
    public async Task AWholeIndexScan_CoversARequestForOneList()
    {
        await using var host = await StartHostAsync();

        Assert.True(await RequestAsync(host, IndexScanMode.Full));
        Assert.False(await RequestAsync(host, IndexScanMode.Full, taskListId: "list-arbeit"));
    }

    [Fact]
    public async Task AScanOfOneList_DoesNotCoverTheWholeIndex()
    {
        await using var host = await StartHostAsync();

        Assert.True(await RequestAsync(host, IndexScanMode.Full, taskListId: "list-arbeit"));
        Assert.True(await RequestAsync(host, IndexScanMode.Full));
    }

    [Fact]
    public async Task AListThatHasNeverSynced_IsScheduled()
    {
        await using var host = await StartHostAsync();
        await SignInAsync(host);
        await SeedListAsync(host, lastAttemptAt: null, lastSuccessfulSyncAt: null);

        Assert.Equal(1, await ScheduleDueAsync(host));
    }

    [Fact]
    public async Task AListSyncedRecently_IsNotScheduled()
    {
        await using var host = await StartHostAsync();
        await SignInAsync(host);
        await SeedListAsync(
            host,
            lastAttemptAt: DateTimeOffset.UtcNow.AddMinutes(-1),
            lastSuccessfulSyncAt: DateTimeOffset.UtcNow.AddMinutes(-1));

        Assert.Equal(0, await ScheduleDueAsync(host));
    }

    /// <summary>
    /// The defect this test exists for: a list that keeps failing never gets a successful sync, so
    /// scheduling on success alone makes it due again the instant the worker next looks — a scan
    /// every poll interval rather than every sync interval, hammering Graph on behalf of a user
    /// whose list is broken.
    /// </summary>
    [Fact]
    public async Task AListThatJustFailed_WaitsTheSyncIntervalBeforeBeingRetried()
    {
        await using var host = await StartHostAsync();
        await SignInAsync(host);
        await SeedListAsync(
            host,
            lastAttemptAt: DateTimeOffset.UtcNow.AddMinutes(-1),
            lastSuccessfulSyncAt: null,
            failed: true);

        Assert.Equal(0, await ScheduleDueAsync(host));
    }

    [Fact]
    public async Task AListThatFailedLongAgo_IsTriedAgain()
    {
        await using var host = await StartHostAsync();
        await SignInAsync(host);
        await SeedListAsync(
            host,
            lastAttemptAt: DateTimeOffset.UtcNow.AddHours(-3),
            lastSuccessfulSyncAt: null,
            failed: true);

        Assert.Equal(1, await ScheduleDueAsync(host));
    }

    [Fact]
    public async Task AUserWithAScanAlreadyQueued_IsNotScheduledAgain()
    {
        await using var host = await StartHostAsync();
        await SignInAsync(host);
        await SeedListAsync(host, lastAttemptAt: null, lastSuccessfulSyncAt: null);
        await RequestAsync(host, IndexScanMode.Delta);

        Assert.Equal(0, await ScheduleDueAsync(host));
    }

    /// <summary>
    /// The defect this pins: a scan can fail before it touches a single list — no token, no list
    /// of lists — and dueness read off the list states alone would queue that user again on the
    /// very next poll. The scan row itself is recent, and that must be enough to wait.
    /// </summary>
    [Fact]
    public async Task AUserWhoseScanJustFailed_IsNotDueAgainUntilTheIntervalPasses()
    {
        await using var host = await StartHostAsync();
        await SignInAsync(host);
        await SeedListAsync(host, lastAttemptAt: null, lastSuccessfulSyncAt: null);
        await AddScanAsync(host, DateTimeOffset.UtcNow, IndexScanState.Failed);

        Assert.Equal(0, await ScheduleDueAsync(host));
    }

    [Fact]
    public async Task AUserWhoseScanFailedLongAgo_IsTriedAgain()
    {
        await using var host = await StartHostAsync();
        await SignInAsync(host);
        await SeedListAsync(host, lastAttemptAt: null, lastSuccessfulSyncAt: null);
        await AddScanAsync(host, DateTimeOffset.UtcNow.AddHours(-2), IndexScanState.Failed);

        Assert.Equal(1, await ScheduleDueAsync(host));
    }

    /// <summary>
    /// The bound on the schedule: somebody who has not signed in inside the idle window is not
    /// synced on a timer, however stale their index. Without it every person who ever signed in is
    /// synced every half hour for the life of the deployment — and under the Hosted Service, asked
    /// about at the portal first, every time.
    /// </summary>
    [Fact]
    public async Task APersonWhoseLastSignInIsBeyondTheIdleWindow_IsNotScheduled()
    {
        await using var host = await StartHostAsync();
        await SignInAsync(host, signedInAt: DateTimeOffset.UtcNow.AddDays(-15));
        await SeedListAsync(host, lastAttemptAt: null, lastSuccessfulSyncAt: null);

        Assert.Equal(0, await ScheduleDueAsync(host));
    }

    [Fact]
    public async Task APersonWhoSignedInInsideTheIdleWindow_IsScheduled()
    {
        await using var host = await StartHostAsync();
        await SignInAsync(host, signedInAt: DateTimeOffset.UtcNow.AddDays(-13));
        await SeedListAsync(host, lastAttemptAt: null, lastSuccessfulSyncAt: null);

        Assert.Equal(1, await ScheduleDueAsync(host));
    }

    /// <summary>
    /// Being forgotten takes the last sign-in with it (ADR-0009), and somebody with no last moment
    /// is not present. Their index rows are destroyed by the same erasure, so in the product this
    /// state is transient; the test pins that the schedule reads it the right way regardless.
    /// </summary>
    [Fact]
    public async Task AnAnonymisedMember_IsNotScheduled()
    {
        await using var host = await StartHostAsync();
        await SignInAsync(host);
        await AnonymiseAsync(host);
        await SeedListAsync(host, lastAttemptAt: null, lastSuccessfulSyncAt: null);

        Assert.Equal(0, await ScheduleDueAsync(host));
    }

    /// <summary>
    /// Index rows written before the Tenant Member table existed have no member row unless the
    /// person has signed in since. Nobody there is anybody, and the schedule must not invent a
    /// presence for them.
    /// </summary>
    [Fact]
    public async Task AStateWithNoMemberRow_IsNotScheduled()
    {
        await using var host = await StartHostAsync();
        await SeedListAsync(host, lastAttemptAt: null, lastSuccessfulSyncAt: null);

        Assert.Equal(0, await ScheduleDueAsync(host));
    }

    /// <summary>
    /// Coming back needs no code of its own: the sign-in stamp is what the schedule reads, so the
    /// very next pass after it queues the overdue sync.
    /// </summary>
    [Fact]
    public async Task ASignInByAnIdlePerson_MakesThemDueOnTheNextSchedule()
    {
        await using var host = await StartHostAsync();
        await SignInAsync(host, signedInAt: DateTimeOffset.UtcNow.AddDays(-15));
        await SeedListAsync(host, lastAttemptAt: null, lastSuccessfulSyncAt: null);

        Assert.Equal(0, await ScheduleDueAsync(host));

        await SignInAsync(host);

        Assert.Equal(1, await ScheduleDueAsync(host));
    }

    /// <summary>
    /// The gate is on the schedule and nothing else. A scan somebody asks for is theirs to ask
    /// for, whatever their sign-in record says — the Teams tab's SSO is one path that could reach
    /// the request before the member row has moved — and it is queued and claimable as before.
    /// </summary>
    [Fact]
    public async Task AScanAnIdlePersonRequests_IsStillQueuedAndClaimable()
    {
        await using var host = await StartHostAsync();
        await SignInAsync(host, signedInAt: DateTimeOffset.UtcNow.AddDays(-15));
        await SeedListAsync(host, lastAttemptAt: null, lastSuccessfulSyncAt: null);

        Assert.True(await RequestAsync(host, IndexScanMode.Delta));

        var claimed = await WithSchedulerAsync(host, scheduler =>
            scheduler.ClaimNextAsync(TimeSpan.FromHours(1), TestContext.Current.CancellationToken));

        Assert.NotNull(claimed);
        Assert.Equal(User.UserId, claimed.UserId);
    }

    [Fact]
    public async Task ClaimingTakesTheOldestScan_AndEachRowOnlyOnce()
    {
        await using var host = await StartHostAsync();
        var older = await AddScanAsync(host, DateTimeOffset.UtcNow.AddMinutes(-2));
        var newer = await AddScanAsync(host, DateTimeOffset.UtcNow, userId: "00000000-0000-0000-0000-00000000beef");

        var first = await WithSchedulerAsync(host, scheduler =>
            scheduler.ClaimNextAsync(TimeSpan.FromHours(1), TestContext.Current.CancellationToken));
        var second = await WithSchedulerAsync(host, scheduler =>
            scheduler.ClaimNextAsync(TimeSpan.FromHours(1), TestContext.Current.CancellationToken));
        var third = await WithSchedulerAsync(host, scheduler =>
            scheduler.ClaimNextAsync(TimeSpan.FromHours(1), TestContext.Current.CancellationToken));

        Assert.Equal(older, first!.Id);
        Assert.Equal(newer, second!.Id);
        Assert.Null(third);
    }

    /// <summary>
    /// The recovery ADR-0003's "a deploy mid-scan resumes" rests on: a Running row whose lease
    /// has aged out belongs to a dead process and is claimable again — and one whose lease is
    /// fresh is not.
    /// </summary>
    [Fact]
    public async Task AnAbandonedScan_IsReclaimable_AndALiveOneIsNot()
    {
        await using var host = await StartHostAsync();
        var id = await AddScanAsync(host, DateTimeOffset.UtcNow);

        var claimed = await WithSchedulerAsync(host, scheduler =>
            scheduler.ClaimNextAsync(TimeSpan.FromHours(1), TestContext.Current.CancellationToken));
        Assert.Equal(id, claimed!.Id);

        var whileLive = await WithSchedulerAsync(host, scheduler =>
            scheduler.ClaimNextAsync(TimeSpan.FromHours(1), TestContext.Current.CancellationToken));
        Assert.Null(whileLive);

        var reclaimed = await WithSchedulerAsync(host, scheduler =>
            scheduler.ClaimNextAsync(TimeSpan.Zero, TestContext.Current.CancellationToken));
        Assert.Equal(id, reclaimed!.Id);
    }

    /// <summary>
    /// A process that lost its row must find out: renewals and completion are conditional on the
    /// lease, so the late owner's writes are refused and the new owner's stand.
    /// </summary>
    [Fact]
    public async Task ALostLease_RefusesTheLateOwnersWrites()
    {
        await using var host = await StartHostAsync();
        await AddScanAsync(host, DateTimeOffset.UtcNow);

        var original = await WithSchedulerAsync(host, scheduler =>
            scheduler.ClaimNextAsync(TimeSpan.FromHours(1), TestContext.Current.CancellationToken));
        var usurper = await WithSchedulerAsync(host, scheduler =>
            scheduler.ClaimNextAsync(TimeSpan.Zero, TestContext.Current.CancellationToken));

        Assert.False(await WithSchedulerAsync(host, scheduler =>
            scheduler.RenewLeaseAsync(original!, TestContext.Current.CancellationToken)));
        Assert.False(await WithSchedulerAsync(host, scheduler =>
            scheduler.CompleteAsync(original!, TestContext.Current.CancellationToken)));
        Assert.True(await WithSchedulerAsync(host, scheduler =>
            scheduler.CompleteAsync(usurper!, TestContext.Current.CancellationToken)));
    }

    /// <summary>
    /// Two queued rows for one user run in turn, never at once: two concurrent runners over one
    /// mailbox would double-count every Occurrence they both write.
    /// </summary>
    [Fact]
    public async Task ASecondScanForTheSameUser_WaitsForTheFirstToFinish()
    {
        await using var host = await StartHostAsync();
        var first = await AddScanAsync(host, DateTimeOffset.UtcNow.AddMinutes(-1));
        var second = await AddScanAsync(host, DateTimeOffset.UtcNow);

        var running = await WithSchedulerAsync(host, scheduler =>
            scheduler.ClaimNextAsync(TimeSpan.FromHours(1), TestContext.Current.CancellationToken));
        Assert.Equal(first, running!.Id);

        Assert.Null(await WithSchedulerAsync(host, scheduler =>
            scheduler.ClaimNextAsync(TimeSpan.FromHours(1), TestContext.Current.CancellationToken)));

        Assert.True(await WithSchedulerAsync(host, scheduler =>
            scheduler.CompleteAsync(running, TestContext.Current.CancellationToken)));

        var next = await WithSchedulerAsync(host, scheduler =>
            scheduler.ClaimNextAsync(TimeSpan.FromHours(1), TestContext.Current.CancellationToken));
        Assert.Equal(second, next!.Id);
    }

    /// <summary>
    /// One user's live scan must not hold up the queue. Their own rows wait — that is the point
    /// of the guard — but they are also the oldest rows in the queue while the scan runs, and
    /// stopping at them would mean nobody else is scanned until the lease expires an hour later.
    /// </summary>
    [Fact]
    public async Task AUserWithAScanRunning_DoesNotBlockAnotherUsersQueuedScan()
    {
        await using var host = await StartHostAsync();
        await AddScanAsync(host, DateTimeOffset.UtcNow.AddMinutes(-5));
        var otherUser = await AddScanAsync(
            host,
            DateTimeOffset.UtcNow.AddMinutes(-4),
            userId: "00000000-0000-0000-0000-00000000beef");

        // The blocked user's second row is older than the other user's, so a claim that stops at
        // the first candidate it cannot take never reaches the row it should.
        var running = await WithSchedulerAsync(host, scheduler =>
            scheduler.ClaimNextAsync(TimeSpan.FromHours(1), TestContext.Current.CancellationToken));
        Assert.NotNull(running);
        await AddScanAsync(host, DateTimeOffset.UtcNow.AddMinutes(-6));

        var next = await WithSchedulerAsync(host, scheduler =>
            scheduler.ClaimNextAsync(TimeSpan.FromHours(1), TestContext.Current.CancellationToken));

        Assert.NotNull(next);
        Assert.Equal(otherUser, next.Id);
    }

    /// <summary>The shutdown path: a released row goes back to the queue and is claimable at once.</summary>
    [Fact]
    public async Task AReleasedScan_IsImmediatelyClaimableAgain()
    {
        await using var host = await StartHostAsync();
        var id = await AddScanAsync(host, DateTimeOffset.UtcNow);

        var claimed = await WithSchedulerAsync(host, scheduler =>
            scheduler.ClaimNextAsync(TimeSpan.FromHours(1), TestContext.Current.CancellationToken));

        Assert.True(await WithSchedulerAsync(host, scheduler =>
            scheduler.ReleaseAsync(claimed!, TestContext.Current.CancellationToken)));

        var again = await WithSchedulerAsync(host, scheduler =>
            scheduler.ClaimNextAsync(TimeSpan.FromHours(1), TestContext.Current.CancellationToken));
        Assert.Equal(id, again!.Id);
    }

    /// <summary>The queue is a queue, not an audit log: finished rows past retention go.</summary>
    [Fact]
    public async Task PurgingFinishedScans_SweepsOldRowsAndOnlyThose()
    {
        await using var host = await StartHostAsync();
        await AddScanAsync(host, DateTimeOffset.UtcNow.AddDays(-8), IndexScanState.Completed);
        await AddScanAsync(host, DateTimeOffset.UtcNow.AddDays(-8), IndexScanState.Failed);
        await AddScanAsync(host, DateTimeOffset.UtcNow.AddHours(-1), IndexScanState.Completed);
        await AddScanAsync(host, DateTimeOffset.UtcNow.AddDays(-8));

        var purged = await WithSchedulerAsync(host, scheduler =>
            scheduler.PurgeFinishedAsync(TimeSpan.FromDays(7), TestContext.Current.CancellationToken));

        Assert.Equal(2, purged);
        Assert.Equal(2, await CountScansAsync(host));
    }

    private static async Task<bool> RequestAsync(
        TodoWerkWebApplicationFactory host,
        IndexScanMode mode,
        string? taskListId = null)
    {
        await using var scope = host.Services.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<IndexScanScheduler>()
            .RequestScanAsync(
                new IndexUser(User.TenantId, User.UserId),
                mode,
                taskListId,
                TestContext.Current.CancellationToken);
    }

    private static async Task<int> ScheduleDueAsync(TodoWerkWebApplicationFactory host)
    {
        await using var scope = host.Services.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<IndexScanScheduler>()
            .ScheduleDueSyncsAsync(SyncInterval, IdleAfter, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// A sign-in, recorded through the store the two human sign-in paths write through, so the
    /// row is the one the product would have written — the store is the invariant, not the table.
    /// A moment other than now is then moved back in place, because the store stamps the clock it
    /// is given and these tests run on the real one; the row's shape is still the store's.
    /// </summary>
    private static async Task SignInAsync(TodoWerkWebApplicationFactory host, DateTimeOffset? signedInAt = null)
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var scope = host.Services.CreateAsyncScope();

        await scope.ServiceProvider.GetRequiredService<ITenantMemberStore>()
            .RecordSignInAsync(new IndexUser(User.TenantId, User.UserId), cancellationToken);

        if (signedInAt is null)
        {
            return;
        }

        await scope.ServiceProvider.GetRequiredService<TodoWerkDbContext>()
            .Set<TenantMember>()
            .Where(member => member.TenantId == User.TenantId && member.UserId == User.UserId)
            .ExecuteUpdateAsync(
                set => set.SetProperty(member => member.LastSignedInAt, signedInAt),
                cancellationToken);
    }

    private static async Task AnonymiseAsync(TodoWerkWebApplicationFactory host)
    {
        await using var scope = host.Services.CreateAsyncScope();

        Assert.True(await scope.ServiceProvider.GetRequiredService<ITenantMemberStore>()
            .AnonymiseAsync(new IndexUser(User.TenantId, User.UserId), TestContext.Current.CancellationToken));
    }

    private static async Task<int> CountScansAsync(TodoWerkWebApplicationFactory host)
    {
        await using var scope = host.Services.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<TodoWerkDbContext>()
            .Set<IndexScan>()
            .CountAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<T> WithSchedulerAsync<T>(
        TodoWerkWebApplicationFactory host,
        Func<IndexScanScheduler, Task<T>> action)
    {
        await using var scope = host.Services.CreateAsyncScope();

        return await action(scope.ServiceProvider.GetRequiredService<IndexScanScheduler>());
    }

    /// <summary>A queue row written directly, so a test controls its age and outcome exactly.</summary>
    private static async Task<Guid> AddScanAsync(
        TodoWerkWebApplicationFactory host,
        DateTimeOffset requestedAt,
        IndexScanState? finishedAs = null,
        string? userId = null)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TodoWerkDbContext>();

        var scan = IndexScan.Request(
            User.TenantId,
            userId ?? User.UserId,
            IndexScanMode.Delta,
            taskListId: null,
            requestedAt);

        if (finishedAs is IndexScanState.Completed)
        {
            scan.Complete(requestedAt);
        }
        else if (finishedAs is IndexScanState.Failed)
        {
            scan.Fail("Seeded as failed.", FailureCode.Unknown, requestedAt);
        }

        context.Add(scan);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        return scan.Id;
    }

    private static async Task SeedListAsync(
        TodoWerkWebApplicationFactory host,
        DateTimeOffset? lastAttemptAt,
        DateTimeOffset? lastSuccessfulSyncAt,
        bool failed = false)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TodoWerkDbContext>();

        var state = TaskListIndexState.Create(User.TenantId, User.UserId, "list-arbeit", "Arbeit");

        if (lastAttemptAt is not null)
        {
            state.BeginScan(lastAttemptAt.Value);
        }

        if (lastSuccessfulSyncAt is not null)
        {
            state.CompleteFullScan("delta-token", lastSuccessfulSyncAt.Value);
        }
        else if (failed)
        {
            state.FailScan("Microsoft To Do is rate-limiting this account.", FailureCode.Throttled, lastAttemptAt!.Value);
        }

        context.Add(state);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task<TodoWerkWebApplicationFactory> StartHostAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var host = new TodoWerkWebApplicationFactory()
            .WithConfigurationOverride("ConnectionStrings:TodoWerk", database.ConnectionString)
            // These tests are the worker: they claim, renew and complete rows themselves, so the
            // real one must not be in the host competing for the same queue.
            .WithoutBackgroundWorkers()
            .WithOutboundHttpHandler(() => new FakeEntraAndGraphHandler(new EntraAndGraphRecorder()));

        await using var scope = host.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TodoWerkDbContext>();

        await context.Database.MigrateAsync(cancellationToken);
        await context.Set<TaskListIndexState>().ExecuteDeleteAsync(cancellationToken);
        await context.Set<IndexScan>().ExecuteDeleteAsync(cancellationToken);
        // The schedule reads who is present off this table, so each test says who is.
        await context.Set<TenantMember>().ExecuteDeleteAsync(cancellationToken);

        return host;
    }

    private sealed record IndexUserPair(string TenantId, string UserId);
}
