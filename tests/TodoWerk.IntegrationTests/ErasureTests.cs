using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TodoWerk.Application.Abstractions.Authentication;
using TodoWerk.Application.Licensing;
using TodoWerk.Domain.Changes;
using TodoWerk.Domain.Hashtags;
using TodoWerk.Domain.Indexing;
using TodoWerk.Domain.Markers;
using TodoWerk.Infrastructure.Persistence;
using Xunit;

namespace TodoWerk.IntegrationTests;

/// <summary>
/// "Everything is deleted" as a verified claim rather than an intention.
/// <para>
/// Every test here builds real data first — an index scanned from the fake mailbox, a Change with
/// its plan and journal, a refresh token in the durable cache — because the failure mode worth
/// catching is a purge that misses one table, and a test that erased an empty database would pass
/// while missing all of them.
/// </para>
/// </summary>
public sealed class ErasureTests(SqlServerDatabaseFixture database) : IClassFixture<SqlServerDatabaseFixture>
{
    private const string ErasurePath = "/api/me/erasure";

    /// <summary>Somebody in the same tenant whose data must be untouched by anybody else's erasure.</summary>
    private const string ColleagueObjectId = "33333333-3333-3333-3333-333333333333";

    private static readonly string[] WorkSourceKeys = ["work"];

    [Fact]
    public async Task Erasure_DestroysTheIndexTheChangesTheJournalsAndTheToken_AndSignsThePersonOut()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var host = await StartWithDataAsync(cancellationToken);

        var session = await host.SignInAsync(cancellationToken);
        await host.ScanAsync(cancellationToken);
        await SeedColleagueDataAsync(host, cancellationToken);

        // A colleague who has also signed in, so the tenant's cumulative count is two and the claim
        // "erasure does not erode it" has something to be true of.
        await host.SeedMemberAsync(
            ColleagueObjectId,
            DateTimeOffset.UtcNow.AddDays(-10),
            DateTimeOffset.UtcNow,
            cancellationToken);

        // The sign-in above filled the durable cache, which is what a background job would act on.
        Assert.True(await host.CountTokenCacheRowsAsync(cancellationToken) > 0, "no token cache row to evict");
        Assert.True(await CountAsync<HashtagOccurrence>(host, SignInTestHost.User, cancellationToken) > 0);

        await ConfirmAChangeAsync(host, session, cancellationToken);
        Assert.True(await CountAsync<Change>(host, SignInTestHost.User, cancellationToken) > 0);

        using var response = await host.PostFormAsync(ErasurePath, session, cancellationToken);

        // Signed out at the end, which is a redirect to the identity provider's end-session
        // endpoint — the same answer /auth/sign-out gives.
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains(
            "login.microsoftonline.com",
            response.Headers.Location?.ToString() ?? string.Empty,
            StringComparison.Ordinal);

        Assert.Equal(0, await CountAsync<HashtagOccurrence>(host, SignInTestHost.User, cancellationToken));
        Assert.Equal(0, await CountAsync<IndexedTask>(host, SignInTestHost.User, cancellationToken));
        Assert.Equal(0, await CountAsync<TaskListIndexState>(host, SignInTestHost.User, cancellationToken));
        Assert.Equal(0, await CountAsync<IndexScan>(host, SignInTestHost.User, cancellationToken));
        Assert.Equal(0, await CountAsync<Change>(host, SignInTestHost.User, cancellationToken));
        Assert.Equal(0, await CountAsync<ChangePlanItem>(host, SignInTestHost.User, cancellationToken));
        Assert.Equal(0, await CountAsync<ChangeJournalEntry>(host, SignInTestHost.User, cancellationToken));
        Assert.Equal(0, await CountAsync<MarkerRule>(host, SignInTestHost.User, cancellationToken));

        // Nothing can act as them afterwards.
        Assert.Equal(0, await host.CountTokenCacheRowsAsync(cancellationToken));

        // The membership row survives without an object id, so the tenant's cumulative count is
        // unaffected and nobody remains identifiable in it. Two rows still: theirs and the
        // colleague's, who is untouched.
        var members = await host.ReadMembersAsync(cancellationToken);
        Assert.Equal(2, members.Count);

        var erased = Assert.Single(members, member => member.UserId is null);
        Assert.Null(erased.LastSignedInAt);
        Assert.NotEqual(default, erased.FirstSignedInAt);

        Assert.Single(members, member => member.UserId == ColleagueObjectId);

        // And nobody else was touched.
        var colleague = new IndexUser(TodoWerkWebApplicationFactory.TenantId, ColleagueObjectId);
        Assert.True(await CountAsync<IndexedTask>(host, colleague, cancellationToken) > 0);
        Assert.True(await CountAsync<HashtagOccurrence>(host, colleague, cancellationToken) > 0);
        Assert.True(await CountAsync<Change>(host, colleague, cancellationToken) > 0);
        Assert.True(await CountAsync<MarkerRule>(host, colleague, cancellationToken) > 0);
    }

    /// <summary>
    /// Twelve months without a sign-in has the same effect as asking, because it is the same code —
    /// and this is the test that says so rather than trusting the shared class to stay shared.
    /// </summary>
    [Fact]
    public async Task Sweep_ForgetsSomebodyDormantPastTheWindow_AndLeavesSomebodyInsideItAlone()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var host = await StartWithDataAsync(cancellationToken);

        await host.SignInAsync(cancellationToken);
        await host.ScanAsync(cancellationToken);
        await SeedColleagueDataAsync(host, cancellationToken);

        var now = DateTimeOffset.UtcNow;

        // The colleague went dormant a month past the configured window; the person who really
        // signed in did so moments ago.
        await host.SeedMemberAsync(
            ColleagueObjectId,
            now.AddDays(-800),
            now - ConfiguredDormancyWindow(host) - TimeSpan.FromDays(30),
            cancellationToken);

        var forgotten = await host.SweepAsync(cancellationToken);

        Assert.Equal(1, forgotten);

        var colleague = new IndexUser(TodoWerkWebApplicationFactory.TenantId, ColleagueObjectId);
        Assert.Equal(0, await CountAsync<IndexedTask>(host, colleague, cancellationToken));
        Assert.Equal(0, await CountAsync<HashtagOccurrence>(host, colleague, cancellationToken));
        Assert.Equal(0, await CountAsync<Change>(host, colleague, cancellationToken));
        Assert.Equal(0, await CountAsync<ChangeJournalEntry>(host, colleague, cancellationToken));
        Assert.Equal(0, await CountAsync<MarkerRule>(host, colleague, cancellationToken));

        // The person who is still here kept everything.
        Assert.True(await CountAsync<HashtagOccurrence>(host, SignInTestHost.User, cancellationToken) > 0);
        Assert.True(await CountAsync<MarkerRule>(host, SignInTestHost.User, cancellationToken) > 0);

        // Two membership rows still, one of them anonymised — exactly as for somebody who erased
        // themselves, so the cumulative count behaves the same either way.
        var members = await host.ReadMembersAsync(cancellationToken);
        Assert.Equal(2, members.Count);
        Assert.Single(members, member => member.UserId is null);
    }

    /// <summary>
    /// A restart mid-sweep resumes without erasing twice or skipping anybody. Standing in for the
    /// restart: a second pass, which is what a restarted worker does. It must find nothing, because
    /// the first pass anonymised what it finished.
    /// </summary>
    [Fact]
    public async Task Sweep_RunTwice_ForgetsTheSamePersonOnceAndFindsNobodyTheSecondTime()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var host = await StartWithDataAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var dormantAt = now - ConfiguredDormancyWindow(host) - TimeSpan.FromDays(1);

        await host.SeedMemberAsync(ColleagueObjectId, now.AddDays(-800), dormantAt, cancellationToken);

        Assert.Equal(1, await host.SweepAsync(cancellationToken));
        Assert.Equal(0, await host.SweepAsync(cancellationToken));

        var member = Assert.Single(await host.ReadMembersAsync(cancellationToken));
        Assert.Null(member.UserId);
        Assert.Equal(now.AddDays(-800).ToUnixTimeSeconds(), member.FirstSignedInAt.ToUnixTimeSeconds());
    }

    /// <summary>
    /// The window is configuration, and the test drives the configured value: somebody dormant for
    /// just under it is left alone, and the same person would be forgotten under the default.
    /// </summary>
    [Fact]
    public async Task Sweep_LeavesSomebodyInsideAShortenedWindowAlone()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var host = await StartWithDataAsync(
            cancellationToken,
            new Dictionary<string, string?> { ["Onboarding:DormancyWindow"] = "60.00:00:00" });

        var now = DateTimeOffset.UtcNow;
        var window = ConfiguredDormancyWindow(host);

        Assert.Equal(TimeSpan.FromDays(60), window);

        await host.SeedMemberAsync(
            ColleagueObjectId,
            now.AddDays(-200),
            now - window + TimeSpan.FromDays(1),
            cancellationToken);

        Assert.Equal(0, await host.SweepAsync(cancellationToken));

        var member = Assert.Single(await host.ReadMembersAsync(cancellationToken));
        Assert.Equal(ColleagueObjectId, member.UserId);
    }

    /// <summary>
    /// Somebody shut out by a lapsed Licence can still have everything destroyed. The endpoint
    /// says so — erasure is an obligation rather than a feature of the product, and it carries
    /// <c>AllowUnlicensed</c> for that reason — and <c>LicensingTests</c> pins that the gate is
    /// not what stands in its way. This is the one that says the erasure behind it actually
    /// happens, which takes a database.
    /// </summary>
    [Fact]
    public async Task Erasure_CompletesForSomebodyWhoseLicenceHasEnded()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var host = await StartWithDataAsync(
            cancellationToken,
            licensing: _ => FakeManagementPortal.PortalAnswer.Ok(
                FakeManagementPortal.Expired("Your trial of TodoWerk ended on 2 October 2026.")));

        var session = await host.SignInAsync(cancellationToken);
        await host.ScanAsync(cancellationToken);

        Assert.True(await CountAsync<HashtagOccurrence>(host, SignInTestHost.User, cancellationToken) > 0);
        Assert.True(await host.CountTokenCacheRowsAsync(cancellationToken) > 0, "no token cache row to evict");

        // The gate really is in front of this person, which is what makes the rest of this test
        // about erasure rather than about a host that quietly licensed everybody.
        var refused = await host.GetAsync<JsonElement>("/api/licence", session, cancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Equal(LicensingErrors.Ended.Code, refused.Code);

        using var response = await host.PostFormAsync(ErasurePath, session, cancellationToken);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        Assert.Equal(0, await CountAsync<HashtagOccurrence>(host, SignInTestHost.User, cancellationToken));
        Assert.Equal(0, await CountAsync<IndexedTask>(host, SignInTestHost.User, cancellationToken));
        Assert.Equal(0, await CountAsync<TaskListIndexState>(host, SignInTestHost.User, cancellationToken));
        Assert.Equal(0, await CountAsync<IndexScan>(host, SignInTestHost.User, cancellationToken));
        Assert.Equal(0, await host.CountTokenCacheRowsAsync(cancellationToken));

        var member = Assert.Single(await host.ReadMembersAsync(cancellationToken));

        Assert.Null(member.UserId);
        Assert.Null(member.LastSignedInAt);
    }

    private static Marker MarkerOf(string text)
    {
        Assert.True(Marker.TryCreate(text, out var marker));

        return marker;
    }

    private static TimeSpan ConfiguredDormancyWindow(SignInTestHost host) =>
        host.Factory.Services
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<Application.Onboarding.OnboardingOptions>>()
            .Value.DormancyWindow;

    /// <summary>A host with a mailbox worth indexing behind it.</summary>
    private Task<SignInTestHost> StartWithDataAsync(
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string?>? settings = null,
        Func<string, FakeManagementPortal.PortalAnswer>? licensing = null)
    {
        var tenant = new FakeTodoTenant();
        tenant.AddList("list-1", "Work", "defaultList");
        tenant.AddTask("list-1", "task-1", "Ship it #work");
        tenant.AddTask("list-1", "task-2", "And again #Work");

        return SignInTestHost.StartAsync(
            database.ConnectionString,
            cancellationToken,
            settings,
            tenant,
            licensing: licensing);
    }

    /// <summary>
    /// A colleague's index and Change, written directly. They are not the person any test here
    /// erases, and their rows are the assertion that a purge is bounded by whose it is.
    /// </summary>
    private static async Task SeedColleagueDataAsync(SignInTestHost host, CancellationToken cancellationToken)
    {
        await using var scope = host.Scope();
        var context = scope.ServiceProvider.GetRequiredService<TodoWerkDbContext>();

        var colleague = new IndexUser(TodoWerkWebApplicationFactory.TenantId, ColleagueObjectId);

        var task = IndexedTask.Create(
            colleague.TenantId,
            colleague.UserId,
            "list-1",
            "colleague-task",
            "Their own thing #work",
            DateTimeOffset.UtcNow);

        context.Add(task);
        context.Add(HashtagOccurrence.Create(
            colleague.TenantId,
            colleague.UserId,
            task.Id,
            new ExtractedHashtag(HashtagKey.Fold("work"), "work")));

        // A Marker Rule each. Rules are the first thing TodoWerk stores that a person chose rather
        // than observed (ADR-0014), and erasure has to take them with everything else — theirs, and
        // not the colleague's.
        context.Add(MarkerRule.Create(
            SignInTestHost.User.TenantId,
            SignInTestHost.User.UserId,
            HashtagKey.Fold("work"),
            "work",
            MarkerOf("🍞"),
            10,
            DateTimeOffset.UtcNow));

        context.Add(MarkerRule.Create(
            colleague.TenantId,
            colleague.UserId,
            HashtagKey.Fold("work"),
            "work",
            MarkerOf("☕"),
            10,
            DateTimeOffset.UtcNow));

        var change = Change.Plan(
            colleague.TenantId,
            colleague.UserId,
            WorkSourceKeys,
            "Work",
            ChangeKind.NormaliseCasing,
            carriedRule: [],
            1,
            DateTimeOffset.UtcNow);

        context.Add(change);
        context.Add(ChangeJournalEntry.Record(
            colleague.TenantId,
            colleague.UserId,
            change.Id,
            "list-1",
            "colleague-task",
            "Their own thing #work",
            "Their own thing #Work",
            DateTimeOffset.UtcNow));

        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>A Change of the signed-in person's own, confirmed through the API and then run.</summary>
    private static async Task ConfirmAChangeAsync(
        SignInTestHost host,
        string session,
        CancellationToken cancellationToken)
    {
        var confirmed = await host.PostJsonAsync(
            "/api/changes",
            new { sourceKeys = WorkSourceKeys, targetSpelling = "Work", confirmMerge = false },
            session,
            cancellationToken);

        Assert.Equal(HttpStatusCode.Accepted, confirmed);

        await host.RunQueuedChangeAsync(cancellationToken);
    }

    private static async Task<int> CountAsync<TEntity>(
        SignInTestHost host,
        IndexUser user,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        await using var scope = host.Scope();

        return await scope.ServiceProvider.GetRequiredService<TodoWerkDbContext>()
            .Set<TEntity>()
            .AsNoTracking()
            .Where(row => EF.Property<string>(row, "TenantId") == user.TenantId
                && EF.Property<string>(row, "UserId") == user.UserId)
            .CountAsync(cancellationToken);
    }
}
