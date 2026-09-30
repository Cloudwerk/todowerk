using System.Net;
using TodoWerk.Application.Licensing;
using TodoWerk.Domain.Licensing;
using TodoWerk.Infrastructure.Licensing;
using Xunit;
using static TodoWerk.UnitTests.Licensing.LicensingTestHost;

namespace TodoWerk.UnitTests.Licensing;

/// <summary>
/// Resolution, caching, fail-open and deny — the four rules licensing hangs off, driven against a
/// scripted portal and a clock the test moves.
/// <para>
/// Bodies are written out literally so a contract change shows up in the diff. A helper would
/// hide it: this suite is the only place in the repository that says what the portal sends.
/// </para>
/// </summary>
public sealed class PortalLicenceResolverTests
{
    private const string TenantLicence = """
        {
          "valid": true,
          "status": "active",
          "expiresUtc": "2027-03-14T00:00:00Z",
          "message": "License is valid.",
          "recheckSeconds": 300,
          "failOpenSeconds": 14400,
          "kind": "tenant",
          "licenseType": "paid",
          "channel": "portal",
          "licenseId": "3b6f2c9e-1d4a-4e8b-9f0c-7a5d2e1b8c34",
          "usageSecret": "us_secret_value"
        }
        """;

    private const string PersonalLicence = """
        {
          "valid": true,
          "status": "active",
          "expiresUtc": "2027-01-31T00:00:00Z",
          "recheckSeconds": 300,
          "kind": "personal",
          "licenseId": "personal-licence",
          "usageSecret": "us_personal"
        }
        """;

    private const string TrialLicence = """
        {
          "valid": true,
          "status": "active",
          "expiresUtc": "2026-10-02T09:14:07Z",
          "recheckSeconds": 300,
          "kind": "trial",
          "licenseId": "trial-licence",
          "usageSecret": "us_trial",
          "purchaseUrl": "https://portal.test/buy/todowerk"
        }
        """;

    private const string ExpiredTrial = """
        {
          "valid": false,
          "status": "expired",
          "message": "Your trial of TodoWerk ended on 2 October 2026.",
          "recheckSeconds": 300,
          "kind": "trial"
        }
        """;

    [Theory]
    [InlineData(TenantLicence, LicenceKind.Tenant, true)]
    [InlineData(PersonalLicence, LicenceKind.Personal, false)]
    [InlineData(TrialLicence, LicenceKind.Trial, true)]
    public async Task ReadsTheKindAndWhetherTenantConsentMayBeOffered(
        string body,
        LicenceKind kind,
        bool mayOfferConsent)
    {
        using var host = Answering(PortalReply.Ok(body));

        var resolution = await host.Resolver.ResolveAsync(User, TestContext.Current.CancellationToken);

        Assert.Equal(LicenceOutcome.Licensed, resolution.Outcome);
        Assert.True(resolution.IsLicensed);
        Assert.Equal(kind, resolution.Licence!.Kind);

        // The one rule ADR-0012 is most insistent about: paying for one seat is not standing to
        // approve TodoWerk for an organisation.
        Assert.Equal(mayOfferConsent, resolution.Licence.MayOfferTenantConsent);
    }

    /// <summary>
    /// A Tenant Licence covers everybody, and it is resolved per person all the same — so a
    /// colleague is a second call with a second answer, not an inherited one.
    /// </summary>
    [Fact]
    public async Task ResolvesEachPersonSeparatelyEvenUnderATenantLicence()
    {
        using var host = Answering(PortalReply.Ok(TenantLicence));

        var mine = await host.Resolver.ResolveAsync(User, TestContext.Current.CancellationToken);
        var theirs = await host.Resolver.ResolveAsync(Colleague, TestContext.Current.CancellationToken);

        Assert.True(mine.IsLicensed);
        Assert.True(theirs.IsLicensed);
        Assert.Equal(2, host.Calls.Count);
        Assert.Contains(ObjectId, host.Calls[0].Body, StringComparison.Ordinal);
        Assert.Contains(ColleagueObjectId, host.Calls[1].Body, StringComparison.Ordinal);
    }

    /// <summary>
    /// A confirmed negative — <c>valid: false</c> over HTTP 200 — is the only thing that ends
    /// anybody's access, and it ends one person's. The portal's own sentence travels with it,
    /// because an expired Trial and an expired subscription are different news.
    /// </summary>
    [Fact]
    public async Task AConfirmedNegativeDeniesThatPersonAndCarriesThePortalsWords()
    {
        using var host = Answering(PortalReply.Ok(ExpiredTrial));

        var resolution = await host.Resolver.ResolveAsync(User, TestContext.Current.CancellationToken);

        Assert.Equal(LicenceOutcome.Ended, resolution.Outcome);
        Assert.False(resolution.IsLicensed);

        var error = LicensingErrors.ForDeniedResolution(wasConfirmedNegative: true, resolution.Message);

        Assert.Equal(LicensingErrors.Ended.Code, error.Code);
        Assert.Equal("Your trial of TodoWerk ended on 2 October 2026.", error.Description);
    }

    /// <summary>
    /// Denying a colleague is the mistake the per-person model exists to avoid, so it gets a test
    /// of its own rather than being inferred from the two above.
    /// </summary>
    [Fact]
    public async Task ADeniedPersonLeavesTheirColleagueUntouched()
    {
        using var host = Answering(PortalReply.Ok(ExpiredTrial), PortalReply.Ok(TenantLicence));

        var denied = await host.Resolver.ResolveAsync(User, TestContext.Current.CancellationToken);
        var colleague = await host.Resolver.ResolveAsync(Colleague, TestContext.Current.CancellationToken);

        Assert.False(denied.IsLicensed);
        Assert.True(colleague.IsLicensed);
    }

    /// <summary>
    /// Never resolve per request. What bounds it is the portal's own <c>recheckSeconds</c>, and
    /// what bounds that is this deployment's ceiling — an operator cutting an organisation off has
    /// to land in minutes either way.
    /// </summary>
    [Fact]
    public async Task AnswersFromCacheUntilTheRecheckIntervalRunsOut()
    {
        using var host = Answering(PortalReply.Ok(TenantLicence));

        await host.Resolver.ResolveAsync(User, TestContext.Current.CancellationToken);
        host.Clock.Advance(TimeSpan.FromMinutes(4));
        await host.Resolver.ResolveAsync(User, TestContext.Current.CancellationToken);

        Assert.Single(host.Calls);

        host.Clock.Advance(TimeSpan.FromMinutes(2));
        await host.Resolver.ResolveAsync(User, TestContext.Current.CancellationToken);

        Assert.Equal(2, host.Calls.Count);
    }

    /// <summary>
    /// The portal records which resolution was a person's first in this process. It changes no
    /// answer and is worth one assertion so that a refactor cannot silently start reporting every
    /// request as an install.
    /// </summary>
    [Fact]
    public async Task SaysInstallOnceAndPeriodicAfterwards()
    {
        using var host = Answering(PortalReply.Ok(TenantLicence));

        await host.Resolver.ResolveAsync(User, TestContext.Current.CancellationToken);
        host.Clock.Advance(TimeSpan.FromMinutes(6));
        await host.Resolver.ResolveAsync(User, TestContext.Current.CancellationToken);

        Assert.Contains("\"checkKind\":\"install\"", host.Calls[0].Body, StringComparison.Ordinal);
        Assert.Contains("\"checkKind\":\"periodic\"", host.Calls[1].Body, StringComparison.Ordinal);
    }

    /// <summary>
    /// "Never resolve per request" has to hold at the moment the recheck interval runs out, too.
    /// Opening the Workbench sends half a dozen requests within the same few milliseconds, every
    /// one of them behind the Licence gate; when the cached answer has just aged out they all miss
    /// it together, and without something to join them each would ask the portal on its own.
    /// </summary>
    [Fact]
    public async Task RequestsThatMissTheCacheTogetherShareOneResolution()
    {
        var held = new HeldReply();
        using var host = Answering(PortalReply.Ok(TenantLicence), PortalReply.Ok(TenantLicence, holds: held));

        await host.Resolver.ResolveAsync(User, TestContext.Current.CancellationToken);
        host.Clock.Advance(TimeSpan.FromMinutes(6));

        var burst = Enumerable.Range(0, 5)
            .Select(_ => host.Resolver.ResolveAsync(User, TestContext.Current.CancellationToken))
            .ToArray();

        Assert.Equal(2, host.Calls.Count);
        Assert.All(burst, request => Assert.False(request.IsCompleted));

        held.Release();
        var resolutions = await Task.WhenAll(burst);

        Assert.All(resolutions, resolution => Assert.True(resolution.IsLicensed));
        Assert.Equal(2, host.Calls.Count);
        Assert.Contains("\"checkKind\":\"periodic\"", host.Calls[1].Body, StringComparison.Ordinal);
    }

    /// <summary>
    /// The shared call belongs to nobody in particular. The first request in a burst is the one
    /// that sent it, and if that browser tab navigates away the four behind it still need an
    /// answer — and the cache still needs the answer written, or the next burst starts over.
    /// </summary>
    [Fact]
    public async Task ACallerGivingUpDoesNotCancelTheResolutionOthersAreWaitingOn()
    {
        var held = new HeldReply();
        using var host = Answering(PortalReply.Ok(TenantLicence, holds: held));
        using var leaver = new CancellationTokenSource();

        var leaving = host.Resolver.ResolveAsync(User, leaver.Token);
        var staying = host.Resolver.ResolveAsync(User, TestContext.Current.CancellationToken);

        await leaver.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => leaving);
        Assert.False(staying.IsCompleted);

        held.Release();
        var resolution = await staying;

        Assert.True(resolution.IsLicensed);
        Assert.Single(host.Calls);
        Assert.NotNull(host.Cache.Read(User)?.Answer);
    }

    /// <summary>
    /// Joined per person, not per process: two people arriving in the same instant are two
    /// questions with two answers, exactly as they are when they arrive a minute apart.
    /// </summary>
    [Fact]
    public async Task TwoPeopleArrivingTogetherAreStillTwoResolutions()
    {
        var held = new HeldReply();
        using var host = Answering(PortalReply.Ok(TenantLicence, holds: held));

        var mine = host.Resolver.ResolveAsync(User, TestContext.Current.CancellationToken);
        var theirs = host.Resolver.ResolveAsync(Colleague, TestContext.Current.CancellationToken);

        Assert.Equal(2, host.Calls.Count);

        held.Release();
        var resolutions = await Task.WhenAll(mine, theirs);

        Assert.All(resolutions, resolution => Assert.True(resolution.IsLicensed));
        Assert.Equal(2, host.Calls.Count);
    }

    /// <summary>Every call carries the one credential, and it is a header rather than a body field.</summary>
    [Fact]
    public async Task SendsTheApplicationKeyAsAHeader()
    {
        using var host = Answering(PortalReply.Ok(TenantLicence));

        await host.Resolver.ResolveAsync(User, TestContext.Current.CancellationToken);

        Assert.Equal("test-key", host.Calls[0].ApplicationKey);
        Assert.DoesNotContain("test-key", host.Calls[0].Body, StringComparison.Ordinal);
        Assert.Equal(FirstPartyPortalClient.ResolvePath, host.Calls[0].Path);
    }

    /// <summary>
    /// Unreachable is not a licence failure. Nothing a user can see changes inside the window,
    /// whatever shape the unreachability took.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task KeepsServingOnTheLastAnswerInsideTheFailOpenWindow(bool transportFailure)
    {
        using var host = Answering(
            PortalReply.Ok(TenantLicence),
            transportFailure ? PortalReply.Unreachable() : PortalReply.Status(HttpStatusCode.InternalServerError));

        await host.Resolver.ResolveAsync(User, TestContext.Current.CancellationToken);

        host.Clock.Advance(TimeSpan.FromHours(20));
        var resolution = await host.Resolver.ResolveAsync(User, TestContext.Current.CancellationToken);

        Assert.Equal(LicenceOutcome.Licensed, resolution.Outcome);
        Assert.Equal(LicenceKind.Tenant, resolution.Licence!.Kind);
    }

    /// <summary>
    /// Past the window, the second problem code — never the first. A customer who has paid is
    /// exactly who meets this one, and telling them their access had ended would be a lie they
    /// would reasonably act on.
    /// </summary>
    [Fact]
    public async Task DeniesWithCouldNotBeVerifiedOnceTheWindowHasRunOut()
    {
        using var host = Answering(PortalReply.Ok(TenantLicence), PortalReply.Unreachable());

        await host.Resolver.ResolveAsync(User, TestContext.Current.CancellationToken);

        host.Clock.Advance(TimeSpan.FromHours(25));
        var resolution = await host.Resolver.ResolveAsync(User, TestContext.Current.CancellationToken);

        Assert.Equal(LicenceOutcome.Unverified, resolution.Outcome);
        Assert.False(resolution.IsLicensed);
        Assert.Equal(
            LicensingErrors.CouldNotBeVerified.Code,
            LicensingErrors.ForDeniedResolution(wasConfirmedNegative: false, resolution.Message).Code);
    }

    /// <summary>
    /// Somebody arriving for the first time during an outage has no positive answer to fall back
    /// on, so they are denied rather than served. It is the one place the First-Party Path is
    /// stricter than a package contract: TodoWerk denies a first-time arrival during an outage
    /// rather than serve unverified.
    /// </summary>
    [Fact]
    public async Task DeniesSomebodyNeverResolvedWhileThePortalIsDown()
    {
        using var host = Answering(PortalReply.Unreachable());

        var resolution = await host.Resolver.ResolveAsync(User, TestContext.Current.CancellationToken);

        Assert.Equal(LicenceOutcome.Unverified, resolution.Outcome);
    }

    /// <summary>
    /// And it clears itself, with nobody pressing anything: the next request after the retry floor
    /// resolves, and the person is served.
    /// </summary>
    [Fact]
    public async Task ClearsItselfWhenThePortalComesBack()
    {
        using var host = Answering(PortalReply.Unreachable(), PortalReply.Ok(TenantLicence));

        Assert.Equal(
            LicenceOutcome.Unverified,
            (await host.Resolver.ResolveAsync(User, TestContext.Current.CancellationToken)).Outcome);

        host.Clock.Advance(TimeSpan.FromMinutes(1));

        Assert.Equal(
            LicenceOutcome.Licensed,
            (await host.Resolver.ResolveAsync(User, TestContext.Current.CancellationToken)).Outcome);
    }

    /// <summary>
    /// A portal that has stopped answering must not be called once per request: each call costs
    /// the caller a timeout, and an outage would take the Hosted Service down with it.
    /// </summary>
    [Fact]
    public async Task DoesNotCallAnUnreachablePortalOnEveryRequest()
    {
        using var host = Answering(PortalReply.Unreachable());

        await host.Resolver.ResolveAsync(User, TestContext.Current.CancellationToken);
        await host.Resolver.ResolveAsync(User, TestContext.Current.CancellationToken);
        await host.Resolver.ResolveAsync(User, TestContext.Current.CancellationToken);

        Assert.Single(host.Calls);
    }

    /// <summary>
    /// The floor is counted from when the failed attempt ended, not from when it began.
    /// <para>
    /// A portal that has stopped answering is exactly the one whose round trip takes the whole of
    /// <c>RequestTimeout</c>, so a floor stamped from before the call would be thinnest precisely
    /// when it is needed.
    /// </para>
    /// </summary>
    [Fact]
    public async Task CountsTheRetryFloorFromWhenTheFailedAttemptEndedRatherThanFromWhenItBegan()
    {
        using var host = Answering(PortalReply.Unreachable(takes: TimeSpan.FromSeconds(30)));

        await host.Resolver.ResolveAsync(User, TestContext.Current.CancellationToken);

        // Twenty seconds after the attempt ended: inside a floor counted from its end, and ten
        // seconds past one counted from its start.
        host.Clock.Advance(TimeSpan.FromSeconds(20));
        await host.Resolver.ResolveAsync(User, TestContext.Current.CancellationToken);

        Assert.Single(host.Calls);

        // And the floor is thirty seconds long rather than longer: on the stroke, it asks again.
        host.Clock.Advance(TimeSpan.FromSeconds(10));
        await host.Resolver.ResolveAsync(User, TestContext.Current.CancellationToken);

        Assert.Equal(2, host.Calls.Count);
    }

    /// <summary>
    /// The same instant on the other path: an answer is trusted for the recheck interval counted
    /// from when it arrived, and the fail-open window counts from there too. One reading of the
    /// clock for both stamps on purpose — two instants for one answer is how the floor above would
    /// go wrong.
    /// </summary>
    [Fact]
    public async Task TrustsAnAnswerForTheRecheckIntervalCountedFromWhenItArrived()
    {
        using var host = Answering(PortalReply.Ok(TenantLicence, takes: TimeSpan.FromSeconds(30)));

        await host.Resolver.ResolveAsync(User, TestContext.Current.CancellationToken);

        // The fixture's recheck is five minutes and the call took thirty seconds, so this lands
        // four minutes forty past the answer: inside five minutes counted from its arrival, and
        // ten seconds past five minutes counted from the asking.
        host.Clock.Advance(TimeSpan.FromSeconds(280));
        await host.Resolver.ResolveAsync(User, TestContext.Current.CancellationToken);

        Assert.Single(host.Calls);
    }

    /// <summary>
    /// The purchase URL is read off the answer's own <c>purchaseUrl</c> field, and only when it is
    /// a usable one. The portal validates it too; this second check exists because a bad value
    /// becomes a link nobody chose.
    /// <para>
    /// <c>http</c> passes: the portal answers a development host with one, and a local instance is
    /// a legitimate thing to point TodoWerk at.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("\"https://portal.test/buy\"", "https://portal.test/buy")]
    [InlineData("\"http://localhost:5000/Solutions\"", "http://localhost:5000/Solutions")]
    [InlineData("\"/buy\"", null)]
    [InlineData("\"javascript:alert(1)\"", null)]
    [InlineData("\"\"", null)]
    [InlineData("null", null)]
    public async Task ReadsAPurchaseUrlOnlyWhenTheAnswerCarriesAUsableOne(
        string delivered,
        string? expected)
    {
        using var host = Answering(PortalReply.Ok($$"""
            {
              "valid": true,
              "status": "active",
              "recheckSeconds": 300,
              "kind": "trial",
              "licenseId": "l",
              "usageSecret": "s",
              "purchaseUrl": {{delivered}}
            }
            """));

        var resolution = await host.Resolver.ResolveAsync(User, TestContext.Current.CancellationToken);

        Assert.Equal(expected, resolution.PurchaseUrl?.ToString());
    }

    /// <summary>
    /// An answer with no <c>purchaseUrl</c> at all is the ordinary case, not a fault: the portal
    /// sends none where it sells nothing for TodoWerk or where CloudWerk has deactivated the
    /// organisation. Nothing is denied and no link is shown.
    /// </summary>
    [Fact]
    public async Task TreatsAnAnswerWithNoPurchaseUrlAsAPersonWithNowhereToBuy()
    {
        using var host = Answering(PortalReply.Ok(TenantLicence));

        var resolution = await host.Resolver.ResolveAsync(User, TestContext.Current.CancellationToken);

        Assert.Equal(LicenceOutcome.Licensed, resolution.Outcome);
        Assert.Null(resolution.PurchaseUrl);
    }

    /// <summary>
    /// The point of the field living on the answer rather than in the client configuration
    /// document: a refusal carries it too. That document is withheld on every non-valid answer, so
    /// a link inside it could never reach the ended card — which is the one screen whose reader is
    /// being told to buy something.
    /// </summary>
    [Fact]
    public async Task CarriesThePurchaseUrlOnAConfirmedNegativeToo()
    {
        using var host = Answering(PortalReply.Ok("""
            {
              "valid": false,
              "status": "expired",
              "message": "Your trial of TodoWerk ended on 2 October 2026.",
              "recheckSeconds": 300,
              "kind": "trial",
              "purchaseUrl": "https://portal.test/Solutions"
            }
            """));

        var resolution = await host.Resolver.ResolveAsync(User, TestContext.Current.CancellationToken);

        Assert.Equal(LicenceOutcome.Ended, resolution.Outcome);
        Assert.Null(resolution.Licence);
        Assert.Equal("https://portal.test/Solutions", resolution.PurchaseUrl?.ToString());
    }

    /// <summary>
    /// A refusal replayed out of the cache carries the link it arrived with. The whole
    /// <see cref="LicenceResolution"/> is what the cache holds and what the fail-open window
    /// replays, which is the reason the URL lives on the answer rather than on the Licence: a
    /// field held anywhere else would vanish the moment an answer was served from memory.
    /// </summary>
    [Fact]
    public async Task ReplaysThePurchaseUrlOnACachedRefusal()
    {
        using var host = Answering(PortalReply.Ok("""
            {
              "valid": false,
              "status": "expired",
              "message": "It ended.",
              "recheckSeconds": 300,
              "purchaseUrl": "https://portal.test/Solutions"
            }
            """));

        await host.Resolver.ResolveAsync(User, TestContext.Current.CancellationToken);

        var cached = await host.Resolver.ResolveAsync(User, TestContext.Current.CancellationToken);

        Assert.Single(host.Calls);
        Assert.Equal("https://portal.test/Solutions", cached.PurchaseUrl?.ToString());
    }

    /// <summary>
    /// An unreachable portal answered nothing, so there is no address — and TodoWerk composes none
    /// of its own. The card a person meets here is the one for people who have paid, which must
    /// never offer them a way to buy what they already own.
    /// </summary>
    [Fact]
    public async Task OffersNowhereToBuyWhenThePortalNeverAnswered()
    {
        using var host = Answering(PortalReply.Unreachable());

        var resolution = await host.Resolver.ResolveAsync(User, TestContext.Current.CancellationToken);

        Assert.Equal(LicenceOutcome.Unverified, resolution.Outcome);
        Assert.Null(resolution.PurchaseUrl);
    }

    /// <summary>
    /// A <c>purchaseUrl</c> that is not a string costs the link and nothing else. The person stays
    /// licensed.
    /// <para>
    /// This is the whole reason the field is read as a <c>JsonElement</c> rather than as the
    /// <c>string?</c> the contract promises. Typed as a string it would fail the answer to parse,
    /// which this client treats as unreachability — so one cosmetic field, malformed, would deny
    /// every person until somebody deployed. Nothing decides on this value, so it is the one field
    /// on this shape where dropping the value beats dropping the answer.
    /// </para>
    /// </summary>
    [Fact]
    public async Task DropsAPurchaseUrlThatIsNotAStringAndLicensesThePersonAnyway()
    {
        using var host = Answering(PortalReply.Ok("""
            {
              "valid": true,
              "status": "active",
              "recheckSeconds": 300,
              "kind": "trial",
              "licenseId": "l",
              "usageSecret": "s",
              "purchaseUrl": 42
            }
            """));

        var resolution = await host.Resolver.ResolveAsync(User, TestContext.Current.CancellationToken);

        Assert.Equal(LicenceOutcome.Licensed, resolution.Outcome);
        Assert.Null(resolution.PurchaseUrl);
    }

    /// <summary>
    /// A kind this build has never heard of reads as a Personal Licence, which is the conservative
    /// answer and not an arbitrary one: no banner, and no Tenant Consent invitation. The two
    /// mistakes available are not symmetrical.
    /// </summary>
    [Fact]
    public async Task TreatsAnUnknownKindAsAPersonalLicence()
    {
        using var host = Answering(PortalReply.Ok("""
            {"valid": true, "status": "active", "recheckSeconds": 300, "kind": "syndicate"}
            """));

        var resolution = await host.Resolver.ResolveAsync(User, TestContext.Current.CancellationToken);

        Assert.True(resolution.IsLicensed);
        Assert.Equal(LicenceKind.Personal, resolution.Licence!.Kind);
        Assert.False(resolution.Licence.MayOfferTenantConsent);
    }

    /// <summary>
    /// A 401 is a rotated or wrong key. It is a configuration fault that retrying will not fix and
    /// somebody has to be paged — but it is not a licence failure, and denying every customer over
    /// it while the operator sleeps would be the worse of the two outcomes.
    /// </summary>
    [Fact]
    public async Task TreatsAnUnauthorizedKeyAsUnreachableRatherThanADenial()
    {
        using var host = Answering(PortalReply.Ok(TenantLicence), PortalReply.Status(HttpStatusCode.Unauthorized));

        await host.Resolver.ResolveAsync(User, TestContext.Current.CancellationToken);
        host.Clock.Advance(TimeSpan.FromMinutes(6));

        var resolution = await host.Resolver.ResolveAsync(User, TestContext.Current.CancellationToken);

        Assert.Equal(LicenceOutcome.Licensed, resolution.Outcome);
    }

    /// <summary>
    /// TodoWerk's configured window overrides the portal's <c>failOpenSeconds</c>.
    /// </summary>
    [Fact]
    public async Task PrefersTheConfiguredFailOpenWindowOverThePortalsOwn()
    {
        using var host = Answering(PortalReply.Ok(TenantLicence), PortalReply.Unreachable());

        await host.Resolver.ResolveAsync(User, TestContext.Current.CancellationToken);

        // Past the portal's four hours, well inside TodoWerk's twenty-four.
        host.Clock.Advance(TimeSpan.FromHours(6));

        Assert.True((await host.Resolver.ResolveAsync(User, TestContext.Current.CancellationToken)).IsLicensed);
    }

    /// <summary>
    /// The gate the background claims ask through and the resolver the request path asks are one
    /// object over one cache, so a Workbench that is serving and a scan that is refused cannot
    /// disagree about the same person.
    /// </summary>
    [Fact]
    public async Task TheBackgroundGateAnswersFromTheSameCacheAsTheRequestPath()
    {
        using var host = Answering(PortalReply.Ok(ExpiredTrial));

        await host.Resolver.ResolveAsync(User, TestContext.Current.CancellationToken);

        Assert.False(await host.Resolver.IsLicensedAsync(User, TestContext.Current.CancellationToken));
        Assert.Single(host.Calls);
    }

    /// <summary>
    /// Erasure reaches this module too. The cache holds an object id with an answer beside it, and
    /// somebody who has asked to be forgotten should not still be a key in a dictionary.
    /// </summary>
    [Fact]
    public async Task ErasureForgetsTheCachedAnswer()
    {
        using var host = Answering(PortalReply.Ok(TenantLicence));

        await host.Resolver.ResolveAsync(User, TestContext.Current.CancellationToken);

        var seats = new PortalSeatUsageReporter(
            host.Resolver,
            host.Portal,
            host.Clock,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<PortalSeatUsageReporter>.Instance);

        seats.ClaimToday(User);

        var purge = new LicencePersonalDataPurge(host.Cache, seats);
        var outcome = await purge.PurgeAsync(User, TestContext.Current.CancellationToken);

        // Both dictionaries, not one. Each held an Entra object id, and forgetting one without the
        // other would make this purge's promise half true.
        Assert.Equal(2, outcome.Removed);
        Assert.True(outcome.NothingLeft);
        Assert.Null(host.Cache.Read(User));
        Assert.True(seats.ClaimToday(User));

        // And the next request resolves from scratch rather than serving an answer about somebody
        // the database no longer holds.
        await host.Resolver.ResolveAsync(User, TestContext.Current.CancellationToken);

        Assert.Equal(2, host.Calls.Count);
    }
}
