using System.Net;
using Microsoft.Extensions.Logging;
using TodoWerk.Infrastructure.Onboarding;
using TodoWerk.Web.Endpoints;
using Xunit;

namespace TodoWerk.IntegrationTests;

/// <summary>
/// The Approval Link: the start of the Tenant Consent round trip, carrying its tenant, for the
/// administrator who keeps a separate account for administration and so never has a session here
/// (ADR-0015).
/// <para>
/// Most of these follow a link minted for a tenant other than the one the deployment is registered
/// in. In this suite the two would otherwise be the same guid, and a flow sent to the deployment's
/// own authority would pass every assertion about being sent to the link's.
/// </para>
/// </summary>
public sealed class ApprovalLinkTests(SqlServerDatabaseFixture database) : IClassFixture<SqlServerDatabaseFixture>
{
    /// <summary>The organisation the link was minted for, and not the one the deployment is in.</summary>
    private const string LinkedTenant = "22222222-2222-2222-2222-222222222222";

    /// <summary>Minted here, for the right tenant, a minute more than a week ago.</summary>
    private const string Expired = "expired";

    /// <summary>Minted here this moment, with one character of the token changed.</summary>
    private const string Altered = "altered";

    /// <summary>Never minted at all: what somebody types who has guessed the parameter's name.</summary>
    private const string Invented = "invented";

    [Fact]
    public async Task FollowedWithNobodySignedIn_GoesToMicrosoftAddressedToTheLinksTenant()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var host = await SignInTestHost.StartAsync(database.ConnectionString, cancellationToken);

        var start = await host.StartTenantConsentAsync(
            session: null,
            cancellationToken,
            approval: host.MintApprovalToken(LinkedTenant));

        var location = start.Location?.ToString() ?? string.Empty;

        Assert.Equal(HttpStatusCode.Redirect, start.StatusCode);
        Assert.Contains($"/{LinkedTenant}/v2.0/adminconsent", location, StringComparison.Ordinal);
        Assert.DoesNotContain($"/{TodoWerkWebApplicationFactory.TenantId}/", location, StringComparison.Ordinal);

        // The same grant every other route asks for, and nothing wider (ADR-0008).
        Assert.Contains("Tasks.ReadWrite", location, StringComparison.Ordinal);
        Assert.DoesNotContain(".default", location, StringComparison.Ordinal);
    }

    /// <summary>
    /// The point of the link. Nobody is signed in on either leg, the callback's rule is the one
    /// every flow meets, and the grant is recorded — so the invitation goes away for everybody in
    /// the organisation, which an approval with no link could never do.
    /// </summary>
    [Fact]
    public async Task ApprovedThroughIt_IsRecordedWithoutASession_AndTheAnswerGoesToTheFrontDoor()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var host = await SignInTestHost.StartAsync(database.ConnectionString, cancellationToken);

        using var callback = await host.CompleteTenantConsentResponseAsync(
            session: null,
            adminConsent: "True",
            tenant: LinkedTenant,
            cancellationToken,
            approval: host.MintApprovalToken(LinkedTenant));

        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);

        // Not the Tenant Overview, which is behind a sign-in the administrator should not be asked
        // for: their administration account would become a TodoWerk user.
        Assert.Equal(
            $"/?{OnboardingEndpoints.ConsentParameter}={OnboardingEndpoints.Granted}",
            callback.Headers.Location?.ToString());

        Assert.True(await host.IsTenantConsentGrantedAsync(LinkedTenant, cancellationToken));
        Assert.False(await host.IsTenantConsentGrantedAsync(TodoWerkWebApplicationFactory.TenantId, cancellationToken));
    }

    /// <summary>
    /// The callback's rule did not change for the link. Microsoft naming any tenant but the one the
    /// link carries is the impersonation its documentation warns about, and is believed no more
    /// here than anywhere else.
    /// </summary>
    [Theory]
    [InlineData("True", TodoWerkWebApplicationFactory.TenantId, null)]
    [InlineData("True", null, "consent_required")]
    [InlineData("True", null, null)]
    [InlineData("False", LinkedTenant, null)]
    public async Task ACallbackThatDoesNotIndicateSuccessForTheLinksTenant_RecordsNothing(
        string adminConsent,
        string? tenant,
        string? error)
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var host = await SignInTestHost.StartAsync(database.ConnectionString, cancellationToken);

        using var callback = await host.CompleteTenantConsentResponseAsync(
            session: null,
            adminConsent,
            tenant,
            cancellationToken,
            error,
            approval: host.MintApprovalToken(LinkedTenant));

        Assert.Equal(
            $"/?{OnboardingEndpoints.ConsentParameter}={OnboardingEndpoints.NotGranted}",
            callback.Headers.Location?.ToString());

        Assert.False(await host.IsTenantConsentGrantedAsync(LinkedTenant, cancellationToken));
        Assert.False(await host.IsTenantConsentGrantedAsync(TodoWerkWebApplicationFactory.TenantId, cancellationToken));
    }

    /// <summary>
    /// A link this deployment cannot believe is no link at all. The administrator is still taken to
    /// Microsoft and can still approve — the flow is simply as unbound as one started with nothing,
    /// so it reports the approval and records nothing, whatever tenant comes back.
    /// </summary>
    [Theory]
    [InlineData(Expired)]
    [InlineData(Altered)]
    [InlineData(Invented)]
    public async Task ALinkThatCannotBeBelieved_StillReachesMicrosoft_AndRecordsNothing(string kind)
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var host = await SignInTestHost.StartAsync(database.ConnectionString, cancellationToken);

        var token = Unbelievable(host, kind);

        var start = await host.StartTenantConsentAsync(session: null, cancellationToken, approval: token);

        // The deployment's own authority — the unbound flow — rather than the tenant the link
        // claimed. This deployment is registered in the suite's tenant, so that is where it goes.
        Assert.Equal(HttpStatusCode.Redirect, start.StatusCode);
        Assert.DoesNotContain(LinkedTenant, start.Location?.ToString() ?? string.Empty, StringComparison.Ordinal);

        using var callback = await host.CompleteTenantConsentResponseAsync(
            session: null,
            adminConsent: "True",
            tenant: LinkedTenant,
            cancellationToken,
            approval: token);

        Assert.Equal(
            $"/?{OnboardingEndpoints.ConsentParameter}={OnboardingEndpoints.Granted}",
            callback.Headers.Location?.ToString());

        Assert.False(await host.IsTenantConsentGrantedAsync(LinkedTenant, cancellationToken));
    }

    /// <summary>
    /// The operator's half of the same: an approval that came by a link and left no record has to
    /// say why somewhere other than the administrator's browser — and the line must not carry the
    /// token, which is the caller's and is not the operator's to keep.
    /// </summary>
    [Fact]
    public async Task ALinkThatCannotBeBelieved_LeavesALineForTheOperator()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var logs = new RecordedLogs();

        await using var host = await SignInTestHost.StartAsync(
            database.ConnectionString,
            cancellationToken,
            logs: logs);

        var token = Unbelievable(host, Expired);

        await host.StartTenantConsentAsync(session: null, cancellationToken, approval: token);

        var line = Assert.Single(logs.Records, record =>
            record.Message.Contains("Approval Link", StringComparison.Ordinal));

        Assert.Equal(LogLevel.Information, line.Level);
        Assert.Contains("record nothing", line.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(token, line.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Somebody signed in to one organisation who opens a link made for another — a consultant, an
    /// administrator of several — means to approve the one the link names. Their session's tenant
    /// would send them to approve their own.
    /// </summary>
    [Fact]
    public async Task OpenedByASessionInAnotherTenant_ApprovesTheLinksTenantAndNotTheSessions()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var host = await SignInTestHost.StartAsync(database.ConnectionString, cancellationToken);

        var session = await host.SignInAsync(cancellationToken);
        var token = host.MintApprovalToken(LinkedTenant);

        var start = await host.StartTenantConsentAsync(session, cancellationToken, approval: token);

        Assert.Contains(
            $"/{LinkedTenant}/v2.0/adminconsent",
            start.Location?.ToString() ?? string.Empty,
            StringComparison.Ordinal);

        using var callback = await host.CompleteTenantConsentResponseAsync(
            session,
            adminConsent: "True",
            tenant: LinkedTenant,
            cancellationToken,
            approval: token);

        // Their own tenant's overview would show an invitation for a different organisation's
        // approval, so the answer goes where it goes for anybody else the link was not from.
        Assert.Equal(
            $"/?{OnboardingEndpoints.ConsentParameter}={OnboardingEndpoints.Granted}",
            callback.Headers.Location?.ToString());

        Assert.True(await host.IsTenantConsentGrantedAsync(LinkedTenant, cancellationToken));
        Assert.False(await host.IsTenantConsentGrantedAsync(TodoWerkWebApplicationFactory.TenantId, cancellationToken));
    }

    /// <summary>
    /// And somebody signed in to the very organisation the link is for has a screen of their own to
    /// go back to, which shows the invitation gone.
    /// </summary>
    [Fact]
    public async Task OpenedByASessionInTheLinksOwnTenant_GoesBackToTheTenantOverview()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var host = await SignInTestHost.StartAsync(database.ConnectionString, cancellationToken);

        var session = await host.SignInAsync(cancellationToken);

        using var callback = await host.CompleteTenantConsentResponseAsync(
            session,
            adminConsent: "True",
            tenant: TodoWerkWebApplicationFactory.TenantId,
            cancellationToken,
            approval: host.MintApprovalToken(TodoWerkWebApplicationFactory.TenantId));

        Assert.Equal("/tenant", callback.Headers.Location?.ToString());
        Assert.True(await host.IsTenantConsentGrantedAsync(TodoWerkWebApplicationFactory.TenantId, cancellationToken));
    }

    private static string Unbelievable(SignInTestHost host, string kind) => kind switch
    {
        Expired => host.MintApprovalToken(
            LinkedTenant,
            new FixedClock(DateTimeOffset.UtcNow - ApprovalLink.Lifetime - TimeSpan.FromMinutes(1))),
        Altered => Altering(host.MintApprovalToken(LinkedTenant)),
        Invented => LinkedTenant,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    /// <summary>
    /// One character from the middle, where the ciphertext and its authentication tag are rather
    /// than the key id at the front, swapped for one that is still base64url — so the change is
    /// caught by the cryptography and not by the decoding.
    /// </summary>
    private static string Altering(string token)
    {
        var characters = token.ToCharArray();
        var middle = characters.Length / 2;

        characters[middle] = characters[middle] == 'A' ? 'B' : 'A';

        return new string(characters);
    }

    /// <summary>A clock stopped at one moment, for the link minted a week and a minute ago.</summary>
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
