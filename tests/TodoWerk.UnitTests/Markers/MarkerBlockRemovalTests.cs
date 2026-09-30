using TodoWerk.Domain.Hashtags;
using Xunit;

using static TodoWerk.UnitTests.Markers.MarkerBlockTests;

namespace TodoWerk.UnitTests.Markers;

/// <summary>
/// The write a Remove Markers performs: take Markers out of the block at the front, and do nothing
/// else. The mirror of <see cref="MarkerBlockRewriterTests"/>, which never removes.
/// <para>
/// Two arguments decide it. <c>wanted</c> is what a standing rule asks for on this task, and is
/// what a Marker has to fail to be stale; <c>removable</c> is the run's scope, and is what a stale
/// Marker has to be in before this run may touch it. A Marker outside the scope stays however
/// stale it is, which is what makes a one-rule Remove narrow.
/// </para>
/// </summary>
public sealed class MarkerBlockRemovalTests
{
    [Fact]
    public void AStaleMarker_IsTakenOut()
    {
        var removed = MarkerBlockRewriter.Remove(
            "🍞 Etwas anderes",
            wanted: [],
            known: [Marker("🍞")],
            removable: [Marker("🍞")]);

        Assert.Equal("Etwas anderes", removed);
    }

    /// <summary>
    /// The Marker a standing rule asks for on this task is not stale, whatever the scope says. This
    /// is the whole safety of the operation: a task that carries its Hashtag keeps its Marker.
    /// </summary>
    [Fact]
    public void AMarkerAStandingRuleAsksForOnThisTask_Stays() =>
        Assert.Null(MarkerBlockRewriter.Remove(
            "🍞 Brot kaufen #bread",
            wanted: [Marker("🍞")],
            known: [Marker("🍞")],
            removable: [Marker("🍞")]));

    /// <summary>The scope, which is what a one-rule Remove narrows with.</summary>
    [Fact]
    public void AStaleMarkerOutsideTheScope_Stays()
    {
        var removed = MarkerBlockRewriter.Remove(
            "🍞☕ Etwas anderes",
            wanted: [],
            known: [Marker("🍞"), Marker("☕")],
            removable: [Marker("☕")]);

        Assert.Equal("🍞 Etwas anderes", removed);
    }

    /// <summary>
    /// What is left keeps the order it was written in. Removing is not applying: a block an Apply
    /// would still reorder is not this operation's to reorder, and a Remove that quietly did both
    /// would write a title nobody previewed as a reorder.
    /// </summary>
    [Fact]
    public void WhatIsLeft_KeepsTheOrderItWasWrittenIn()
    {
        var removed = MarkerBlockRewriter.Remove(
            "☕🥐🍞 Frühstück #coffee #bread",
            wanted: [Marker("🍞"), Marker("☕")],
            known: [Marker("🍞"), Marker("☕"), Marker("🥐")],
            removable: [Marker("🍞"), Marker("☕"), Marker("🥐")]);

        Assert.Equal("☕🍞 Frühstück #coffee #bread", removed);
    }

    /// <summary>Removing never adds one either, however much a rule asks for it.</summary>
    [Fact]
    public void AWantedMarkerThatIsNotThere_IsNotAdded() =>
        Assert.Null(MarkerBlockRewriter.Remove(
            "Brot kaufen #bread",
            wanted: [Marker("🍞")],
            known: [Marker("🍞")],
            removable: [Marker("🍞")]));

    /// <summary>
    /// The block owns the one whitespace character behind it (ADR-0014), so emptying the block
    /// takes that space with it — and what is left starts where the rest of the title always did.
    /// </summary>
    [Fact]
    public void EmptyingTheBlock_TakesItsOwnSpaceWithIt()
    {
        Assert.Equal("Brot kaufen #bread", MarkerBlockRewriter.Remove(
            "🍞☕ Brot kaufen #bread",
            wanted: [],
            known: [Marker("🍞"), Marker("☕")],
            removable: [Marker("🍞"), Marker("☕")]));

        // A tab is the block's separator too, and goes the same way.
        Assert.Equal("Frühstück", MarkerBlockRewriter.Remove(
            "🍞\tFrühstück",
            wanted: [],
            known: [Marker("🍞")],
            removable: [Marker("🍞")]));
    }

    /// <summary>
    /// Whitespace the title opened with is not the block's, so it stays exactly as it was typed —
    /// the same rule that stops an Apply putting a second block in front of the first.
    /// </summary>
    [Fact]
    public void WhitespaceTheTitleOpenedWith_Stays() =>
        Assert.Equal("  Brot #bread", MarkerBlockRewriter.Remove(
            "  🍞 Brot #bread",
            wanted: [],
            known: [Marker("🍞")],
            removable: [Marker("🍞")]));

    /// <summary>
    /// A title that is nothing but a block would be emptied altogether. Answered rather than
    /// refused here — this is a pure function and an empty title is a true answer to what was
    /// asked — and passed over by the caller, which is where a task Microsoft To Do would reject
    /// is skipped by name.
    /// </summary>
    [Fact]
    public void ATitleThatIsOnlyABlock_IsAnsweredEmpty()
    {
        Assert.Equal(string.Empty, MarkerBlockRewriter.Remove(
            "🍞",
            wanted: [],
            known: [Marker("🍞")],
            removable: [Marker("🍞")]));

        // The typed trailing space is the block's separator, so it goes with the block.
        Assert.Equal(string.Empty, MarkerBlockRewriter.Remove(
            "🍞 ",
            wanted: [],
            known: [Marker("🍞")],
            removable: [Marker("🍞")]));
    }

    /// <summary>
    /// An emoji nobody made a rule about was never in the block, so it is never a candidate — the
    /// same property of <see cref="MarkerBlock"/> that stops an Apply reordering it. This is what
    /// makes "an emoji no rule of theirs has ever mentioned" unreachable rather than merely
    /// declined: a Remove cannot touch text it does not recognise as a Marker.
    /// </summary>
    [Fact]
    public void AnEmojiNobodyMadeARuleAbout_IsNotACandidate() =>
        Assert.Null(MarkerBlockRewriter.Remove(
            "🎉 Party",
            wanted: [],
            known: [Marker("🍞")],
            removable: [Marker("🍞")]));

    /// <summary>And it is still not a candidate when a real block sits in front of it.</summary>
    [Fact]
    public void AnEmojiNobodyMadeARuleAbout_SurvivesTheBlockInFrontOfItGoing() =>
        Assert.Equal("🎉 Party", MarkerBlockRewriter.Remove(
            "🍞 🎉 Party",
            wanted: [],
            known: [Marker("🍞")],
            removable: [Marker("🍞")]));

    [Fact]
    public void ATitleWithNoBlockAtAll_RemovesToNothing() =>
        Assert.Null(MarkerBlockRewriter.Remove(
            "Nur ein Titel",
            wanted: [],
            known: [Marker("🍞")],
            removable: [Marker("🍞")]));

    /// <summary>
    /// A duplicate somebody's own typing left behind goes with the Marker it duplicates: the block
    /// is written whole, so both copies are read and neither is emitted.
    /// </summary>
    [Fact]
    public void ADuplicateOfAStaleMarker_GoesWithIt() =>
        Assert.Equal("Brot", MarkerBlockRewriter.Remove(
            "🍞🍞 Brot",
            wanted: [],
            known: [Marker("🍞")],
            removable: [Marker("🍞")]));

    /// <summary>
    /// A duplicate of a Marker that stays collapses to one, because the block is rewritten whole
    /// and a Marker is emitted once. The one tidy-up removing shares with applying.
    /// </summary>
    [Fact]
    public void ADuplicateOfAMarkerThatStays_Collapses() =>
        Assert.Equal("🍞 Brot #bread", MarkerBlockRewriter.Remove(
            "🍞🍞 Brot #bread",
            wanted: [Marker("🍞")],
            known: [Marker("🍞")],
            removable: [Marker("🍞")]));

    /// <summary>
    /// Nothing in scope is stale, so nothing is written — the "already read the way the change
    /// asked for" skip the other Changes share.
    /// </summary>
    [Fact]
    public void ABlockWithNothingStaleInIt_RemovesToNothing() =>
        Assert.Null(MarkerBlockRewriter.Remove(
            "🍞☕ Frühstück #coffee #bread",
            wanted: [Marker("🍞"), Marker("☕")],
            known: [Marker("🍞"), Marker("☕")],
            removable: [Marker("🍞"), Marker("☕")]));

    [Fact]
    public void RemovingTwice_ChangesNothingTheSecondTime()
    {
        var once = MarkerBlockRewriter.Remove(
            "🍞☕ Frühstück #coffee",
            wanted: [Marker("☕")],
            known: [Marker("🍞"), Marker("☕")],
            removable: [Marker("🍞"), Marker("☕")]);

        Assert.Equal("☕ Frühstück #coffee", once);
        Assert.Null(MarkerBlockRewriter.Remove(
            once,
            wanted: [Marker("☕")],
            known: [Marker("🍞"), Marker("☕")],
            removable: [Marker("🍞"), Marker("☕")]));
    }

    /// <summary>
    /// Only the block and its own one space go. A second space somebody typed is the rest of the
    /// title's, and the rest of the title is nobody's to tidy.
    /// </summary>
    [Fact]
    public void TheRestOfTheTitleIsCopiedVerbatim() =>
        Assert.Equal(" Brot   kaufen — #bread!", MarkerBlockRewriter.Remove(
            "🍞  Brot   kaufen — #bread!",
            wanted: [],
            known: [Marker("🍞")],
            removable: [Marker("🍞")]));

    [Fact]
    public void ANullTitle_RemovesToNothing() =>
        Assert.Null(MarkerBlockRewriter.Remove(null, [], [Marker("🍞")], [Marker("🍞")]));
}
