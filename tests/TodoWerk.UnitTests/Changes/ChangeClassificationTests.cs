using TodoWerk.Domain.Changes;
using TodoWerk.Domain.Hashtags;
using Xunit;

namespace TodoWerk.UnitTests.Changes;

/// <summary>
/// Which operation a Change turns out to be is read off its shape, never chosen by the user
/// (ADR-0006). One mechanism, three names it reports back.
/// </summary>
public sealed class ChangeClassificationTests
{
    [Fact]
    public void OneSourceFoldingToTheSameKey_IsNormaliseCasing() =>
        Assert.Equal(
            ChangeKind.NormaliseCasing,
            Change.Classify([Key("works")], "Works", targetHashtagExists: true));

    [Fact]
    public void OneSourceTakingANameNothingUses_IsARename() =>
        Assert.Equal(
            ChangeKind.Rename,
            Change.Classify([Key("Prio1")], "Priority1", targetHashtagExists: false));

    [Fact]
    public void TwoSources_AreAMerge() =>
        Assert.Equal(
            ChangeKind.Merge,
            Change.Classify([Key("Work"), Key("Works")], "Works", targetHashtagExists: true));

    /// <summary>
    /// Renaming onto a Hashtag that already exists folds two into one, which is a Merge however the
    /// user reached it — and the only one of the three that destroys a distinction they made, so
    /// the UI asks a second time.
    /// </summary>
    [Fact]
    public void OneSourceLandingOnAnExistingHashtag_IsAMerge() =>
        Assert.Equal(
            ChangeKind.Merge,
            Change.Classify([Key("kunde")], "customer", targetHashtagExists: true));

    /// <summary>
    /// Normalise Casing wins over the existence of the target, because the target that exists is
    /// the Hashtag being normalised: <c>#Works</c> already being in the index is exactly what
    /// makes <c>works</c> → <c>Works</c> a casing clean-up rather than a merge into a stranger.
    /// </summary>
    [Fact]
    public void CasingWinsOverTheTargetAlreadyExisting() =>
        Assert.Equal(
            ChangeKind.NormaliseCasing,
            Change.Classify([Key("STRASSE")], "Strasse", targetHashtagExists: true));

    private static string Key(string spelling) => HashtagKey.Fold(spelling);
}
