using System.Net;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using TodoWerk.Web.Endpoints;
using Xunit;

namespace TodoWerk.IntegrationTests;

/// <summary>
/// What signing in records, and what a tenant is shown about itself. Every test here signs in for
/// real, because "somebody signed in, therefore ..." is the claim under all of it — a suite that
/// minted a cookie instead would report every one of these green with the write missing entirely.
/// </summary>
public sealed class TenantOverviewTests(SqlServerDatabaseFixture database) : IClassFixture<SqlServerDatabaseFixture>
{
    private const string OverviewPath = "/api/tenant/overview";

    /// <summary>
    /// Two more than the default of five, so a test that seeds "enough colleagues" is seeding
    /// against the configured number rather than against a literal that would silently stop
    /// agreeing with it.
    /// </summary>
    private const int Floor = 7;

    private static Dictionary<string, string?> WithFloor(int floor) => new()
    {
        ["Onboarding:StatisticsFloor"] = floor.ToString(System.Globalization.CultureInfo.InvariantCulture),
    };

    [Fact]
    public async Task SignIn_RecordsAMembershipWithBothMomentsSet()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var host = await SignInTestHost.StartAsync(database.ConnectionString, cancellationToken);

        await host.SignInAsync(cancellationToken);

        var member = Assert.Single(await host.ReadMembersAsync(cancellationToken));

        Assert.Equal(FakeEntraAndGraphHandler.UserObjectId, member.UserId);
        Assert.NotNull(member.LastSignedInAt);
        Assert.Equal(member.FirstSignedInAt, member.LastSignedInAt);

        // The tenant claim, not a domain lifted off the UPN. The fake signs in
        // signed-in@todowerk.test, so a UPN-derived tenant could never be this guid — which is the
        // point: one tenant holds several verified domains, and a guest's UPN names another
        // organisation entirely.
        Assert.Equal(TodoWerkWebApplicationFactory.TenantId, member.TenantId);
    }

    [Fact]
    public async Task SecondSignIn_MovesTheLastMomentAndLeavesTheFirst()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var host = await SignInTestHost.StartAsync(database.ConnectionString, cancellationToken);

        await host.SignInAsync(cancellationToken);
        var first = Assert.Single(await host.ReadMembersAsync(cancellationToken));

        await host.SignInAsync(cancellationToken);
        var second = Assert.Single(await host.ReadMembersAsync(cancellationToken));

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(first.FirstSignedInAt, second.FirstSignedInAt);

        // Strictly greater, or the assertion is vacuous: under one clock, >= passes even when the
        // second sign-in never moves the moment at all. Strict inequality is still deterministic —
        // the column holds 100-nanosecond ticks and a whole second sign-in round trip sits between
        // the two writes, so equal moments can only mean the update did not happen.
        Assert.True(
            second.LastSignedInAt > first.LastSignedInAt,
            "the second sign-in did not move the last moment forward");
    }

    /// <summary>
    /// The distinction the activity counts are made of. A scan running on a timer is the product
    /// working, not somebody using it, and if a background pass touched this row then "active in the
    /// last thirty days" would mean nothing at all.
    /// </summary>
    [Fact]
    public async Task AScan_NeitherCreatesNorTouchesAMembershipRecord()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var tenant = new FakeTodoTenant();
        tenant.AddList("list-1", "Work", "defaultList");
        tenant.AddTask("list-1", "task-1", "Ship it #work");

        await using var host = await SignInTestHost.StartAsync(
            database.ConnectionString,
            cancellationToken,
            tenant: tenant);

        // Nobody has signed in, and a full scan runs anyway — the token comes from the cache the
        // sign-in below will fill, so scan first with nothing there, then sign in and scan again.
        await host.ScanAsync(cancellationToken);
        Assert.Empty(await host.ReadMembersAsync(cancellationToken));

        await host.SignInAsync(cancellationToken);
        var afterSignIn = Assert.Single(await host.ReadMembersAsync(cancellationToken));

        await host.ScanAsync(cancellationToken);
        var afterScan = Assert.Single(await host.ReadMembersAsync(cancellationToken));

        Assert.Equal(afterSignIn.LastSignedInAt, afterScan.LastSignedInAt);
    }

    [Fact]
    public async Task Overview_BelowTheFloor_WithholdsTheStatisticsFromTheResponse()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var host = await SignInTestHost.StartAsync(
            database.ConnectionString,
            cancellationToken,
            WithFloor(Floor));

        var session = await host.SignInAsync(cancellationToken);

        // One short of the configured floor, so the boundary is the thing under test rather than
        // "hardly anybody has signed in".
        await SeedColleaguesAsync(host, Floor - 2, DateTimeOffset.UtcNow, cancellationToken);

        var overview = await host.GetAsync<TenantOverviewPayload>(OverviewPath, session, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, overview.StatusCode);
        Assert.Null(overview.Body?.Statistics);
    }

    [Fact]
    public async Task Overview_AtTheFloor_ReportsTheCountsTheWindowsAndTheAverage()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var tenant = new FakeTodoTenant();
        tenant.AddList("list-1", "Work", "defaultList");
        tenant.AddTask("list-1", "task-1", "Ship it #work #urgent");
        tenant.AddTask("list-1", "task-2", "And again #work");

        await using var host = await SignInTestHost.StartAsync(
            database.ConnectionString,
            cancellationToken,
            WithFloor(Floor),
            tenant);

        var session = await host.SignInAsync(cancellationToken);
        await host.ScanAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;

        // Exactly the floor of identifiable people, counting the person who really signed in. One
        // colleague has not been back for two hundred days, so the trailing windows have something
        // to disagree about — and two more have been forgotten entirely, so the cumulative total,
        // the floor and the average's denominator have three different things to be right about.
        await SeedColleaguesAsync(host, Floor - 2, now, cancellationToken);
        await host.SeedMemberAsync("dormant-colleague", now.AddDays(-300), now.AddDays(-200), cancellationToken);
        await host.SeedAnonymisedMemberAsync(now.AddDays(-400), cancellationToken);
        await host.SeedAnonymisedMemberAsync(now.AddDays(-500), cancellationToken);

        var overview = await host.GetAsync<TenantOverviewPayload>(OverviewPath, session, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, overview.StatusCode);

        var statistics = overview.Body?.Statistics;
        Assert.NotNull(statistics);

        // The headline count is cumulative — the forgotten stay counted (ADR-0009).
        Assert.Equal(Floor + 2, statistics.MemberCount);

        // Three Occurrences over three tasks — #work twice and #urgent once — divided by the people
        // still on record, rounded to one place. Dividing by the cumulative count would give
        // 3 / (Floor + 2): the forgotten hold nothing, and they must not water the average down.
        Assert.Equal(3, statistics.TotalOccurrences);
        Assert.Equal(Math.Round(3d / Floor, 1), statistics.AverageOccurrencesPerMember);

        // The windows, shortest first. Everybody but the dormant colleague is inside all three; the
        // dormant one is inside the year alone, which is what proves the counts come off the last
        // moment rather than off the row's existence.
        Assert.Equal([30, 90, 365], statistics.Activity.Select(window => window.WindowDays));
        Assert.Equal([Floor - 1, Floor - 1, Floor], statistics.Activity.Select(window => window.MemberCount));

        Assert.True(statistics.FirstSignedInAt <= now, "the first moment is in the future");
    }

    /// <summary>
    /// The promise the whole screen rests on. Asserted against the raw body rather than the parsed
    /// one, because a field nobody deserialises is still a field somebody received.
    /// </summary>
    [Fact]
    public async Task Overview_NamesNobody_AndCarriesNoPerPersonFigure()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var host = await SignInTestHost.StartAsync(
            database.ConnectionString,
            cancellationToken,
            WithFloor(2));

        var session = await host.SignInAsync(cancellationToken);
        await SeedColleaguesAsync(host, 3, DateTimeOffset.UtcNow, cancellationToken);

        var body = await host.GetRawAsync(OverviewPath, session, cancellationToken);

        Assert.DoesNotContain(FakeEntraAndGraphHandler.UserObjectId, body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("signed-in@todowerk.test", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("userId", body, StringComparison.OrdinalIgnoreCase);
        // The prefix the seeding helper really writes — asserting a word the data never contained
        // would pass whatever the response leaked.
        Assert.DoesNotContain("seeded", body, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The floor protects the people the figures still describe, so somebody who has been forgotten
    /// counts toward the total the screen shows and never toward the floor. In a tenant of six where
    /// five erased themselves, every number on the screen would be about one identifiable colleague
    /// — which is exactly the inference the floor exists to prevent.
    /// </summary>
    [Fact]
    public async Task Overview_AnonymisedMembers_CountInTheTotalButNeverTowardTheFloor()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var host = await SignInTestHost.StartAsync(
            database.ConnectionString,
            cancellationToken,
            WithFloor(3));

        var session = await host.SignInAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;

        // Two identifiable people — one short of the floor — and five forgotten ones. Cumulatively
        // seven, which sails past any floor the cumulative count were measured against.
        await SeedColleaguesAsync(host, 1, now, cancellationToken);

        for (var index = 0; index < 5; index++)
        {
            await host.SeedAnonymisedMemberAsync(now.AddDays(-400), cancellationToken);
        }

        var below = await host.GetAsync<TenantOverviewPayload>(OverviewPath, session, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, below.StatusCode);
        Assert.Null(below.Body?.Statistics);

        // One more identifiable person reaches the floor — and the total the screen then shows is
        // the cumulative eight, forgotten members included.
        await host.SeedMemberAsync("seeded-third-identifiable", now.AddDays(-1), now, cancellationToken);

        var at = await host.GetAsync<TenantOverviewPayload>(OverviewPath, session, cancellationToken);

        Assert.NotNull(at.Body?.Statistics);
        Assert.Equal(8, at.Body.Statistics.MemberCount);
    }

    /// <summary>
    /// A tenant with no membership rows at all is reachable: the membership write is logged rather
    /// than fatal, so a person can be signed in with nothing recorded. The overview has to withhold
    /// the statistics for them like any under-floor tenant — never fail. This pins the aggregate
    /// query's empty shape, where a non-nullable MIN materialised from no rows is a 500.
    /// </summary>
    [Fact]
    public async Task Overview_ForATenantWithNoMembershipRecords_WithholdsStatisticsRatherThanFailing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var host = await SignInTestHost.StartAsync(database.ConnectionString, cancellationToken);

        // A minted session, deliberately: completing a real sign-in is exactly what would write the
        // membership row this test needs absent.
        var session = $"{TestSession.SessionCookieName}={TestSession.ProtectTicket(host.Factory)}";

        var overview = await host.GetAsync<TenantOverviewPayload>(OverviewPath, session, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, overview.StatusCode);
        Assert.Null(overview.Body?.Statistics);
        Assert.False(overview.Body?.TenantConsentGrantedThroughTodoWerk);
    }

    [Fact]
    public async Task Overview_WithoutASession_IsRefusedAsProblemDetails()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var host = await SignInTestHost.StartAsync(database.ConnectionString, cancellationToken);

        using var response = await host.GetAnonymousAsync(OverviewPath, cancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.Contains(
            "json",
            response.Content.Headers.ContentType?.MediaType ?? string.Empty,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task TenantConsent_BeforeAnyGrant_OffersTheAdminConsentLinkForTheScopesSignInAlreadyUses()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var host = await SignInTestHost.StartAsync(database.ConnectionString, cancellationToken);

        var session = await host.SignInAsync(cancellationToken);

        var overview = await host.GetAsync<TenantOverviewPayload>(OverviewPath, session, cancellationToken);
        Assert.False(overview.Body?.TenantConsentGrantedThroughTodoWerk);

        var start = await host.StartTenantConsentAsync(session, cancellationToken);

        var location = start.Location?.ToString() ?? string.Empty;

        Assert.Equal(HttpStatusCode.Redirect, start.StatusCode);
        Assert.Contains($"/{TodoWerkWebApplicationFactory.TenantId}/v2.0/adminconsent", location, StringComparison.Ordinal);
        Assert.Contains($"client_id={TodoWerkWebApplicationFactory.ClientId}", location, StringComparison.Ordinal);
        Assert.Contains("Tasks.ReadWrite", location, StringComparison.Ordinal);

        // Nothing wider than sign-in asks for: no .default, and no All-suffixed application
        // permission. ADR-0008 is the reason, and this is the line that keeps it true.
        Assert.DoesNotContain(".default", location, StringComparison.Ordinal);
        Assert.DoesNotContain("Tasks.ReadWrite.All", location, StringComparison.Ordinal);
        Assert.DoesNotContain("Tasks.Read.All", location, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TenantConsent_ApprovedAndReturned_IsRecordedAgainstTheTenant()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var host = await SignInTestHost.StartAsync(database.ConnectionString, cancellationToken);

        var session = await host.SignInAsync(cancellationToken);

        var callback = await host.CompleteTenantConsentAsync(
            session,
            adminConsent: "True",
            tenant: TodoWerkWebApplicationFactory.TenantId,
            cancellationToken);

        // Back to the screen the link was on, whatever happened.
        Assert.Equal(HttpStatusCode.Redirect, callback);

        var overview = await host.GetAsync<TenantOverviewPayload>(OverviewPath, session, cancellationToken);

        Assert.True(overview.Body?.TenantConsentGrantedThroughTodoWerk);
    }

    /// <summary>
    /// The invitation has no floor and the statistics do. That combination is how the feature gets
    /// found at all: the tenants most likely to need an administrator to approve are the ones too
    /// small to be shown any numbers.
    /// </summary>
    [Fact]
    public async Task TenantConsent_IsOfferedBelowTheFloorWhereTheStatisticsAreNot()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var host = await SignInTestHost.StartAsync(
            database.ConnectionString,
            cancellationToken,
            WithFloor(Floor));

        var session = await host.SignInAsync(cancellationToken);

        var overview = await host.GetAsync<TenantOverviewPayload>(OverviewPath, session, cancellationToken);

        Assert.Null(overview.Body?.Statistics);
        Assert.False(overview.Body?.TenantConsentGrantedThroughTodoWerk);

        var start = await host.StartTenantConsentAsync(session, cancellationToken);

        Assert.Equal(HttpStatusCode.Redirect, start.StatusCode);
    }

    [Theory]
    // Something went wrong and Microsoft did not say True.
    [InlineData("False", TodoWerkWebApplicationFactory.TenantId, null)]
    [InlineData(null, TodoWerkWebApplicationFactory.TenantId, null)]
    // Approved, but naming a tenant this flow was not started for. Microsoft's own documentation
    // warns that this parameter can be forged to impersonate a response, and a recorded grant for
    // somebody else's tenant would switch off their invitation to approve.
    [InlineData("True", "99999999-9999-9999-9999-999999999999", null)]
    // The administrator declined. On a decline Entra returns admin_consent=True with
    // error=consent_required (AADSTS65004) and no tenant — the flag says which flow this was, not
    // what the administrator decided, and read alone it would record a decline as an approval.
    [InlineData("True", null, "consent_required")]
    // An error outranks an otherwise success-shaped callback, whatever else it carries.
    [InlineData("True", TodoWerkWebApplicationFactory.TenantId, "consent_required")]
    // A success that omits the tenant is not believed either: Microsoft's documented success
    // always names it, and a decline is exactly a tenant-less admin_consent=True.
    [InlineData("True", null, null)]
    public async Task TenantConsent_ACallbackThatDoesNotIndicateSuccessForThisTenant_RecordsNothing(
        string? adminConsent,
        string? tenant,
        string? error)
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var host = await SignInTestHost.StartAsync(database.ConnectionString, cancellationToken);

        var session = await host.SignInAsync(cancellationToken);

        var callback = await host.CompleteTenantConsentAsync(session, adminConsent, tenant, cancellationToken, error);

        Assert.Equal(HttpStatusCode.Redirect, callback);

        var overview = await host.GetAsync<TenantOverviewPayload>(OverviewPath, session, cancellationToken);

        Assert.False(overview.Body?.TenantConsentGrantedThroughTodoWerk);
    }

    /// <summary>
    /// The second caller this link has: somebody whose sign-in has just ended without an approval.
    /// They have no session and cannot be sent to get one — the sign-in they would be sent to is
    /// the one that just refused them — so the flow starts anyway, addressed to this deployment's
    /// own authority, and Microsoft asks whoever presses it to sign in as somebody who can approve.
    /// </summary>
    [Fact]
    public async Task TenantConsent_StartedByNobodySignedIn_GoesToMicrosoftRatherThanBackToTheSignInThatRefusedThem()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var host = await SignInTestHost.StartAsync(database.ConnectionString, cancellationToken);

        var start = await host.StartTenantConsentAsync(session: null, cancellationToken);

        var location = start.Location?.ToString() ?? string.Empty;

        Assert.Equal(HttpStatusCode.Redirect, start.StatusCode);
        Assert.Contains(
            $"/{TodoWerkWebApplicationFactory.TenantId}/v2.0/adminconsent",
            location,
            StringComparison.Ordinal);

        // The loop this replaced: sending them to sign in again was sending them back to the screen
        // they had just been unable to get past.
        Assert.DoesNotContain("/auth/sign-in", location, StringComparison.Ordinal);

        Assert.NotEmpty(start.StateCookie);
    }

    /// <summary>
    /// An approval that came back to a flow nobody was signed in for: the person is told, and
    /// nothing is written. The state bound to no tenant, so the tenant naming itself on the way
    /// back is a parameter rather than a fact — and Microsoft's own guidance is that it can be
    /// forged. Recording it would let anybody switch off any organisation's invitation.
    /// </summary>
    [Fact]
    public async Task TenantConsent_ApprovedWithoutASession_SaysSoAndRecordsNothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var host = await SignInTestHost.StartAsync(database.ConnectionString, cancellationToken);

        using var callback = await host.CompleteTenantConsentResponseAsync(
            session: null,
            adminConsent: "True",
            tenant: TodoWerkWebApplicationFactory.TenantId,
            cancellationToken);

        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        Assert.Equal(
            $"/?{OnboardingEndpoints.ConsentParameter}={OnboardingEndpoints.Granted}",
            callback.Headers.Location?.ToString());

        var session = await host.SignInAsync(cancellationToken);
        var overview = await host.GetAsync<TenantOverviewPayload>(OverviewPath, session, cancellationToken);

        Assert.False(overview.Body?.TenantConsentGrantedThroughTodoWerk);
    }

    /// <summary>
    /// The Teams tab runs this round trip in a popup, whose last stop is the tab's auth-end
    /// document — and that document reports back to the tab. So the outcome travels with the
    /// return URL: a popup that reported success for an approval nobody granted would have the tab
    /// announce a grant that does not exist and redraw the same invitation with no explanation.
    /// </summary>
    [Theory]
    [InlineData("True", TodoWerkWebApplicationFactory.TenantId, null, OnboardingEndpoints.Granted)]
    [InlineData("True", null, "consent_required", OnboardingEndpoints.NotGranted)]
    public async Task TenantConsent_AnsweredInsideTheTeamsPopup_CarriesTheOutcomeToTheDocumentTheTabListensTo(
        string? adminConsent,
        string? tenant,
        string? error,
        string expected)
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var host = await SignInTestHost.StartAsync(database.ConnectionString, cancellationToken);

        var session = await host.SignInAsync(cancellationToken);

        using var callback = await host.CompleteTenantConsentResponseAsync(
            session,
            adminConsent,
            tenant,
            cancellationToken,
            error,
            returnUrl: "/teams/auth-end");

        Assert.Equal(
            $"/teams/auth-end?{OnboardingEndpoints.ConsentParameter}={expected}",
            callback.Headers.Location?.ToString());
    }

    /// <summary>
    /// A declined approval must leave a trace somewhere other than the administrator's browser:
    /// the callback records nothing on purpose (ADR-0008), so without a log line "did last night's
    /// approval attempt work?" would have no answer.
    /// <para>
    /// The line is observability over untrusted input — every parameter on that callback is the
    /// caller's — so it is also the line a crafted error must not be able to write a second one
    /// beside.
    /// </para>
    /// </summary>
    [Fact]
    public async Task TenantConsent_Declined_LeavesATraceAnOperatorCanRead()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var logs = new RecordedLogs();

        await using var host = await SignInTestHost.StartAsync(
            database.ConnectionString,
            cancellationToken,
            logs: logs);

        var session = await host.SignInAsync(cancellationToken);

        // The shape Entra returns on a decline, with a forged line break riding in the error,
        // which is the thing that must not survive to the log.
        await host.CompleteTenantConsentAsync(
            session,
            adminConsent: "True",
            tenant: null,
            cancellationToken,
            error: "consent_required\r\nfail: TodoWerk[0] Tenant Consent was approved.");

        var line = Assert.Single(logs.Records, record =>
            record.Message.Contains("did not end in an approval", StringComparison.Ordinal));

        Assert.Equal(LogLevel.Warning, line.Level);
        Assert.Contains("consent_required", line.Message, StringComparison.Ordinal);

        // The three facts an operator needs: was a state present, did it match, was a tenant named.
        Assert.Contains("state was present", line.Message, StringComparison.Ordinal);
        Assert.Contains("matched one this deployment issued", line.Message, StringComparison.Ordinal);
        Assert.Contains("tenant was not named", line.Message, StringComparison.Ordinal);

        Assert.DoesNotContain('\n', line.Message);
        Assert.DoesNotContain('\r', line.Message);
    }

    /// <summary>
    /// A callback naming somebody else's tenant is the impersonation Microsoft's documentation
    /// warns about, and it must not read like an ordinary decline in the log — still less like an
    /// approval. Nothing is recorded either way; what is asserted here is that an operator can
    /// tell the two apart.
    /// </summary>
    [Fact]
    public async Task TenantConsent_AnsweredForATenantTheFlowDidNotBeginFor_IsLoggedAsThatRatherThanAsAnApproval()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var logs = new RecordedLogs();

        await using var host = await SignInTestHost.StartAsync(
            database.ConnectionString,
            cancellationToken,
            logs: logs);

        var session = await host.SignInAsync(cancellationToken);

        await host.CompleteTenantConsentAsync(
            session,
            adminConsent: "True",
            tenant: "99999999-9999-9999-9999-999999999999",
            cancellationToken);

        var line = Assert.Single(logs.Records, record =>
            record.Message.Contains("did not end in an approval", StringComparison.Ordinal));

        Assert.Equal(LogLevel.Warning, line.Level);
        Assert.Contains("not the one the flow began for", line.Message, StringComparison.Ordinal);

        // And nothing anywhere claims an approval landed.
        Assert.DoesNotContain(logs.Records, record =>
            record.Message.Contains("was approved", StringComparison.Ordinal));
    }

    /// <summary>
    /// A callback nobody started. The state cookie is the only thing that makes the redirect
    /// believable, and without one there is nothing here to record.
    /// </summary>
    [Fact]
    public async Task TenantConsent_ACallbackWithNoStateOfItsOwn_RecordsNothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var host = await SignInTestHost.StartAsync(database.ConnectionString, cancellationToken);

        var session = await host.SignInAsync(cancellationToken);

        using var response = await host.GetAnonymousAsync(
            "/auth/tenant-consent/callback"
            + $"?admin_consent=True&tenant={TodoWerkWebApplicationFactory.TenantId}&state=invented",
            cancellationToken);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        var overview = await host.GetAsync<TenantOverviewPayload>(OverviewPath, session, cancellationToken);

        Assert.False(overview.Body?.TenantConsentGrantedThroughTodoWerk);
    }

    private static async Task SeedColleaguesAsync(
        SignInTestHost host,
        int count,
        DateTimeOffset lastSignedInAt,
        CancellationToken cancellationToken)
    {
        for (var index = 0; index < count; index++)
        {
            await host.SeedMemberAsync(
                $"seeded-{index}",
                lastSignedInAt.AddDays(-30),
                lastSignedInAt,
                cancellationToken);
        }
    }

    /// <summary>Mirrors <c>TenantOverviewDto</c>, so a contract change fails here rather than in the UI.</summary>
    private sealed record TenantOverviewPayload(
        TenantStatisticsPayload? Statistics,
        bool TenantConsentGrantedThroughTodoWerk);

    private sealed record TenantStatisticsPayload(
        int MemberCount,
        IReadOnlyList<TenantActivityWindowPayload> Activity,
        DateTimeOffset FirstSignedInAt,
        long TotalOccurrences,
        double AverageOccurrencesPerMember);

    private sealed record TenantActivityWindowPayload(
        [property: JsonPropertyName("windowDays")] int WindowDays,
        [property: JsonPropertyName("memberCount")] int MemberCount);
}
