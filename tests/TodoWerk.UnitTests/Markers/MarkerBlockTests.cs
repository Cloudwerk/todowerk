using TodoWerk.Domain.Hashtags;
using Xunit;

namespace TodoWerk.UnitTests.Markers;

/// <summary>
/// Where the block ends and the title begins. Defined against one person's Markers rather than
/// against emoji in general, which is what keeps somebody's decorative sparkle out of a reorder.
/// </summary>
public sealed class MarkerBlockTests
{
    private static readonly IReadOnlySet<Marker> Known = Set("🍞", "🥐");

    [Fact]
    public void ATitleThatOpensWithAKnownMarker_HasABlock()
    {
        var block = MarkerBlock.Read("🍞 Brot kaufen", Known);

        Assert.Equal([Marker("🍞")], block.Markers);
        Assert.Equal("Brot kaufen", "🍞 Brot kaufen"[block.RestStart..]);
    }

    [Fact]
    public void SeveralKnownMarkers_AreOneBlock()
    {
        var block = MarkerBlock.Read("🍞🥐 Brot kaufen", Known);

        Assert.Equal([Marker("🍞"), Marker("🥐")], block.Markers);
    }

    /// <summary>
    /// The rule that makes an Apply safe to run against a title somebody decorated themselves: an
    /// emoji nobody wrote a rule about opens the rest of the title.
    /// </summary>
    [Fact]
    public void AnEmojiNobodyMadeARuleAbout_IsNotABlock()
    {
        var block = MarkerBlock.Read("🎉 Party", Known);

        Assert.True(block.IsEmpty);
        Assert.Equal(0, block.RestStart);
    }

    [Fact]
    public void ATitleThatIsOnlyABlock_HasNoRest()
    {
        var title = "🍞🥐";
        var block = MarkerBlock.Read(title, Known);

        Assert.Equal(2, block.Markers.Count);
        Assert.Equal(string.Empty, title[block.RestStart..]);
    }

    /// <summary>
    /// Exactly one space belongs to the block. A second one the user typed is part of their title
    /// and stays there — TodoWerk is a hashtag manager, not a title tidier.
    /// </summary>
    [Fact]
    public void OnlyOneSpaceBelongsToTheBlock()
    {
        var title = "🍞  Brot";
        var block = MarkerBlock.Read(title, Known);

        Assert.Equal(" Brot", title[block.RestStart..]);
    }

    /// <summary>
    /// A title pasted with a space in front still has its block at the front. Reading past the
    /// space is what stops a rewrite putting a second block in front of the first.
    /// </summary>
    [Fact]
    public void WhitespaceBeforeTheBlock_BelongsToTheBlock()
    {
        var title = "  🍞 Brot";
        var block = MarkerBlock.Read(title, Known);

        Assert.Equal([Marker("🍞")], block.Markers);
        Assert.Equal("Brot", title[block.RestStart..]);
    }

    /// <summary>Without a block, the one that an Apply adds would begin where the whitespace ends.</summary>
    [Fact]
    public void WhitespaceBeforeNoBlock_IsWhereABlockWouldBegin()
    {
        var title = "  Brot";
        var block = MarkerBlock.Read(title, Known);

        Assert.True(block.IsEmpty);
        Assert.Equal(2, block.Start);
        Assert.Equal("Brot", title[block.RestStart..]);
    }

    [Fact]
    public void ATitleWithNoLeadingMarker_HasNoBlock()
    {
        var block = MarkerBlock.Read("Brot kaufen 🍞", Known);

        Assert.True(block.IsEmpty);
        Assert.Equal(0, block.RestStart);
    }

    [Fact]
    public void APersonWithNoRules_HasNoBlockAnywhere()
    {
        var block = MarkerBlock.Read("🍞 Brot", Set());

        Assert.True(block.IsEmpty);
    }

    internal static Marker Marker(string text)
    {
        Assert.True(Domain.Hashtags.Marker.TryCreate(text, out var marker));

        return marker;
    }

    internal static IReadOnlySet<Marker> Set(params string[] markers) =>
        new HashSet<Marker>(markers.Select(Marker));
}
