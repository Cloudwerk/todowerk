using TodoWerk.Domain.Hashtags;
using TodoWerk.Domain.Markers;
using Xunit;

namespace TodoWerk.UnitTests.Markers;

/// <summary>
/// What a rule remembers about the Marker its titles carry. The retired Marker is a claim about
/// the titles, not about the rule's history, and every method here is judged against that.
/// </summary>
public sealed class MarkerRuleTests
{
    [Fact]
    public void ChangingTheMarker_RetiresTheOneTitlesCarry()
    {
        var rule = Rule("🍞");

        rule.ChangeMarker(Marker("🥐"));

        Assert.Equal(Marker("🥐"), rule.Marker);
        Assert.Equal(Marker("🍞"), rule.RetiredMarker);
    }

    /// <summary>🥐 never reached a title, so it is 🍞 that still has to be swapped.</summary>
    [Fact]
    public void ChangingTwiceBeforeAnApply_KeepsTheFirstRetired()
    {
        var rule = Rule("🍞");

        rule.ChangeMarker(Marker("🥐"));
        rule.ChangeMarker(Marker("☕"));

        Assert.Equal(Marker("🍞"), rule.RetiredMarker);
    }

    [Fact]
    public void ChangingBack_CancelsTheSwap()
    {
        var rule = Rule("🍞");

        rule.ChangeMarker(Marker("🥐"));
        rule.ChangeMarker(Marker("🍞"));

        Assert.Null(rule.RetiredMarker);
    }

    [Fact]
    public void WritingTheRulesOwnMarker_LeavesNothingToRetire()
    {
        var rule = Rule("🍞");
        rule.ChangeMarker(Marker("🥐"));

        rule.Wrote(Marker("🥐"));

        Assert.Null(rule.RetiredMarker);
    }

    /// <summary>
    /// The rule was edited after the Apply was confirmed, so the run wrote a Marker the rule no
    /// longer has — and that is now what the titles carry.
    /// </summary>
    [Fact]
    public void WritingAMarkerTheRuleHasSinceLeft_RetiresIt()
    {
        var rule = Rule("🍞");
        rule.ChangeMarker(Marker("🥐"));
        rule.ChangeMarker(Marker("☕"));

        rule.Wrote(Marker("🥐"));

        Assert.Equal(Marker("☕"), rule.Marker);
        Assert.Equal(Marker("🥐"), rule.RetiredMarker);
    }

    /// <summary>An undo puts the old Marker back, and the rule has to remember it again.</summary>
    [Fact]
    public void WritingBackTheRetiredMarker_ReArmsIt()
    {
        var rule = Rule("🍞");
        rule.ChangeMarker(Marker("🥐"));
        rule.Wrote(Marker("🥐"));

        rule.Wrote(Marker("🍞"));

        Assert.Equal(Marker("🍞"), rule.RetiredMarker);
    }

    private static MarkerRule Rule(string marker) =>
        MarkerRule.Create("tenant", "user", "BREAD", "bread", Marker(marker), 10, DateTimeOffset.UnixEpoch);

    private static Marker Marker(string text)
    {
        Assert.True(Domain.Hashtags.Marker.TryCreate(text, out var marker));

        return marker;
    }
}
