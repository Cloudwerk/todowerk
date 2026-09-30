using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TodoWerk.Infrastructure.Licensing;
using Xunit;
using static TodoWerk.UnitTests.Licensing.LicensingTestHost;

namespace TodoWerk.UnitTests.Licensing;

/// <summary>
/// Seat metering: a figure and a reconnect signal, never a gate. What matters here is what does
/// <em>not</em> go out — the object id and any licence key — and that a second sign-in on the same
/// day is not a second report.
/// </summary>
public sealed class PortalSeatUsageReporterTests
{
    private const string ValidLicence = """
        {
          "valid": true,
          "status": "active",
          "recheckSeconds": 300,
          "kind": "tenant",
          "licenseId": "3b6f2c9e-1d4a-4e8b-9f0c-7a5d2e1b8c34",
          "usageSecret": "us_secret_value"
        }
        """;

    /// <summary>
    /// The token is <c>hex(HMAC-SHA256(key = utf8(usageSecret), message = utf8(objectId)))</c>,
    /// computed here and nowhere else. Pinned against an independent computation rather than
    /// against a literal, because a literal would only pin what this code did the day it was
    /// written — the point is that it matches the formula ManagementPortal will verify against.
    /// </summary>
    [Fact]
    public void DerivesTheUsageTokenFromThePerLicenceSecretAndTheObjectId()
    {
        var expected = Convert.ToHexStringLower(HMACSHA256.HashData(
            Encoding.UTF8.GetBytes("us_secret_value"),
            Encoding.UTF8.GetBytes(ObjectId)));

        var token = PortalSeatUsageReporter.UsageToken("us_secret_value", ObjectId);

        Assert.Equal(expected, token);
        Assert.Equal(64, token.Length);
        Assert.Matches("^[0-9a-f]{64}$", token);
    }

    /// <summary>
    /// Keying on the per-Licence secret is the load-bearing part: it is what stops one person
    /// being correlated across two Licences, so the same person under two secrets must not produce
    /// the same token.
    /// </summary>
    [Fact]
    public void GivesOnePersonADifferentTokenUnderEachLicence()
    {
        Assert.NotEqual(
            PortalSeatUsageReporter.UsageToken("us_one", ObjectId),
            PortalSeatUsageReporter.UsageToken("us_two", ObjectId));
    }

    [Fact]
    public async Task ReportsTheSeatUnderTheLicenceIdWithoutSendingTheObjectId()
    {
        using var host = Answering(
            PortalReply.Ok(ValidLicence),
            PortalReply.Ok("""{"recorded": true, "seatStatus": "unlimited"}"""));

        var reporter = Reporter(host);

        await reporter.ReportNowAsync(User, TestContext.Current.CancellationToken);

        var usage = Assert.Single(host.Calls, call => call.Path == FirstPartyPortalClient.UsagePath);

        Assert.Contains("\"licenseId\":\"3b6f2c9e-1d4a-4e8b-9f0c-7a5d2e1b8c34\"", usage.Body, StringComparison.Ordinal);
        Assert.Contains(
            PortalSeatUsageReporter.UsageToken("us_secret_value", ObjectId),
            usage.Body,
            StringComparison.Ordinal);

        // The two things that must never be in an outbound body: the person's raw identifier, and
        // the secret the token was derived from.
        Assert.DoesNotContain(ObjectId, usage.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("us_secret_value", usage.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportsAtMostOncePerPersonPerDay()
    {
        using var host = Answering(PortalReply.Ok(ValidLicence));
        var reporter = Reporter(host);

        Assert.True(reporter.ClaimToday(User));
        Assert.False(reporter.ClaimToday(User));
        Assert.False(reporter.ClaimToday(User));

        // A colleague signing in the same day is a person of their own, and their seat is their own.
        Assert.True(reporter.ClaimToday(Colleague));

        host.Clock.Advance(TimeSpan.FromDays(1));

        Assert.True(reporter.ClaimToday(User));
        Assert.False(reporter.ClaimToday(User));
    }

    /// <summary>
    /// The claim is taken before the work starts rather than after it succeeds, so a portal having
    /// a bad minute costs one uncounted seat rather than a retry loop against a figure that gates
    /// nothing.
    /// </summary>
    [Fact]
    public async Task DoesNotRetryAFailedReportUntilTheNextDay()
    {
        using var host = Answering(
            PortalReply.Ok(ValidLicence),
            PortalReply.Status(HttpStatusCode.InternalServerError));

        var reporter = Reporter(host);

        await reporter.ReportSignInAsync(User, TestContext.Current.CancellationToken);

        Assert.False(reporter.ClaimToday(User));
    }

    /// <summary>
    /// A denied person has no seat to report. Nothing goes out, and nothing about the denial is
    /// changed by the attempt.
    /// </summary>
    [Fact]
    public async Task ReportsNothingForSomebodyWhoIsNotLicensed()
    {
        using var host = Answering(PortalReply.Ok("""
            {"valid": false, "status": "expired", "recheckSeconds": 300, "kind": "trial"}
            """));

        var reporter = Reporter(host);

        await reporter.ReportNowAsync(User, TestContext.Current.CancellationToken);

        Assert.DoesNotContain(host.Calls, call => call.Path == FirstPartyPortalClient.UsagePath);
    }

    /// <summary>
    /// A failed report is a log line. Nothing throws, because the caller is a sign-in that has
    /// already succeeded and refusing somebody the product over a figure would be the wrong trade.
    /// </summary>
    [Fact]
    public async Task SurvivesAPortalThatRefusesTheReport()
    {
        using var host = Answering(
            PortalReply.Ok(ValidLicence),
            PortalReply.Status(HttpStatusCode.InternalServerError));

        var logs = new RecordingLogger<PortalSeatUsageReporter>();

        await Reporter(host, logs).ReportNowAsync(User, TestContext.Current.CancellationToken);

        // A portal that never decided is still a seat nobody counted, and the line says so with
        // no status to name.
        Assert.Contains("no answer", Assert.Single(logs.Complaints), StringComparison.Ordinal);
    }

    /// <summary>
    /// The portal declines over HTTP 200, with <c>recorded: false</c> in the body. <c>invalid</c>
    /// is the licence itself — out of term by the moment the report lands, suspended, not this
    /// tenant's, not this solution's, or one the portal has never heard of — and reading the status
    /// code alone would make every one of them a seat that landed.
    /// </summary>
    [Fact]
    public async Task WarnsAboutASeatThePortalDeclinedToStoreOverHttp200()
    {
        using var host = Answering(
            PortalReply.Ok(ValidLicence),
            PortalReply.Ok("""
                {"recorded": false, "seatStatus": "invalid", "seatsUsed": 0, "seatLimit": null}
                """));

        var logs = new RecordingLogger<PortalSeatUsageReporter>();

        await Reporter(host, logs).ReportNowAsync(User, TestContext.Current.CancellationToken);

        var complaint = Assert.Single(logs.Complaints);

        // The tenant, so the line points at somebody, and the portal's own word for why, so it
        // says which of the six reasons this was.
        Assert.Contains(TenantId, complaint, StringComparison.Ordinal);
        Assert.Contains("invalid", complaint, StringComparison.Ordinal);
    }

    /// <summary>
    /// The seat-token ceiling, the other way a report is declined over HTTP 200. The portal
    /// refuses over the ceiling with <c>recorded: false</c> and a seat status of <c>within</c> or
    /// <c>over</c> — not <c>invalid</c>, which is reserved for the licence itself — so what is
    /// complained about is every report that did not land, not one status word.
    /// </summary>
    [Fact]
    public async Task WarnsAboutASeatRefusedOverThePerLicenceCeiling()
    {
        using var host = Answering(
            PortalReply.Ok(ValidLicence),
            PortalReply.Ok("""
                {"recorded": false, "seatStatus": "over", "seatsUsed": 20000, "seatLimit": 500}
                """));

        var logs = new RecordingLogger<PortalSeatUsageReporter>();

        await Reporter(host, logs).ReportNowAsync(User, TestContext.Current.CancellationToken);

        Assert.Contains("over", Assert.Single(logs.Complaints), StringComparison.Ordinal);
    }

    /// <summary>
    /// The soft one. A seat above the licence's limit <em>is</em> stored — <c>recorded: true</c>
    /// with <c>over</c> — and the contract asks callers to warn and never to block, so this line
    /// says the seat was counted and the licence is carrying more people than it was sold. Both
    /// figures, because "how far over" is the whole question a person reading it has.
    /// </summary>
    [Fact]
    public async Task WarnsAboutASeatCountedAboveTheLicencesSeatLimit()
    {
        using var host = Answering(
            PortalReply.Ok(ValidLicence),
            PortalReply.Ok("""
                {"recorded": true, "seatStatus": "over", "seatsUsed": 205, "seatLimit": 200}
                """));

        var logs = new RecordingLogger<PortalSeatUsageReporter>();

        await Reporter(host, logs).ReportNowAsync(User, TestContext.Current.CancellationToken);

        var complaint = Assert.Single(logs.Complaints);

        Assert.Contains(TenantId, complaint, StringComparison.Ordinal);
        Assert.Contains("205", complaint, StringComparison.Ordinal);
        Assert.Contains("200", complaint, StringComparison.Ordinal);

        // The seat was counted. A line that reads like the one about a seat nobody counted would
        // send whoever finds it looking for a report that never went missing.
        Assert.DoesNotContain("did not record", complaint, StringComparison.Ordinal);
    }

    /// <summary>
    /// A solution metered as Unlimited never meters, so it answers <c>recorded: false</c> with a
    /// seat status of <c>unlimited</c> and stores nothing — by design rather than by refusal. It
    /// is the normal answer for such a solution, on every sign-in of every person, and warning
    /// about it would bury the refusals that mean something.
    /// </summary>
    [Fact]
    public async Task SaysNothingAboutASolutionMeteredAsUnlimited()
    {
        using var host = Answering(
            PortalReply.Ok(ValidLicence),
            PortalReply.Ok("""
                {"recorded": false, "seatStatus": "unlimited", "seatsUsed": 0, "seatLimit": null}
                """));

        var logs = new RecordingLogger<PortalSeatUsageReporter>();

        await Reporter(host, logs).ReportNowAsync(User, TestContext.Current.CancellationToken);

        Assert.Empty(logs.Complaints);
    }

    [Fact]
    public async Task SaysNothingAboutASeatThePortalStored()
    {
        using var host = Answering(
            PortalReply.Ok(ValidLicence),
            PortalReply.Ok("""
                {"recorded": true, "seatStatus": "within", "seatsUsed": 1, "seatLimit": 500}
                """));

        var logs = new RecordingLogger<PortalSeatUsageReporter>();

        await Reporter(host, logs).ReportNowAsync(User, TestContext.Current.CancellationToken);

        Assert.Empty(logs.Complaints);
    }

    /// <summary>
    /// An answer this build cannot read is not a seat that landed either. A proxy or a maintenance
    /// page answering HTTP 200 with something that is not the contract is exactly what reading the
    /// status code alone would call a success.
    /// </summary>
    [Fact]
    public async Task ReadsAnUnreadableAnswerAsASeatThatDidNotLand()
    {
        using var host = Answering(
            PortalReply.Ok(ValidLicence),
            PortalReply.Ok("<html>Down for maintenance</html>"));

        var logs = new RecordingLogger<PortalSeatUsageReporter>();

        await Reporter(host, logs).ReportNowAsync(User, TestContext.Current.CancellationToken);

        Assert.Contains("no answer", Assert.Single(logs.Complaints), StringComparison.Ordinal);
    }

    private static PortalSeatUsageReporter Reporter(
        LicensingTestHost host,
        ILogger<PortalSeatUsageReporter>? logger = null) =>
        new(host.Resolver, host.Portal, host.Clock, logger ?? NullLogger<PortalSeatUsageReporter>.Instance);
}
