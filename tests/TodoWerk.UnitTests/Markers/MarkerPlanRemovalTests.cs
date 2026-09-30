using TodoWerk.Domain.Changes;
using TodoWerk.Domain.Hashtags;
using Xunit;

namespace TodoWerk.UnitTests.Markers;

/// <summary>
/// What a Remove Markers takes out of one title, decided from the Markers the Change carries — the
/// same copy an Apply is computed from, so that the two directions cannot disagree about what a
/// block is.
/// <para>
/// One question decides every case: does a standing rule ask for this Marker on this task? A
/// Marker that fails it is stale. What a Marker is stale <em>against</em> is the task's own
/// Hashtags, read out of the title through the grammar the index counted them with, so "the tag has
/// gone" is answered from the title rather than from a table that may have moved on.
/// </para>
/// </summary>
public sealed class MarkerPlanRemovalTests
{
    /// <summary>The two rules, in a Remove's scope — which is what makes their Markers removable.</summary>
    private static readonly AppliedMarker Bread = new("BREAD", "bread", "🍞", null, 10, Removable: true);

    private static readonly AppliedMarker Coffee = new("COFFEE", "coffee", "☕", null, 20, Removable: true);

    /// <summary>The plain case: the Hashtag left the task and its Marker stayed behind.</summary>
    [Fact]
    public void AMarkerWhoseHashtagHasLeftTheTask_IsRemoved() =>
        Assert.Equal("Etwas anderes", MarkerPlan.From([Bread, Coffee]).RemoveFor("🍞 Etwas anderes"));

    /// <summary>
    /// And the case that must never be the second one: a task that still carries its Hashtag keeps
    /// its Marker. What a standing rule asks for on this task is never stale, whatever the scope
    /// says.
    /// </summary>
    [Fact]
    public void AMarkerWhoseHashtagIsStillOnTheTask_Stays() =>
        Assert.Null(MarkerPlan.From([Bread, Coffee]).RemoveFor("🍞 Brot kaufen #bread"));

    [Fact]
    public void AMixedBlock_LosesOnlyTheStaleHalf() =>
        Assert.Equal("☕ Kaffee #coffee", MarkerPlan.From([Bread, Coffee]).RemoveFor("🍞☕ Kaffee #coffee"));

    /// <summary>
    /// The Marker of a rule the person deleted. Nothing standing asks for it anywhere, so it is
    /// stale on every task that carries it — and this is the run that finally empties the rows
    /// ADR-0014 keeps only so the block goes on being read whole.
    /// </summary>
    [Fact]
    public void AnAbandonedMarker_IsRemovedWhateverTheTaskCarries()
    {
        var plan = MarkerPlan.From(
        [
            Coffee,
            new AppliedMarker(string.Empty, string.Empty, "🍞", null, int.MaxValue, Abandoned: true, Removable: true),
        ]);

        Assert.Equal("☕ Frühstück #coffee #bread", plan.RemoveFor("☕🍞 Frühstück #coffee #bread"));
        Assert.Equal("Nur so", plan.RemoveFor("🍞 Nur so"));
    }

    /// <summary>
    /// A Marker the rule has retired, on a task that still carries the rule's Hashtag: the next
    /// Apply swaps it for the rule's own, so it is not stale — it is out of date, which is a
    /// different thing with a different Change to fix it.
    /// </summary>
    [Fact]
    public void ARetiredMarkerOnATaskThatStillCarriesTheHashtag_Stays() =>
        Assert.Null(MarkerPlan
            .From([new AppliedMarker("BREAD", "bread", "🥐", "🍞", 10, Removable: true)])
            .RemoveFor("🍞 Brot kaufen #bread"));

    /// <summary>
    /// And the same Marker where the Hashtag has gone: no rule asks for it on this task, so it
    /// goes. An Apply would have swapped it — a rule's own Marker is swapped wherever it sits — and
    /// a Remove is being asked to clear this task instead.
    /// </summary>
    [Fact]
    public void ARetiredMarkerWhereTheHashtagHasGone_IsRemoved() =>
        Assert.Equal("Nur so", MarkerPlan
            .From([new AppliedMarker("BREAD", "bread", "🥐", "🍞", 10, Removable: true)])
            .RemoveFor("🍞 Nur so"));

    /// <summary>
    /// The scope, which is what a one-rule Remove narrows with: a Marker outside it stays however
    /// stale it is. Both rules are read — the block needs all of them to be read whole — and only
    /// the one in scope may go.
    /// </summary>
    [Fact]
    public void AStaleMarkerOutsideTheScope_Stays() =>
        Assert.Equal("🍞 Etwas anderes", MarkerPlan
            .From([Bread with { Removable = false }, Coffee])
            .RemoveFor("🍞☕ Etwas anderes"));

    /// <summary>
    /// A scope carries the Marker its rule retired as well as the one it holds now: that one is the
    /// same rule's residue, and a scope that named one without the other would offer to clear a
    /// rule's Markers and then leave half of them.
    /// </summary>
    [Fact]
    public void AScopedRule_CarriesTheMarkerItRetired()
    {
        var plan = MarkerPlan.From(
        [
            new AppliedMarker("BREAD", "bread", "🥐", "🍞", 10, Removable: true),
            Coffee with { Removable = false },
        ]);

        Assert.Contains(Marker.Restore("🥐"), plan.Removable);
        Assert.Contains(Marker.Restore("🍞"), plan.Removable);
        Assert.DoesNotContain(Marker.Restore("☕"), plan.Removable);
    }

    /// <summary>
    /// Every Marker the person has — standing, retired and abandoned — whatever the scope. What the
    /// block reader is given, and what the planner narrows its search with.
    /// </summary>
    [Fact]
    public void EveryMarker_HoldsAllOfThemWhateverTheScope()
    {
        var plan = MarkerPlan.From(
        [
            new AppliedMarker("BREAD", "bread", "🥐", "🍞", 10),
            new AppliedMarker(string.Empty, string.Empty, "🥖", null, int.MaxValue, Abandoned: true),
        ]);

        // The rule's own, the one it retired, and the one a deleted rule left behind.
        Assert.Equal(3, plan.EveryMarker.Count);
        Assert.Contains(Marker.Restore("🥐"), plan.EveryMarker);
        Assert.Contains(Marker.Restore("🍞"), plan.EveryMarker);
        Assert.Contains(Marker.Restore("🥖"), plan.EveryMarker);

        // And nothing is removable until a Remove says so, which is what keeps an Apply an Apply.
        Assert.Empty(plan.Removable);
    }

    /// <summary>
    /// An emoji nobody made a rule about was never in the block, so no Remove can reach it. The
    /// candidate definition of stale that reads as the certain one — "an emoji no rule of theirs has
    /// ever mentioned" — is the one this operation is structurally unable to act on, and
    /// deliberately so: it is text TodoWerk never wrote.
    /// </summary>
    [Fact]
    public void AnEmojiNobodyMadeARuleAbout_IsUntouchable()
    {
        var plan = MarkerPlan.From([Bread]);

        Assert.Null(plan.RemoveFor("🎉 Party"));
        Assert.Equal("🎉 Party", plan.RemoveFor("🍞 🎉 Party"));
    }

    /// <summary>Nothing stale in the block, so nothing to write.</summary>
    [Fact]
    public void ABlockThatIsAllWanted_RemovesToNothing() =>
        Assert.Null(MarkerPlan.From([Bread, Coffee]).RemoveFor("🍞☕ Frühstück #coffee #bread"));

    /// <summary>
    /// A title that is nothing but stale Markers empties altogether. Answered here and passed over
    /// by the caller: Microsoft To Do has no such task, so the plan names it as a skip rather than
    /// writing a title that would be refused or, worse, accepted.
    /// </summary>
    [Fact]
    public void ATitleThatIsNothingButStaleMarkers_EmptiesAltogether() =>
        Assert.Equal(string.Empty, MarkerPlan.From([Bread]).RemoveFor("🍞"));

    /// <summary>
    /// A Marker one rule retired and another has since taken as its own. On a task carrying the
    /// second rule's Hashtag it is that rule's Marker, plainly wanted — so it stays, however it got
    /// there. The stale count reads the same rules through the same code, so it says the same.
    /// </summary>
    [Fact]
    public void AMarkerAnotherRuleNowHolds_IsNotStaleWhereThatRulesHashtagIs()
    {
        var plan = MarkerPlan.From(
        [
            new AppliedMarker("BREAD", "bread", "🥐", "🍞", 10, Removable: true),
            new AppliedMarker("COFFEE", "coffee", "🍞", null, 20, Removable: true),
        ]);

        // Coffee's own Marker on coffee's task: wanted, and not bread's residue to take.
        Assert.Null(plan.RemoveFor("🍞 Kaffee #coffee"));
        Assert.Empty(plan.StaleIn("🍞 Kaffee #coffee"));

        // And on a task carrying neither, nothing asks for it and it goes.
        Assert.Equal("Etwas anderes", plan.RemoveFor("🍞 Etwas anderes"));
    }

    /// <summary>
    /// A Marker two rules retired is ambiguous, so no Apply will ever swap it — which is exactly
    /// why it is stale rather than merely out of date, and why <c>MarkerPlan.From</c> left it for
    /// this Change to settle.
    /// </summary>
    [Fact]
    public void AMarkerTwoRulesRetired_IsStaleBecauseNoApplyWillSwapIt()
    {
        var plan = MarkerPlan.From(
        [
            new AppliedMarker("BREAD", "bread", "🍞", "🥖", 10, Removable: true),
            new AppliedMarker("COFFEE", "coffee", "☕", "🥖", 20, Removable: true),
        ]);

        // An Apply leaves it where it is, ambiguous, for ever.
        Assert.Equal("🍞🥖 Brot #bread", plan.RewriteFor("🥖 Brot #bread"));

        // A Remove is the thing that was missing, and takes it.
        Assert.Equal("Brot #bread", plan.RemoveFor("🥖 Brot #bread"));
        Assert.Contains(Marker.Restore("🥖"), plan.StaleIn("🥖 Brot #bread"));
    }

    /// <summary>
    /// What the stale count counts and what a Remove takes are the same set, read from the same
    /// rules — which is the condition ADR-0014 put on shipping the count at all.
    /// </summary>
    [Fact]
    public void WhatIsCountedStale_IsWhatARemoveTakes()
    {
        var plan = MarkerPlan.From([Bread, Coffee]);

        const string Title = "🍞☕ Kaffee #coffee";

        Assert.Equal([Marker.Restore("🍞")], plan.StaleIn(Title));
        Assert.Equal("☕ Kaffee #coffee", plan.RemoveFor(Title));
    }

    /// <summary>
    /// An Apply carries no removable Markers, so asking it to remove takes nothing away. The two
    /// directions share this class and cannot be made to do each other's work by accident.
    /// </summary>
    [Fact]
    public void APlanWithNothingInScope_RemovesNothing() =>
        Assert.Null(MarkerPlan
            .From([Bread with { Removable = false }])
            .RemoveFor("🍞 Etwas anderes"));
}
