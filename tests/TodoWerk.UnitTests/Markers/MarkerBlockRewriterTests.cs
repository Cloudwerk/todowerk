using TodoWerk.Domain.Hashtags;
using Xunit;

using static TodoWerk.UnitTests.Markers.MarkerBlockTests;

namespace TodoWerk.UnitTests.Markers;

/// <summary>
/// The write an Apply Markers performs, which ADR-0014 makes "rewrite the whole block, touch
/// nothing behind it". Adds and reorders; never removes.
/// </summary>
public sealed class MarkerBlockRewriterTests
{
    private static readonly IReadOnlyDictionary<Marker, Marker> NothingRetired =
        new Dictionary<Marker, Marker>();

    [Fact]
    public void ATitleWithNoBlock_GainsOne()
    {
        var rewritten = MarkerBlockRewriter.Rewrite(
            "Brot kaufen #bread",
            [Marker("🍞")],
            [Marker("🍞")],
            NothingRetired);

        Assert.Equal("🍞 Brot kaufen #bread", rewritten);
    }

    /// <summary>
    /// The skip the runner shares with the other three Changes: this task already reads the way
    /// the Change asked for.
    /// </summary>
    [Fact]
    public void ATitleThatAlreadyReadsThatWay_RewritesToNothing() =>
        Assert.Null(MarkerBlockRewriter.Rewrite(
            "🍞 Brot kaufen #bread",
            [Marker("🍞")],
            [Marker("🍞")],
            NothingRetired));

    [Fact]
    public void ApplyingTwice_ChangesNothingTheSecondTime()
    {
        var once = MarkerBlockRewriter.Rewrite(
            "Brot und Kaffee",
            [Marker("🍞"), Marker("☕")],
            [Marker("🍞"), Marker("☕")],
            NothingRetired);

        Assert.Equal("🍞☕ Brot und Kaffee", once);
        Assert.Null(MarkerBlockRewriter.Rewrite(
            once,
            [Marker("🍞"), Marker("☕")],
            [Marker("🍞"), Marker("☕")],
            NothingRetired));
    }

    /// <summary>
    /// The block is rewritten whole, so a Marker that arrived out of order is moved rather than
    /// left where it was — which is what makes a one-rule Apply and an all-rules Apply agree.
    /// </summary>
    [Fact]
    public void MarkersAreEmittedInRuleOrder_WhateverOrderTheyWereIn()
    {
        var rewritten = MarkerBlockRewriter.Rewrite(
            "☕🍞 Frühstück",
            [Marker("🍞"), Marker("☕")],
            [Marker("🍞"), Marker("☕")],
            NothingRetired);

        Assert.Equal("🍞☕ Frühstück", rewritten);
    }

    /// <summary>
    /// Applying only adds. A Marker whose Hashtag has left the task stays, after the ones that
    /// belong, until an explicit Remove Markers takes it away.
    /// </summary>
    [Fact]
    public void AStaleMarker_IsKeptAfterTheWantedOnes()
    {
        var rewritten = MarkerBlockRewriter.Rewrite(
            "☕ Brot kaufen #bread",
            [Marker("🍞")],
            [Marker("🍞"), Marker("☕")],
            NothingRetired);

        Assert.Equal("🍞☕ Brot kaufen #bread", rewritten);
    }

    [Fact]
    public void ADuplicateInTheBlock_Collapses()
    {
        var rewritten = MarkerBlockRewriter.Rewrite(
            "🍞🍞 Brot",
            [Marker("🍞")],
            [Marker("🍞")],
            NothingRetired);

        Assert.Equal("🍞 Brot", rewritten);
    }

    /// <summary>
    /// The one case where an Apply takes a Marker away: the rule that put it there has changed its
    /// own Marker, which is not a Hashtag going away.
    /// </summary>
    [Fact]
    public void ARetiredMarker_IsEmittedAsItsReplacement()
    {
        var rewritten = MarkerBlockRewriter.Rewrite(
            "🍞 Brot kaufen #bread",
            [Marker("🥐")],
            [Marker("🥐")],
            new Dictionary<Marker, Marker> { [Marker("🍞")] = Marker("🥐") });

        Assert.Equal("🥐 Brot kaufen #bread", rewritten);
    }

    /// <summary>
    /// And it is swapped even where the Hashtag has gone, because the swap is about the rule's
    /// Marker and not about the task's tags (ADR-0014).
    /// </summary>
    [Fact]
    public void ARetiredMarkerWhoseHashtagIsGone_IsStillSwapped()
    {
        var rewritten = MarkerBlockRewriter.Rewrite(
            "🍞 Etwas anderes",
            [],
            [Marker("🥐")],
            new Dictionary<Marker, Marker> { [Marker("🍞")] = Marker("🥐") });

        Assert.Equal("🥐 Etwas anderes", rewritten);
    }

    [Fact]
    public void AnEmojiNobodyMadeARuleAbout_IsLeftWhereItIs()
    {
        var rewritten = MarkerBlockRewriter.Rewrite(
            "🎉 Party #bread",
            [Marker("🍞")],
            [Marker("🍞")],
            NothingRetired);

        Assert.Equal("🍞 🎉 Party #bread", rewritten);
    }

    [Fact]
    public void ATaskWithNoRulesInScopeAndNoBlock_RewritesToNothing() =>
        Assert.Null(MarkerBlockRewriter.Rewrite(
            "Nur ein Titel",
            [],
            [Marker("🍞")],
            NothingRetired));

    /// <summary>A title that is only a block gains no trailing space nobody typed.</summary>
    [Fact]
    public void ATitleThatIsOnlyABlock_KeepsNoTrailingSpace()
    {
        var rewritten = MarkerBlockRewriter.Rewrite(
            "☕🍞",
            [Marker("🍞"), Marker("☕")],
            [Marker("🍞"), Marker("☕")],
            NothingRetired);

        Assert.Equal("🍞☕", rewritten);
    }

    /// <summary>
    /// A leading space must not become a second block: " 🍞 Brot" read as blockless would gain a
    /// 🍞 in front of the 🍞, and the next Apply a third.
    /// </summary>
    [Fact]
    public void ATitleWithASpaceBeforeItsBlock_IsNotGivenASecondBlock() =>
        Assert.Null(MarkerBlockRewriter.Rewrite(
            " 🍞 Brot #bread",
            [Marker("🍞")],
            [Marker("🍞")],
            NothingRetired));

    /// <summary>
    /// And whitespace the title opened with stays where it was typed: the block goes after it,
    /// which is where the next read will look for it.
    /// </summary>
    [Fact]
    public void ABlockGoesAfterLeadingWhitespace_WhichStays()
    {
        var rewritten = MarkerBlockRewriter.Rewrite(
            "  Brot #bread",
            [Marker("🍞")],
            [Marker("🍞")],
            NothingRetired);

        Assert.Equal("  🍞 Brot #bread", rewritten);
        Assert.Null(MarkerBlockRewriter.Rewrite(rewritten, [Marker("🍞")], [Marker("🍞")], NothingRetired));
    }

    [Fact]
    public void ABlockAfterLeadingWhitespace_IsReorderedInPlace()
    {
        var rewritten = MarkerBlockRewriter.Rewrite(
            " ☕🍞 Frühstück",
            [Marker("🍞"), Marker("☕")],
            [Marker("🍞"), Marker("☕")],
            NothingRetired);

        Assert.Equal(" 🍞☕ Frühstück", rewritten);
    }

    /// <summary>A tab somebody typed after their block is not this function's to turn into a space.</summary>
    [Fact]
    public void ATabAfterTheBlock_Stays()
    {
        var rewritten = MarkerBlockRewriter.Rewrite(
            "☕🍞\tFrühstück",
            [Marker("🍞"), Marker("☕")],
            [Marker("🍞"), Marker("☕")],
            NothingRetired);

        Assert.Equal("🍞☕\tFrühstück", rewritten);
    }

    /// <summary>A space the user typed after a block that is the whole title stays.</summary>
    [Fact]
    public void ATypedTrailingSpaceAfterABlockOnItsOwn_Stays() =>
        Assert.Null(MarkerBlockRewriter.Rewrite(
            "🍞 ",
            [Marker("🍞")],
            [Marker("🍞")],
            NothingRetired));

    [Fact]
    public void TheRestOfTheTitleIsCopiedVerbatim()
    {
        var rewritten = MarkerBlockRewriter.Rewrite(
            "🍞  Brot   kaufen — #bread!",
            [Marker("🍞"), Marker("☕")],
            [Marker("🍞"), Marker("☕")],
            NothingRetired);

        Assert.Equal("🍞☕  Brot   kaufen — #bread!", rewritten);
    }
}
