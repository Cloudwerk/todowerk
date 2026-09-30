using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TodoWerk.Domain.Changes;
using TodoWerk.Domain.Failures;
using TodoWerk.Domain.Hashtags;
using TodoWerk.Domain.Indexing;
using TodoWerk.Infrastructure.Changes;
using TodoWerk.Infrastructure.Changes.Persistence;
using TodoWerk.Infrastructure.Indexing;
using TodoWerk.Application.Abstractions.Authentication;
using TodoWerk.Application.Onboarding;
using TodoWerk.Domain.Onboarding;
using TodoWerk.Infrastructure.Persistence;
using Xunit;

namespace TodoWerk.IntegrationTests;

/// <summary>
/// What a lapsed Licence does to work that is already queued, watched where it happens: inside a
/// worker's tick, over a real database, with real rows in the queue.
/// <para>
/// <c>LicensingTests</c> pins what the gate answers and which endpoints it stands in front of, and
/// the unit suite pins that the gate and the request path answer from one cache. Neither watches
/// the consequence, because the consequence is a row: that a denied person's queued work is still
/// waiting after the tick, that their colleague's ran in the same one, and that a Change already
/// running is not cut off. Those need a database and a worker, which is why they live here rather
/// than beside the rest of the licensing tests.
/// </para>
/// <para>
/// Each test starts its worker itself, once it has finished writing the rows the tick is about —
/// see <see cref="WorkerTick"/> for why a worker started with the host cannot be given that. The
/// poll intervals are pushed out to five minutes so that exactly one tick runs.
/// </para>
/// </summary>
public sealed class WorkerLicensingTests(SqlServerDatabaseFixture database)
    : IClassFixture<SqlServerDatabaseFixture>
{
    private const string ListId = "list-arbeit";

    /// <summary>Somebody the portal turns away, with a scan waiting.</summary>
    private const string DeniedWithAScan = "44444444-4444-4444-4444-444444444444";

    /// <summary>Somebody the portal turns away, with a Change waiting.</summary>
    private const string DeniedWithAChange = "55555555-5555-5555-5555-555555555555";

    /// <summary>Somebody the portal turns away whose Change is already running.</summary>
    private const string DeniedMidChange = "66666666-6666-6666-6666-666666666666";

    /// <summary>Somebody the portal would license, whose last sign-in is beyond the idle window.</summary>
    private const string IdleWithAStaleIndex = "77777777-7777-7777-7777-777777777777";

    private static readonly string[] Denied = [DeniedWithAScan, DeniedWithAChange, DeniedMidChange];

    /// <summary>
    /// A denied person's queued scan survives the tick exactly as it was, and the person beside
    /// them is scanned in that same tick. The two halves are one test on purpose: a gate that
    /// stopped the drain loop rather than stepping over the row it refused would satisfy the first
    /// half and take everybody else's work down with it.
    /// </summary>
    [Fact]
    public async Task TheScanWorker_LeavesADeniedPersonsScanQueued_AndScansTheirColleagueInTheSameTick()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var tenant = new FakeTodoTenant();
        tenant.AddList(ListId, "Arbeit");
        tenant.AddTask(ListId, "t1", "Angebot schreiben #Kunde");

        await using var host = await StartAsync(tenant, cancellationToken);

        // The denied person's row is the older of the two, so the drain loop meets it first.
        var held = await QueueScanAsync(host, DeniedWithAScan, Minutes(-5), cancellationToken);
        var run = await QueueScanAsync(host, ChangeTestHost.User.UserId, Minutes(-1), cancellationToken);

        var settled = await WorkerTick.RunAsync<IndexScanBackgroundService>(
            host.Factory.Services,
            async () =>
            {
                var rows = await ReadScansAsync(host, cancellationToken);

                return rows[run].State is IndexScanState.Completed or IndexScanState.Failed
                    && rows[held] is { State: IndexScanState.Pending, StartedAt: null };
            },
            cancellationToken);

        Assert.True(settled, "the scan worker never finished a tick over the two queued rows");

        var scans = await ReadScansAsync(host, cancellationToken);

        Assert.True(
            scans[run].State is IndexScanState.Completed,
            $"the licensed person's scan ended {scans[run].State}: {scans[run].FailureReason}");

        // Untouched rather than failed or refused: a Licence that comes back finds the work still
        // queued, and nothing on the row says anything happened to it.
        var waiting = scans[held];

        Assert.Equal(IndexScanState.Pending, waiting.State);
        Assert.Null(waiting.StartedAt);
        Assert.Null(waiting.CompletedAt);
        Assert.Equal(FailureCode.None, waiting.FailureCode);
        Assert.Null(waiting.FailureReason);

        // Nothing of theirs was read, and their colleague's mailbox was.
        Assert.Equal(0, await CountIndexedTasksAsync(host, DeniedWithAScan, cancellationToken));
        Assert.True(await CountIndexedTasksAsync(host, ChangeTestHost.User.UserId, cancellationToken) > 0);

        // And the row really was claimed and handed back rather than never reached: nobody is
        // signed in as this person, so the claim is the only thing that could have asked about them.
        Assert.Contains(host.Portal.Calls, call => call.Body.Contains(DeniedWithAScan, StringComparison.Ordinal));
    }

    /// <summary>
    /// A deployment with nobody signed in must not call the portal on the sync schedule; an idle
    /// person is never resolved. So this is the assertion that pins it — not that the idle person
    /// was not <em>scheduled</em>, which the scheduling tests cover, but that the portal was never
    /// asked about them at all.
    /// The present person in the same tick is the control: their stale index is scheduled, their
    /// Licence is resolved, and their scan runs, so a schedule that had quietly stopped for
    /// everybody would fail here too.
    /// </summary>
    [Fact]
    public async Task TheScanWorker_NeverAsksThePortalAboutAnIdlePerson_AndStillSyncsOneWhoIsPresent()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var tenant = new FakeTodoTenant();
        tenant.AddList(ListId, "Arbeit");
        tenant.AddTask(ListId, "t1", "Angebot schreiben #Kunde");

        await using var host = await StartAsync(tenant, cancellationToken);

        // Two stale indexes, one of them somebody's. Fifteen days is one past the default window.
        await SignInAsync(host, IdleWithAStaleIndex, DateTimeOffset.UtcNow.AddDays(-15), cancellationToken);
        await SeedStaleListAsync(host, IdleWithAStaleIndex, cancellationToken);
        await SignInAsync(host, ChangeTestHost.User.UserId, signedInAt: null, cancellationToken);
        await SeedStaleListAsync(host, ChangeTestHost.User.UserId, cancellationToken);

        var settled = await WorkerTick.RunAsync<IndexScanBackgroundService>(
            host.Factory.Services,
            async () =>
            {
                var rows = await ReadScansAsync(host, cancellationToken);

                return rows.Values.Any(scan => scan.UserId == ChangeTestHost.User.UserId
                    && scan.State is IndexScanState.Completed or IndexScanState.Failed);
            },
            cancellationToken);

        Assert.True(settled, "the scan worker never scheduled and finished a sync for the present person");

        var scans = await ReadScansAsync(host, cancellationToken);
        var present = Assert.Single(scans.Values, scan => scan.UserId == ChangeTestHost.User.UserId);

        Assert.True(
            present.State is IndexScanState.Completed,
            $"the present person's scheduled sync ended {present.State}: {present.FailureReason}");
        Assert.Equal(IndexScanMode.Delta, present.Mode);

        // Nothing was queued for the idle person, and — the point — nobody asked about them.
        Assert.DoesNotContain(scans.Values, scan => scan.UserId == IdleWithAStaleIndex);
        Assert.DoesNotContain(
            host.Portal.Calls,
            call => call.Body.Contains(IdleWithAStaleIndex, StringComparison.Ordinal));

        // And the negative above is not the portal having been silent: the present person's
        // claim asked, which is the one call a sync interval is meant to cost.
        Assert.Contains(
            host.Portal.Calls,
            call => call.Body.Contains(ChangeTestHost.User.UserId, StringComparison.Ordinal));
    }

    /// <summary>
    /// The same for the Change queue, in one tick: a denied person's confirmed Change is still
    /// waiting, a second denied person's Change that was already running is not cut off, and their
    /// colleague's Change writes their tasks meanwhile.
    /// </summary>
    [Fact]
    public async Task TheChangeWorker_LeavesADeniedPersonsChangeQueuedAndOneAlreadyRunningAlone_AndRunsTheirColleaguesInTheSameTick()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var tenant = new FakeTodoTenant();
        tenant.AddList(ListId, "Arbeit");
        tenant.AddTask(ListId, "t1", "Angebot schreiben #Prio1");

        await using var host = await StartAsync(tenant, cancellationToken);
        await host.ScanAsync(cancellationToken);

        // Claimed while it is the only row in the queue, because the claim takes the oldest: this
        // is the Change somebody is watching when their Licence lapses, and it holds a live lease
        // as the tick begins.
        var running = await PlanAsync(host, DeniedMidChange, Minutes(-10), cancellationToken);
        var lease = await ClaimAsync(host, cancellationToken);

        Assert.Equal(running, lease.Id);

        // Older than the colleague's confirmation below, so the drain loop meets it first.
        var held = await PlanAsync(host, DeniedWithAChange, Minutes(-5), cancellationToken);

        var confirmed = await ConfirmAsync(host, "Prio1", "Priority1", cancellationToken);

        var settled = await WorkerTick.RunAsync<ChangeBackgroundService>(
            host.Factory.Services,
            async () =>
            {
                var rows = await ReadChangesAsync(host, cancellationToken);

                return rows[confirmed].IsFinished
                    && rows[held] is { State: ChangeState.Pending, StartedAt: null };
            },
            cancellationToken);

        Assert.True(settled, "the change worker never finished a tick over the queued rows");

        var changes = await ReadChangesAsync(host, cancellationToken);

        Assert.True(
            changes[confirmed].State is ChangeState.Completed,
            $"the licensed person's Change ended {changes[confirmed].State}: {changes[confirmed].FailureReason}");

        Assert.Equal("Angebot schreiben #Priority1", host.Tenant.TitleOf(ListId, "t1"));

        // The denied person's Change is where they left it, with nothing written and nothing said.
        var waiting = changes[held];

        Assert.Equal(ChangeState.Pending, waiting.State);
        Assert.Null(waiting.StartedAt);
        Assert.False(waiting.CancelRequested);
        Assert.Equal(0, await CountJournalledWritesAsync(host, held, cancellationToken));

        // The one already running keeps its lease. What a lapsed Licence stops is the next Change
        // starting, not the one somebody is watching — and it is not even asked about, because the
        // claim is what asks and a row that is running is not claimable.
        var midChange = changes[running];

        Assert.Equal(ChangeState.Running, midChange.State);
        Assert.Equal(lease.Lease, midChange.StartedAt);
        Assert.False(midChange.CancelRequested);
        Assert.DoesNotContain(
            host.Portal.Calls,
            call => call.Body.Contains(DeniedMidChange, StringComparison.Ordinal));

        Assert.Contains(host.Portal.Calls, call => call.Body.Contains(DeniedWithAChange, StringComparison.Ordinal));
    }

    /// <summary>
    /// A Hosted Service that denies the three people above and licenses everybody else, which is
    /// the whole of the per-person model — and the only thing these tests configure differently
    /// from the rest of the Change suite.
    /// </summary>
    private Task<ChangeTestHost> StartAsync(FakeTodoTenant tenant, CancellationToken cancellationToken) =>
        ChangeTestHost.StartAsync(
            database.ConnectionString,
            tenant,
            cancellationToken,
            licensing: body => Denied.Any(person => body.Contains(person, StringComparison.Ordinal))
                ? FakeManagementPortal.PortalAnswer.Ok(
                    FakeManagementPortal.Expired("Your trial of TodoWerk ended on 2 October 2026."))
                : FakeManagementPortal.PortalAnswer.Ok(
                    FakeManagementPortal.Valid("tenant", "2027-03-14T00:00:00Z")),
            // One tick and no more: a worker ticks as it starts and then waits this out, so what
            // the assertions read is the state that one drain left.
            settings: new Dictionary<string, string?>
            {
                ["Indexing:PollInterval"] = "00:05:00",
                ["Changes:PollInterval"] = "00:05:00",
            });

    private static DateTimeOffset Minutes(int minutes) => DateTimeOffset.UtcNow.AddMinutes(minutes);

    /// <summary>
    /// A sign-in recorded through the store, as both human sign-in paths record one, and then —
    /// for a person who is meant to have been away — moved back, because the store stamps the
    /// clock it is given and this host runs on the real one.
    /// </summary>
    private static async Task SignInAsync(
        ChangeTestHost host,
        string userId,
        DateTimeOffset? signedInAt,
        CancellationToken cancellationToken)
    {
        await using var scope = host.Scope();

        await scope.ServiceProvider.GetRequiredService<ITenantMemberStore>()
            .RecordSignInAsync(new IndexUser(TodoWerkWebApplicationFactory.TenantId, userId), cancellationToken);

        if (signedInAt is null)
        {
            return;
        }

        await scope.ServiceProvider.GetRequiredService<TodoWerkDbContext>()
            .Set<TenantMember>()
            .Where(member => member.TenantId == TodoWerkWebApplicationFactory.TenantId && member.UserId == userId)
            .ExecuteUpdateAsync(
                set => set.SetProperty(member => member.LastSignedInAt, signedInAt),
                cancellationToken);
    }

    /// <summary>A list that has never been synced, which is as due as a list gets.</summary>
    private static async Task SeedStaleListAsync(
        ChangeTestHost host,
        string userId,
        CancellationToken cancellationToken)
    {
        await using var scope = host.Scope();
        var context = scope.ServiceProvider.GetRequiredService<TodoWerkDbContext>();

        context.Add(TaskListIndexState.Create(TodoWerkWebApplicationFactory.TenantId, userId, ListId, "Arbeit"));
        await context.SaveChangesAsync(cancellationToken);
    }

    private static async Task<Guid> QueueScanAsync(
        ChangeTestHost host,
        string userId,
        DateTimeOffset requestedAt,
        CancellationToken cancellationToken)
    {
        await using var scope = host.Scope();
        var context = scope.ServiceProvider.GetRequiredService<TodoWerkDbContext>();

        var scan = IndexScan.Request(
            TodoWerkWebApplicationFactory.TenantId,
            userId,
            IndexScanMode.Full,
            taskListId: null,
            requestedAt);

        context.Add(scan);
        await context.SaveChangesAsync(cancellationToken);

        return scan.Id;
    }

    /// <summary>
    /// A confirmed Change written straight into the queue. There is one person the fake Entra ID
    /// can sign in, so everybody else's Changes are seeded rather than confirmed — and what these
    /// tests watch is the claim, which cannot tell the difference.
    /// </summary>
    private static async Task<Guid> PlanAsync(
        ChangeTestHost host,
        string userId,
        DateTimeOffset requestedAt,
        CancellationToken cancellationToken)
    {
        await using var scope = host.Scope();
        var context = scope.ServiceProvider.GetRequiredService<TodoWerkDbContext>();

        var change = Change.Plan(
            TodoWerkWebApplicationFactory.TenantId,
            userId,
            [HashtagKey.Fold("Prio1")],
            "Priority1",
            ChangeKind.Rename,
            carriedRule: [],
            plannedTaskCount: 1,
            requestedAt);

        context.Add(change);
        await context.SaveChangesAsync(cancellationToken);

        return change.Id;
    }

    /// <summary>The oldest waiting Change, claimed the way the worker claims one.</summary>
    private static async Task<ClaimedChange> ClaimAsync(ChangeTestHost host, CancellationToken cancellationToken)
    {
        await using var scope = host.Scope();

        var claimed = await scope.ServiceProvider.GetRequiredService<ChangeQueue>()
            .ClaimNextAsync(TimeSpan.FromHours(1), cancellationToken);

        Assert.NotNull(claimed);

        return claimed;
    }

    /// <summary>The signed-in person's own Change, confirmed through the API and left queued.</summary>
    private static async Task<Guid> ConfirmAsync(
        ChangeTestHost host,
        string source,
        string target,
        CancellationToken cancellationToken)
    {
        var result = await host.PostAsync<ConfirmedChange>(
            "/api/changes",
            new { sourceKeys = new[] { HashtagKey.Fold(source) }, targetSpelling = target, confirmMerge = false },
            cancellationToken);

        Assert.True(
            result.StatusCode == HttpStatusCode.Accepted,
            $"confirming the change answered {(int)result.StatusCode}: {result.Detail}");

        return result.Body!.Id;
    }

    private static async Task<Dictionary<Guid, IndexScan>> ReadScansAsync(
        ChangeTestHost host,
        CancellationToken cancellationToken)
    {
        await using var scope = host.Scope();

        return await scope.ServiceProvider.GetRequiredService<TodoWerkDbContext>()
            .Set<IndexScan>()
            .AsNoTracking()
            .ToDictionaryAsync(scan => scan.Id, cancellationToken);
    }

    private static async Task<Dictionary<Guid, Change>> ReadChangesAsync(
        ChangeTestHost host,
        CancellationToken cancellationToken)
    {
        await using var scope = host.Scope();

        return await scope.ServiceProvider.GetRequiredService<TodoWerkDbContext>()
            .Set<Change>()
            .AsNoTracking()
            .ToDictionaryAsync(change => change.Id, cancellationToken);
    }

    /// <summary>How many tasks one Change wrote, read from the only record of them.</summary>
    private static async Task<int> CountJournalledWritesAsync(
        ChangeTestHost host,
        Guid change,
        CancellationToken cancellationToken)
    {
        await using var scope = host.Scope();

        return await scope.ServiceProvider.GetRequiredService<TodoWerkDbContext>()
            .Set<ChangeJournalEntry>()
            .AsNoTracking()
            .CountAsync(entry => entry.ChangeId == change, cancellationToken);
    }

    private static async Task<int> CountIndexedTasksAsync(
        ChangeTestHost host,
        string userId,
        CancellationToken cancellationToken)
    {
        await using var scope = host.Scope();

        return await scope.ServiceProvider.GetRequiredService<TodoWerkDbContext>()
            .Set<IndexedTask>()
            .AsNoTracking()
            .CountAsync(
                task => task.TenantId == TodoWerkWebApplicationFactory.TenantId && task.UserId == userId,
                cancellationToken);
    }

    /// <summary>What <c>/api/changes</c> answers: the id of the Change now in the queue.</summary>
    private sealed record ConfirmedChange(Guid Id);
}
