using TodoWerk.Domain.Licensing;
using Xunit;

namespace TodoWerk.UnitTests.Licensing;

/// <summary>
/// Which of the three things the Workbench says, and the silence that is the common case. The
/// banner talks to one person about their own Trial and never about anything else — a Tenant
/// Licence renewing next month is not the reader's business, and a Personal Licence in term is not
/// news.
/// </summary>
public sealed class LicenceBannerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 3, 8, 0, 0, TimeSpan.Zero);

    private static readonly TimeSpan EndingSoon = TimeSpan.FromDays(7);

    [Theory]
    [InlineData(LicenceKind.Tenant)]
    [InlineData(LicenceKind.Personal)]
    public void SaysNothingUnderAPaidLicenceHoweverCloseItsEndIs(LicenceKind kind)
    {
        var licence = LicenceOf(kind, Now.AddHours(1));

        Assert.Equal(LicenceBannerState.None, LicenceBanner.For(licence, Now, EndingSoon));
    }

    [Fact]
    public void SaysNothingWhenThereIsNoLicenceAtAll()
    {
        // A Self-Host. There is no Licence to talk about and never a banner.
        Assert.Equal(LicenceBannerState.None, LicenceBanner.For(null, Now, EndingSoon));
    }

    [Fact]
    public void SaysATrialIsRunningWhileItsEndIsFurtherOffThanTheThreshold()
    {
        var licence = LicenceOf(LicenceKind.Trial, Now.AddDays(30));

        Assert.Equal(LicenceBannerState.TrialRunning, LicenceBanner.For(licence, Now, EndingSoon));
    }

    [Theory]
    [InlineData(6)]
    [InlineData(7)]
    public void WarnsOnceTheEndIsInsideTheThreshold(int days)
    {
        // Inclusive at the boundary: a Trial ending in exactly a week is a Trial ending within a
        // week, and the alternative reading loses the warning for a whole day.
        var licence = LicenceOf(LicenceKind.Trial, Now.AddDays(days));

        Assert.Equal(LicenceBannerState.TrialEndingSoon, LicenceBanner.For(licence, Now, EndingSoon));
    }

    [Fact]
    public void WarnsAboutATrialWhoseEndHasAlreadyPassed()
    {
        // The resolver would normally have turned this into a denial before anything asked. Should
        // a stale cached answer reach here, the warning is the honest half-truth and "running" is
        // not.
        var licence = LicenceOf(LicenceKind.Trial, Now.AddDays(-1));

        Assert.Equal(LicenceBannerState.TrialEndingSoon, LicenceBanner.For(licence, Now, EndingSoon));
    }

    [Fact]
    public void TreatsATrialWithNoEndDateAsRunningRatherThanEnding()
    {
        // No end date is not "ends now", and warning about an ending nobody stated would be a
        // warning about nothing.
        var licence = LicenceOf(LicenceKind.Trial, endsAt: null);

        Assert.Equal(LicenceBannerState.TrialRunning, LicenceBanner.For(licence, Now, EndingSoon));
    }

    /// <summary>The threshold is configuration, so the sentence and the number behind it cannot drift.</summary>
    [Fact]
    public void ReadsTheThresholdFromItsArgument()
    {
        var licence = LicenceOf(LicenceKind.Trial, Now.AddDays(10));

        Assert.Equal(LicenceBannerState.TrialRunning, LicenceBanner.For(licence, Now, TimeSpan.FromDays(7)));
        Assert.Equal(LicenceBannerState.TrialEndingSoon, LicenceBanner.For(licence, Now, TimeSpan.FromDays(14)));
    }

    private static Licence LicenceOf(LicenceKind kind, DateTimeOffset? endsAt) =>
        new(kind, endsAt, "licence-id", "usage-secret");
}
