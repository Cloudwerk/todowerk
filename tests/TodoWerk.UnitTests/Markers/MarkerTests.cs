using TodoWerk.Domain.Hashtags;
using Xunit;

namespace TodoWerk.UnitTests.Markers;

/// <summary>
/// What counts as one Marker. The grammar is "one emoji as a reader sees it" (ADR-0014), and every
/// case below is a shape a reader would call one emoji and a naive length check would not.
/// </summary>
public sealed class MarkerTests
{
    /// <summary>
    /// Every shape that is one Marker. Shared with the test that says none of them is a Hashtag,
    /// so the two lists cannot drift apart.
    /// </summary>
    public static TheoryData<string> AcceptedShapes() =>
        new("🍞", "👍🏽", "👨‍👩‍👧", "🇩🇪", "1️⃣", "*️⃣", "🏴󠁧󠁢󠁥󠁮󠁧󠁿", "❤️", "▶️");

    [Theory]
    [MemberData(nameof(AcceptedShapes))]
    public void OneEmojiHoweverManyCodePoints_IsOneMarker(string candidate)
    {
        Assert.True(Marker.TryCreate(candidate, out var marker));
        Assert.Equal(candidate, marker.Text);
    }

    [Theory]
    [InlineData("🍞🥐")]
    [InlineData("🍞x")]
    [InlineData("x")]
    [InlineData("!")]
    [InlineData(" ")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("1")]
    [InlineData("bread")]
    public void AnythingElse_IsRefused(string? candidate)
    {
        Assert.False(Marker.TryCreate(candidate, out var marker));
        Assert.True(marker.IsEmpty);
    }

    /// <summary>
    /// One emoji by every other measure, and the one the Hashtag grammar would read as a tag: it
    /// opens on the marker character and its two combining marks are name runes, so a block of it
    /// at position zero would be indexed as a Hashtag the moment it was written (ADR-0014).
    /// </summary>
    [Fact]
    public void TheHashKeycap_IsRefusedBecauseTheGrammarWouldReadItAsATag()
    {
        Assert.False(Marker.TryCreate("#️⃣", out _));

        // The same cluster is a Hashtag to the extractor — which is the whole reason.
        Assert.Single(HashtagExtractor.Extract("#️⃣ Brot"));
    }

    /// <summary>
    /// A block is Markers written back to back, so one ending in a joiner would fuse with the
    /// next into a cluster no rule matches — and every Apply would write the block again in
    /// front of itself.
    /// </summary>
    [Fact]
    public void AMarkerEndingInAJoiner_IsRefused() =>
        Assert.False(Marker.TryCreate("👩\u200D", out _));

    /// <summary>
    /// Half a flag is a letter in a box, and a block made of them would map two Markers onto one
    /// Hashtag pair by accident.
    /// </summary>
    [Fact]
    public void OneRegionalIndicator_IsNotAMarker() =>
        Assert.False(Marker.TryCreate("\U0001F1E9", out _));

    /// <summary>
    /// A pasted paragraph is refused by length before anything walks it — the column that holds a
    /// Marker is sized for one emoji and nothing longer can ever be stored.
    /// </summary>
    [Fact]
    public void SomethingLongerThanTheColumn_IsRefused() =>
        Assert.False(Marker.TryCreate(new string('a', Marker.MaxLength + 1), out _));

    /// <summary>
    /// Two Markers are the same Marker when their bytes agree after NFC, the same rule the folded
    /// Hashtag key follows and the same one the binary-collated column enforces.
    /// </summary>
    [Fact]
    public void TwoMarkersOfTheSameEmoji_AreEqual()
    {
        Assert.True(Marker.TryCreate("🍞", out var first));
        Assert.True(Marker.TryCreate("🍞", out var second));

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    /// <summary>
    /// U+2709 with and without U+FE0F render as one glyph almost everywhere, and a reader who typed
    /// one into a title and the other into a rule meant the same emoji. Two rules for them would
    /// be two Markers in one block for one thing, which is what the uniqueness rule exists to stop.
    /// </summary>
    [Fact]
    public void TwoPresentationsOfOneEmoji_AreOneMarker()
    {
        Assert.True(Marker.TryCreate("\u2709", out var text));
        Assert.True(Marker.TryCreate("\u2709\uFE0F", out var emoji));

        Assert.Equal(text, emoji);
        Assert.Equal(text.GetHashCode(), emoji.GetHashCode());

        // What is written into a title is what the person chose, selector and all.
        Assert.Equal("\u2709\uFE0F", emoji.Text);
    }

    [Fact]
    public void ADefaultMarker_IsNotOne()
    {
        var marker = default(Marker);

        Assert.True(marker.IsEmpty);
        Assert.Equal(string.Empty, marker.Text);
    }
}
