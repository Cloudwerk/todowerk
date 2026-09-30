using TodoWerk.Domain.Changes;
using TodoWerk.Domain.Hashtags;
using Xunit;

namespace TodoWerk.UnitTests.Changes;

/// <summary>
/// The edit itself, which ADR-0006 makes as small as it can defensibly be: each Occurrence's span
/// is replaced and every other character — spacing, punctuation, the rest of the sentence — is
/// left exactly as it was. TodoWerk is a hashtag manager, not a title editor.
/// </summary>
public sealed class HashtagRewriterTests
{
    [Fact]
    public void ItReplacesTheHashtagAndNothingElse()
    {
        var rewritten = HashtagRewriter.Rewrite("Angebot  schreiben #Prio1 — bis Freitag!", [Key("Prio1")], "Priority1");

        Assert.Equal("Angebot  schreiben #Priority1 — bis Freitag!", rewritten);
    }

    [Fact]
    public void ItRewritesEveryOccurrenceInOneTitle()
    {
        var rewritten = HashtagRewriter.Rewrite("#Work planen und #work prüfen", [Key("Work")], "Work");

        Assert.Equal("#Work planen und #Work prüfen", rewritten);
    }

    /// <summary>
    /// The duplicate ADR-0006 accepts rather than smooths over. Collapsing it would mean deciding
    /// which one survives and what happens to the whitespace around it, and it would turn undo
    /// from a stored string into text surgery. The preview shows this, so nobody is surprised.
    /// </summary>
    [Fact]
    public void MergingOntoATagAlreadyInTheTitle_LeavesTheDuplicateStanding()
    {
        var rewritten = HashtagRewriter.Rewrite("#kunde and #customer", [Key("kunde")], "customer");

        Assert.Equal("#customer and #customer", rewritten);
    }

    [Fact]
    public void ItRewritesEverySourceOfAMerge()
    {
        var rewritten = HashtagRewriter.Rewrite("#Work #Works #privat", [Key("Work"), Key("Works")], "Works");

        Assert.Equal("#Works #Works #privat", rewritten);
    }

    /// <summary>
    /// The signal the write path turns into a skip: the user asked for a tag to change, and by the
    /// time the task was re-read there was no longer a tag there to change.
    /// </summary>
    [Fact]
    public void ATitleWithoutTheSource_RewritesToNothing()
    {
        Assert.Null(HashtagRewriter.Rewrite("Angebot schreiben #anderes", [Key("Prio1")], "Priority1"));
    }

    [Fact]
    public void ATitleThatIsAlreadyRight_RewritesToItself()
    {
        Assert.Equal("#Works planen", HashtagRewriter.Rewrite("#Works planen", [Key("works")], "Works"));
    }

    /// <summary>
    /// The grammar decides what a Hashtag is, and the rewrite inherits that decision whole:
    /// <c>C#</c> is not a Hashtag, so a Change targeting <c>#c</c> must not touch it.
    /// </summary>
    [Fact]
    public void TextTheGrammarDoesNotCallAHashtag_IsLeftAlone()
    {
        Assert.Null(HashtagRewriter.Rewrite("Sprachwahl: C# oder F#", [Key("c")], "CSharp"));
    }

    [Fact]
    public void ItMatchesOnTheFoldedKeyRatherThanOnSpelling()
    {
        var rewritten = HashtagRewriter.Rewrite("Prüfung #STRASSE heute", [Key("strasse")], "Strasse");

        Assert.Equal("Prüfung #Strasse heute", rewritten);
    }

    /// <summary>Offsets have to survive characters outside the basic plane; a surrogate pair is two units.</summary>
    [Fact]
    public void ItKeepsItsOffsetsAcrossSurrogatePairs()
    {
        var rewritten = HashtagRewriter.Rewrite("🎯 Ziel #alt 🎯 und #alt", [Key("alt")], "neu");

        Assert.Equal("🎯 Ziel #neu 🎯 und #neu", rewritten);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ATitleWithNothingInIt_RewritesToNothing(string? title) =>
        Assert.Null(HashtagRewriter.Rewrite(title, [Key("alt")], "neu"));

    [Fact]
    public void NoSources_RewritesToNothing() =>
        Assert.Null(HashtagRewriter.Rewrite("Angebot #kunde", [], "neu"));

    private static string Key(string spelling) => HashtagKey.Fold(spelling);
}
