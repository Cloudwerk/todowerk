using TodoWerk.Domain.Hashtags;
using Xunit;

using static TodoWerk.UnitTests.Markers.MarkerBlockTests;

namespace TodoWerk.UnitTests.Markers;

/// <summary>
/// The half of a block that does not depend on whose rules are being asked about, which is the half
/// the index can store.
/// <para>
/// One property carries the whole optimisation, and it is the last test here: reading a block out
/// of the stored run answers exactly what reading it out of the whole title does. If that ever
/// stops being true, the coverage figure and the Change beside it part company silently.
/// </para>
/// </summary>
public sealed class LeadingEmojiTests
{
    [Fact]
    public void ATitleOpeningWithEmoji_YieldsThem() =>
        Assert.Equal("🍞☕", MarkerBlock.LeadingEmojiOf("🍞☕ Frühstück #coffee #bread"));

    [Fact]
    public void ATitleOpeningWithText_YieldsNothing() =>
        Assert.Equal(string.Empty, MarkerBlock.LeadingEmojiOf("Brot kaufen #bread"));

    /// <summary>
    /// It stops at the first grapheme that is not an emoji, so an emoji in the middle of a sentence
    /// is text — the same thing the block reader says about it.
    /// </summary>
    [Fact]
    public void AnEmojiInTheMiddleOfATitle_IsNotInTheRun() =>
        Assert.Equal(string.Empty, MarkerBlock.LeadingEmojiOf("Brot 🍞 kaufen"));

    /// <summary>
    /// Every emoji at the front, whether or not anybody made a rule about it. That is the point:
    /// the run is written when the task is indexed, long before any question about a person's
    /// rules — so it cannot leave out the ones that are not Markers yet.
    /// </summary>
    [Fact]
    public void AnEmojiNobodyMadeARuleAbout_IsStillInTheRun() =>
        Assert.Equal("🎉🍞", MarkerBlock.LeadingEmojiOf("🎉🍞 Party #bread"));

    /// <summary>Whitespace before the run is stepped over, as the block reader steps over it.</summary>
    [Fact]
    public void WhitespaceBeforeTheRun_IsSteppedOver() =>
        Assert.Equal("🍞", MarkerBlock.LeadingEmojiOf("  🍞 Brot"));

    [Fact]
    public void ATitleThatIsOnlyEmoji_IsAllRun() =>
        Assert.Equal("🍞☕", MarkerBlock.LeadingEmojiOf("🍞☕"));

    [Fact]
    public void ANullOrEmptyTitle_YieldsNothing()
    {
        Assert.Equal(string.Empty, MarkerBlock.LeadingEmojiOf(null));
        Assert.Equal(string.Empty, MarkerBlock.LeadingEmojiOf(string.Empty));
    }

    /// <summary>
    /// A flag, a keycap and a skin tone are each one emoji, so each is one grapheme of the run —
    /// the same clusters <see cref="Marker"/> accepts, asked of a title.
    /// </summary>
    [Fact]
    public void OneEmojiHoweverManyCodePointsItTakes_IsOneGrapheme()
    {
        Assert.Equal("🇩🇪", MarkerBlock.LeadingEmojiOf("🇩🇪 Deutschland"));
        Assert.Equal("👍🏽", MarkerBlock.LeadingEmojiOf("👍🏽 Gut"));
    }

    /// <summary>
    /// The keycap hash is one emoji by every other measure and the Hashtag grammar reads it as a
    /// tag, so <see cref="Marker"/> refuses it — and the run has to refuse it too, or the index
    /// would store something no rule could ever match.
    /// </summary>
    [Fact]
    public void TheHashKeycap_IsNotInTheRun() =>
        Assert.Equal(string.Empty, MarkerBlock.LeadingEmojiOf("#️⃣ Nummer"));

    /// <summary>
    /// A run longer than the column is cut, and the cut lands on a grapheme boundary rather than
    /// inside one. Counting then sees fewer Markers than the title holds, which is the safe
    /// direction: a figure reads low rather than wrong.
    /// </summary>
    [Fact]
    public void ARunLongerThanTheBound_IsCutOnAGraphemeBoundary()
    {
        // Each 🍞 is two chars, so this is four times the bound before cutting.
        var run = MarkerBlock.LeadingEmojiOf(string.Concat(Enumerable.Repeat("🍞", MaxRunEmoji * 4)) + " x");

        Assert.Equal(MarkerBlock.MaxLeadingEmojiLength, run.Length);
        Assert.Equal(string.Concat(Enumerable.Repeat("🍞", MaxRunEmoji)), run);
    }

    /// <summary>
    /// The property the stored run rests on: what the index stores answers the same question the
    /// title does. The run is copied out of the title verbatim, so the block reader compares the
    /// same bytes either way — presentation selectors, flags and all.
    /// </summary>
    [Theory]
    [InlineData("🍞☕ Frühstück #coffee #bread")]
    [InlineData("☕🍞 Frühstück")]
    [InlineData("🎉🍞 Party #bread")]
    [InlineData("  🍞 Brot #bread")]
    [InlineData("Brot kaufen #bread")]
    [InlineData("🍞")]
    [InlineData("🍞🍞 Brot")]
    [InlineData("✉ Post #mail")]
    [InlineData("✉️ Post #mail")]
    public void ABlockReadFromTheRun_IsTheBlockReadFromTheTitle(string title)
    {
        var known = Set("🍞", "☕", "✉️");

        Assert.Equal(
            MarkerBlock.Read(title, known).Markers,
            MarkerBlock.Read(MarkerBlock.LeadingEmojiOf(title), known).Markers);
    }

    /// <summary>How many whole 🍞 fit inside the bound.</summary>
    private const int MaxRunEmoji = MarkerBlock.MaxLeadingEmojiLength / 2;
}
