using TodoWerk.Domain.Hashtags;
using Xunit;

namespace TodoWerk.UnitTests.Markers;

/// <summary>
/// The two properties that let a Marker block and a Hashtag share a title (ADR-0014). Both are
/// already true of the extractor and neither was ever asserted, so they are asserted here — a
/// block that broke indexing would be a Change writing titles the index then stopped counting.
/// </summary>
public sealed class MarkersAndHashtagsTests
{
    /// <summary>The block ends in whitespace, and whitespace is what opens a Hashtag.</summary>
    [Theory]
    [InlineData("🍞 #bread kaufen")]
    [InlineData("🍞☕ #bread kaufen")]
    [InlineData("👨‍👩‍👧 #bread kaufen")]
    [InlineData("🇩🇪 #bread kaufen")]
    public void AHashtagBehindABlock_StillExtracts(string title)
    {
        var extracted = HashtagExtractor.Extract(title);

        Assert.Equal("bread", Assert.Single(extracted).Spelling);
    }

    /// <summary>
    /// An emoji is not a name rune, so it ends a name rather than joining it. Without this a
    /// Marker written straight after a tag would invent a Hashtag nobody typed.
    /// </summary>
    [Fact]
    public void AnEmojiGluedToATag_IsNotPartOfItsName()
    {
        var extracted = HashtagExtractor.Extract("kaufen #bread🍞 heute");

        Assert.Equal("bread", Assert.Single(extracted).Spelling);
    }

    /// <summary>
    /// And the other way round: a marker glued to the back of an emoji opens nothing, because a
    /// Hashtag opens at the start of a title or after whitespace and never mid-word (ADR-0005).
    /// A block always writes its space, so this case is what an Apply must never produce.
    /// </summary>
    [Fact]
    public void AMarkerGluedToTheFrontOfATag_HidesIt() =>
        Assert.Empty(HashtagExtractor.Extract("🍞#bread kaufen"));

    /// <summary>
    /// A Marker is never a Hashtag: the block contributes nothing to the index — for every shape
    /// <see cref="MarkerTests"/> accepts, because the one shape it refuses is refused for exactly
    /// this.
    /// </summary>
    [Theory]
    [MemberData(nameof(MarkerTests.AcceptedShapes), MemberType = typeof(MarkerTests))]
    public void ABlockOnItsOwn_ExtractsNothing(string block)
    {
        Assert.Empty(HashtagExtractor.Extract(block));
        Assert.Empty(HashtagExtractor.Extract(block + block + " Frühstück"));
    }
}
