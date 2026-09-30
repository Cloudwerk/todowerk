using TodoWerk.Domain.Changes;
using TodoWerk.Domain.Hashtags;
using Xunit;

namespace TodoWerk.UnitTests.Markers;

/// <summary>
/// The one place a Change's Markers become a rewrite, shared by the planner and the runner so the
/// block a preview shows and the block a run writes cannot come apart.
/// </summary>
public sealed class MarkerPlanTests
{
    private static readonly AppliedMarker Bread = new("BREAD", "bread", "🍞", null, 10);

    private static readonly AppliedMarker Coffee = new("COFFEE", "coffee", "☕", null, 20);

    [Fact]
    public void ItWritesEveryRuleWhoseHashtagTheTitleHolds_InRuleOrder()
    {
        // Handed over out of order, to prove the position decides and not the list.
        var plan = MarkerPlan.From([Coffee, Bread]);

        Assert.Equal("🍞☕ Frühstück #coffee #bread", plan.RewriteFor("Frühstück #coffee #bread"));
    }

    [Fact]
    public void ItLeavesATitleAlone_WhoseBlockIsAlreadyRight() =>
        Assert.Null(MarkerPlan.From([Bread, Coffee]).RewriteFor("🍞 Brot #bread"));

    [Fact]
    public void ARetiredMarker_IsSwappedForTheRulesOwn()
    {
        var plan = MarkerPlan.From([new AppliedMarker("BREAD", "bread", "🥐", "🍞", 10)]);

        Assert.Equal("🥐 Brot #bread", plan.RewriteFor("🍞 Brot #bread"));
    }

    /// <summary>
    /// A retired Marker that another rule now holds as its own is ambiguous in a block, and a swap
    /// that answered "both" would put a Marker on a task that does not carry its Hashtag. Left in
    /// place instead: applying never removes, and Remove Markers is where stale is settled.
    /// </summary>
    [Fact]
    public void ARetiredMarkerAnotherRuleNowHolds_IsNotSwapped()
    {
        var plan = MarkerPlan.From(
        [
            new AppliedMarker("BREAD", "bread", "🥐", "🍞", 10),
            new AppliedMarker("COFFEE", "coffee", "🍞", null, 20),
        ]);

        // Coffee's own Marker, on coffee's task: not bread's stale one, and not doubled.
        Assert.Null(plan.RewriteFor("🍞 Frühstück #coffee"));

        // On bread's task it is ambiguous, and stays — after the Marker bread now wants.
        Assert.Equal("🥐🍞 Brot #bread", plan.RewriteFor("🍞 Brot #bread"));
    }

    /// <summary>
    /// Two rules that retired the same Marker have an equal claim on it, and which of them put it
    /// on a given task cannot be read off the title. Swapping it for either would put a Marker on
    /// a task that may not carry that rule's Hashtag, so it stays.
    /// </summary>
    [Fact]
    public void AMarkerTwoRulesRetired_IsNotSwapped()
    {
        var plan = MarkerPlan.From(
        [
            new AppliedMarker("BREAD", "bread", "🍞", "🥖", 10),
            new AppliedMarker("COFFEE", "coffee", "☕", "🥖", 20),
        ]);

        Assert.Equal("🍞🥖 Brot #bread", plan.RewriteFor("🥖 Brot #bread"));
        Assert.Equal("🍞☕🥖 X #bread #coffee", plan.RewriteFor("🥖 X #bread #coffee"));
    }

    /// <summary>A Marker a deleted rule left behind is nobody's to swap, whoever else retired it.</summary>
    [Fact]
    public void AMarkerARuleRetiredAndAnotherAbandoned_IsNotSwapped()
    {
        var plan = MarkerPlan.From(
        [
            new AppliedMarker("BREAD", "bread", "🍞", "🥖", 10),
            new AppliedMarker(string.Empty, string.Empty, "🥖", null, int.MaxValue, Abandoned: true),
        ]);

        Assert.Null(plan.RewriteFor("🥖 Nur so"));
        Assert.Equal("🍞🥖 Brot #bread", plan.RewriteFor("🥖 Brot #bread"));
    }

    /// <summary>
    /// The documented case that reads like the ones above and is not one of them: a retired Marker
    /// with exactly one claim on it is swapped wherever it sits in a block, tag or no tag, because
    /// the swap is the rule's own Marker changing and not a Hashtag going away (ADR-0014).
    /// </summary>
    [Fact]
    public void ARetiredMarkerWithOneClaim_IsSwappedEvenWhereTheTagHasGone() =>
        Assert.Equal(
            "🍞 Nur so",
            MarkerPlan.From([new AppliedMarker("BREAD", "bread", "🍞", "☕", 10)]).RewriteFor("☕ Nur so"));

    /// <summary>
    /// A deleted rule's Marker is read as part of the block and nothing more: not wanted, not in
    /// scope, not swapped. It is kept the way any stale Marker is kept — behind the ones a rule
    /// asks for — and never becomes the first character of a second block.
    /// </summary>
    [Fact]
    public void AnAbandonedMarker_IsRecognisedAndNeverWritten()
    {
        var plan = MarkerPlan.From(
        [
            Coffee,
            new AppliedMarker(string.Empty, string.Empty, "🍞", null, int.MaxValue, Abandoned: true),
        ]);

        Assert.Null(plan.RewriteFor("☕🍞 Frühstück #coffee"));
        Assert.Equal("☕🍞 Frühstück #coffee", plan.RewriteFor("🍞☕ Frühstück #coffee"));
        Assert.Equal("☕🍞 Frühstück #coffee", plan.RewriteFor("🍞 Frühstück #coffee"));
        Assert.Equal("☕ Kaffee #coffee", plan.RewriteFor("Kaffee #coffee"));
    }

    /// <summary>
    /// The keys are read through the extractor's own grammar, so what the plan thinks a title
    /// carries is exactly what the index counted.
    /// </summary>
    [Fact]
    public void TheHashtagsOfATitle_AreTheExtractors()
    {
        var keys = MarkerPlan.HashtagKeysIn("🍞 Brot #Bread und C# #coffee");

        Assert.Equal(["BREAD", "COFFEE"], keys.Order(StringComparer.Ordinal));
        Assert.DoesNotContain(HashtagKey.Fold("C"), keys);
    }

    [Fact]
    public void AnAnsweredKeySet_GivesTheSameRewrite()
    {
        var plan = MarkerPlan.From([Bread, Coffee]);
        const string title = "Kaffee #coffee";

        Assert.Equal(plan.RewriteFor(title), plan.RewriteFor(title, MarkerPlan.HashtagKeysIn(title)));
    }
}
