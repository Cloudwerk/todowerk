using Microsoft.AspNetCore.DataProtection;
using TodoWerk.Infrastructure.Onboarding;
using TodoWerk.UnitTests.Licensing;
using Xunit;

namespace TodoWerk.UnitTests.Onboarding;

/// <summary>
/// What an Approval Link will and will not vouch for. The integration suite follows links through
/// the real endpoints; this pins the edges those cannot reach cheaply — the last second of the
/// week, and a payload of this deployment's that is not a link at all.
/// </summary>
public sealed class ApprovalLinkTests
{
    private const string Tenant = "22222222-2222-2222-2222-222222222222";

    private static readonly Uri Origin = new("https://todowerk.test");

    private static readonly DateTimeOffset Minted = new(2026, 11, 4, 9, 0, 0, TimeSpan.Zero);

    private readonly EphemeralDataProtectionProvider _keys = new();

    [Fact]
    public void ALinkMintedHere_NamesItsTenant_OnTheStartOfTheConsentRoundTrip()
    {
        var link = new ApprovalLink(_keys, new TestClock(Minted)).For(Tenant, Origin);

        Assert.Equal("https", link.Scheme);
        Assert.Equal("todowerk.test", link.Host);
        Assert.Equal(TenantConsentFlow.StartPath, link.AbsolutePath);

        Assert.Equal(Tenant, new ApprovalLink(_keys, new TestClock(Minted)).TenantFrom(TokenOf(link)));
    }

    /// <summary>
    /// The tenant is inside the protected payload and nowhere in the address in the clear. A link
    /// pasted into a chat says where it goes, not which organisation has yet to approve TodoWerk.
    /// </summary>
    [Fact]
    public void TheTenant_IsNotReadableInTheAddress()
    {
        var link = new ApprovalLink(_keys, new TestClock(Minted)).For(Tenant, Origin);

        Assert.DoesNotContain(Tenant, link.AbsoluteUri, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ALink_IsBelievedForAWeek_AndNotASecondLonger()
    {
        var clock = new TestClock(Minted);
        var links = new ApprovalLink(_keys, clock);
        var token = TokenOf(links.For(Tenant, Origin));

        clock.Advance(ApprovalLink.Lifetime);
        Assert.Equal(Tenant, links.TenantFrom(token));

        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Null(links.TenantFrom(token));
    }

    /// <summary>
    /// The consent state cookie is the other payload this deployment protects that holds a tenant
    /// id. Its own purpose is what stops one being presented as the other, and the cookie travels
    /// to the browser, so it is exactly what somebody would try.
    /// </summary>
    [Fact]
    public void AnotherPayloadOfThisDeployments_IsNotALink()
    {
        var stateCookie = _keys
            .CreateProtector("TodoWerk.TenantConsent.v1")
            .Protect($"{Tenant}|{Minted.AddDays(1).ToUnixTimeSeconds()}");

        Assert.Null(new ApprovalLink(_keys, new TestClock(Minted)).TenantFrom(stateCookie));
    }

    /// <summary>A link minted by another deployment, or under a key ring this one no longer holds.</summary>
    [Fact]
    public void ALinkFromAnotherKeyRing_IsNotBelieved()
    {
        var elsewhere = TokenOf(
            new ApprovalLink(new EphemeralDataProtectionProvider(), new TestClock(Minted)).For(Tenant, Origin));

        Assert.Null(new ApprovalLink(_keys, new TestClock(Minted)).TenantFrom(elsewhere));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(Tenant)]
    [InlineData("not-base64url-at-all!")]
    public void NothingThatWasNotMintedHere_IsBelieved(string? token)
    {
        Assert.Null(new ApprovalLink(_keys, new TestClock(Minted)).TenantFrom(token));
    }

    private static string TokenOf(Uri link) =>
        System.Web.HttpUtility.ParseQueryString(link.Query)[ApprovalLink.Parameter] ?? string.Empty;
}
