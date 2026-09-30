using TodoWerk.Domain.Indexing;
using Xunit;

namespace TodoWerk.UnitTests.Indexing;

/// <summary>
/// The near-duplicate flag, which is deliberately conservative: a false positive here is a user
/// merging two Hashtags that were never the same, and a merge is not something they can casually
/// undo. Every test below is either a pair the flag must catch or a pair it must leave alone, and
/// the second kind matters more.
/// </summary>
public sealed class NearDuplicateDetectorTests
{
    [Fact]
    public void OneTypo_IsFlagged() =>
        AssertPaired("KUNDE", "KUNED");

    [Fact]
    public void AMissingLetter_IsFlagged() =>
        AssertPaired("PROJEKT", "PROJEKTE");

    /// <summary>
    /// Two tags that merely start alike are not near-duplicates. "#urlaub" and "#urlaubsplanung"
    /// are a prefix pair and nothing more, and offering to merge them would be wrong.
    /// </summary>
    [Fact]
    public void ALongerWordSharingAPrefix_IsNotFlagged() =>
        AssertNotPaired("URLAUB", "URLAUBSPLANUNG");

    /// <summary>
    /// Short names are excluded outright: at three characters, one edit is most of the word, and
    /// "#q1" against "#q2" is the shape that produces nonsense suggestions.
    /// </summary>
    [Theory]
    [InlineData("Q1", "Q2")]
    [InlineData("WEB", "WEG")]
    public void ShortNames_AreNotFlagged(string first, string second) =>
        AssertNotPaired(first, second);

    [Fact]
    public void UnrelatedNames_AreNotFlagged() =>
        AssertNotPaired("KUNDE", "RECHNUNG");

    /// <summary>
    /// Casing never reaches here — the keys are already folded, so <c>#Work</c> and <c>#work</c>
    /// are one key and cannot be a pair. Conflating the two flags was a real risk: the casing flag
    /// is one Hashtag with two Spellings, and a near-duplicate is two Hashtags (ADR-0005).
    /// </summary>
    [Fact]
    public void TheSameKeyTwice_IsNotAPairWithItself() =>
        AssertNotPaired("KUNDE", "KUNDE");

    /// <summary>
    /// German data is the data this was built against, and an umlaut is one edit away from its
    /// base letter — which is exactly the mistake a user makes and wants told about.
    /// </summary>
    [Fact]
    public void AnUmlautAgainstItsBaseLetter_IsFlagged() =>
        AssertPaired("PRÜFUNG", "PRUFUNG");

    [Fact]
    public void EveryTagInAFlaggedPair_IsFlagged()
    {
        var flagged = NearDuplicateDetector.Flag(["KUNDE", "KUNED", "RECHNUNG"]);

        Assert.Equal(["KUNDE", "KUNED"], flagged.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void NothingToCompare_FlagsNothing()
    {
        Assert.Empty(NearDuplicateDetector.Flag([]));
        Assert.Empty(NearDuplicateDetector.Flag(["KUNDE"]));
    }

    /// <summary>
    /// The inventory is thousands of rows per tenant (ADR-0003), and this runs on every page load.
    /// A pairwise sweep over five thousand keys is twelve million comparisons; the length bucket
    /// is what keeps it off the request thread's critical path.
    /// </summary>
    [Fact]
    public void ALargeInventory_IsFlaggedQuickly()
    {
        var keys = Enumerable.Range(0, 5000)
            .Select(index => $"HASHTAG{index.ToString(System.Globalization.CultureInfo.InvariantCulture)}")
            .ToList();

        var started = System.Diagnostics.Stopwatch.StartNew();
        var flagged = NearDuplicateDetector.Flag(keys);
        started.Stop();

        Assert.True(
            started.ElapsedMilliseconds < 2000,
            $"flagging 5000 keys took {started.ElapsedMilliseconds}ms");

        // Sanity: this data really does contain near-duplicates ("HASHTAG1"/"HASHTAG11"), so the
        // timing above is not the speed of doing nothing.
        Assert.NotEmpty(flagged);
    }

    private static void AssertPaired(string first, string second) =>
        Assert.Equal(2, NearDuplicateDetector.Flag([first, second]).Count);

    private static void AssertNotPaired(string first, string second) =>
        Assert.Empty(NearDuplicateDetector.Flag([first, second]));
}
