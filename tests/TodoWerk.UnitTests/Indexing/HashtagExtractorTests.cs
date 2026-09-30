using TodoWerk.Domain.Indexing;
using Xunit;
using TodoWerk.Domain.Hashtags;

namespace TodoWerk.UnitTests.Indexing;

/// <summary>
/// The grammar from
/// <see href="https://github.com/Cloudwerk/todowerk/blob/main/docs/adr/0005-what-a-hashtag-is.md">ADR-0005</see>,
/// case for case.
/// <para>
/// The titles follow the ones <c>scripts/Probe-HashtagGrammar.ps1</c> seeds into a test tenant,
/// and the probe ids are kept so the two can be compared line by line. Cases ADR-0005 marks
/// uncertain assert the conservative behaviour, and each one says which probe would change it.
/// </para>
/// </summary>
public sealed class HashtagExtractorTests
{
    private const char CombiningDiaeresis = '̈';

    [Fact]
    public void Extract_FindsATagAfterWhitespace() =>
        AssertSpellings("Fix the thing #work", "work");

    /// <summary>P20. The ADR admits start-of-title is unverified; recognising it is the choice.</summary>
    [Fact]
    public void Extract_FindsATagAtTheStartOfTheTitle() =>
        AssertSpellings("#anfangstag P20 Tag steht am Anfang", "anfangstag");

    /// <summary>P01. An ASCII-only class would truncate this to "Pr" and corrupt a German index.</summary>
    [Fact]
    public void Extract_KeepsNonAsciiLetters() =>
        AssertSpellings("P01 Umlaut #Prüfung", "Prüfung");

    /// <summary>P04 and P05: eszett and a Turkish dotted capital are ordinary name characters.</summary>
    [Theory]
    [InlineData("P04 Eszett #Straße", "Straße")]
    [InlineData("P05 Türkisch #İstanbul", "İstanbul")]
    public void Extract_KeepsTheRestOfTheLatinAndTurkishAlphabets(string title, string spelling) =>
        AssertSpellings(title, spelling);

    /// <summary>
    /// P02 and P03. Decomposed input keeps its combining mark inside the name — a mark is a name
    /// character — and the key folds the two spellings onto one Hashtag.
    /// </summary>
    [Fact]
    public void Extract_TreatsDecomposedAndPrecomposedTitles_AsOneHashtag()
    {
        var precomposed = Single("P02 NFC #Prüfung");
        var decomposed = Single("P03 NFD #Pru" + CombiningDiaeresis + "fung");

        Assert.NotEqual(precomposed.Spelling, decomposed.Spelling, StringComparer.Ordinal);
        Assert.Equal(precomposed.Key, decomposed.Key, StringComparer.Ordinal);
    }

    /// <summary>P06, P07, P09, P13, P21: punctuation and symbols end the name.</summary>
    [Theory]
    [InlineData("P06 Punkt am Ende #punktende.", "punktende")]
    [InlineData("P07 Komma #kommatag, danach mehr Text", "kommatag")]
    [InlineData("P13 Schraegstrich #kunde/contoso", "kunde")]
    [InlineData("P21 Emoji #emojitag✅", "emojitag")]
    [InlineData("Ende der Liste #letzter)", "letzter")]
    public void Extract_StopsAtTheFirstCharacterThatIsNotPartOfAName(string title, string spelling) =>
        AssertSpellings(title, spelling);

    /// <summary>P10. A colon ends the name; some people write "Prefix: Value" in titles.</summary>
    [Fact]
    public void Extract_TreatsAColonAsATerminator() =>
        AssertSpellings("P10 Doppelpunkt #Kunde:Contoso", "Kunde");

    /// <summary>P11 and P12: hyphen and underscore are inside the name, so a tag is not split.</summary>
    [Theory]
    [InlineData("P11 Bindestrich #kunde-contoso", "kunde-contoso")]
    [InlineData("P12 Unterstrich #kunde_contoso", "kunde_contoso")]
    public void Extract_KeepsHyphenAndUnderscoreInsideTheName(string title, string spelling) =>
        AssertSpellings(title, spelling);

    /// <summary>P14 and P15. Uncertain in the ADR; digits are name characters until the probe says otherwise.</summary>
    [Theory]
    [InlineData("P14 Nur Ziffern #2026", "2026")]
    [InlineData("P15 Ziffernstart #2026review", "2026review")]
    public void Extract_AcceptsDigits(string title, string spelling) =>
        AssertSpellings(title, spelling);

    /// <summary>
    /// P16 and P17. The rule that pays for itself: without a boundary before the marker, every
    /// task mentioning C# grows a tag TodoWerk would then offer to rename.
    /// </summary>
    [Theory]
    [InlineData("P16 Programmiersprache C# und mehr Text")]
    [InlineData("P17 Mittendrin foo#bar Ende")]
    public void Extract_IgnoresAMarkerInsideAWord(string title) =>
        Assert.Empty(HashtagExtractor.Extract(title));

    /// <summary>
    /// P18 and P19. A marker with no name behind it yields nothing — an empty-named row would be
    /// an inventory entry nobody can rename. In <c>##doppelt</c> the second marker is not
    /// preceded by whitespace either, so the whole token is skipped rather than half-read.
    /// </summary>
    [Theory]
    [InlineData("P18 Doppelte Raute ##doppelt")]
    [InlineData("P19 Raute allein # ohne Wort")]
    [InlineData("Nur eine Raute #")]
    public void Extract_IgnoresAMarkerWithoutAName(string title) =>
        Assert.Empty(HashtagExtractor.Extract(title));

    /// <summary>P08: a tag after a parenthetical, an ordinary shape that must just work.</summary>
    [Fact]
    public void Extract_FindsATagAfterAParenthesis() =>
        AssertSpellings("P08 Beschreibung (mit Klammer) #ProjectAlpha", "ProjectAlpha");

    /// <summary>
    /// P09. The ADR's boundary is whitespace or the start of the title, and an opening bracket is
    /// neither — so a bracketed tag is not recognised at all, which is a stricter answer than the
    /// probe question ("does the highlight stop before the `)`?") anticipates. Under-recognising
    /// leaves a tag unmanaged, which the ADR prefers to offering a rename over text the clients do
    /// not highlight. The probe result flips this test if the clients disagree.
    /// </summary>
    [Fact]
    public void Extract_DoesNotRecogniseAMarkerAfterAnOpeningBracket() =>
        Assert.Empty(HashtagExtractor.Extract("P09 Klammer um den Tag (#klammertag)"));

    /// <summary>
    /// P22 is about note bodies, which the extractor never sees — v1 indexes titles only, and
    /// this test pins that the scope is a decision rather than an omission.
    /// </summary>
    [Fact]
    public void Extract_ReadsTitlesOnly() =>
        Assert.Empty(HashtagExtractor.Extract("P22 Tag nur in der Notiz"));

    /// <summary>P23 and P24 in one title: two Spellings, one Hashtag.</summary>
    [Fact]
    public void Extract_KeepsBothSpellingsOfOneHashtag()
    {
        var extracted = HashtagExtractor.Extract("P23 #Kasus und P24 #kasus");

        Assert.Equal(2, extracted.Count);
        Assert.Single(extracted.Select(hashtag => hashtag.Key).Distinct(StringComparer.Ordinal));
        Assert.Equal(["Kasus", "kasus"], extracted.Select(hashtag => hashtag.Spelling).Order(StringComparer.Ordinal));
    }

    /// <summary>An Occurrence is one Spelling in one task, so the same spelling twice is one row.</summary>
    [Fact]
    public void Extract_ReturnsOneOccurrencePerDistinctSpelling() =>
        AssertSpellings("#work am Morgen und #work am Abend", "work");

    [Fact]
    public void Extract_FindsEveryTagInATitle()
    {
        var spellings = HashtagExtractor.Extract("Angebot #kunde-contoso #Q3 fertigstellen #dringend")
            .Select(hashtag => hashtag.Spelling);

        Assert.Equal(["Q3", "dringend", "kunde-contoso"], spellings.Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Ein Titel ganz ohne Tags")]
    public void Extract_ReturnsNothingWhenThereIsNothingToFind(string? title) =>
        Assert.Empty(HashtagExtractor.Extract(title));

    /// <summary>
    /// A tab or a newline is whitespace, so it opens a tag the same way a space does. Graph
    /// accepts both in a title.
    /// </summary>
    [Theory]
    [InlineData("Zeile\n#neuezeile")]
    [InlineData("Tabulator\t#tabtag")]
    public void Extract_TreatsAnyWhitespaceAsTheBoundary(string title) =>
        Assert.Single(HashtagExtractor.Extract(title));

    private static ExtractedHashtag Single(string title) => Assert.Single(HashtagExtractor.Extract(title));

    /// <summary>
    /// Not in the probe: no client renders a 255-character Hashtag, but the columns are 255 and
    /// a longer name must not reach them — one oversized value would fail the save of the whole
    /// page it rides in.
    /// </summary>
    [Fact]
    public void Extract_AcceptsANameExactlyAtTheStorageLimit() =>
        AssertSpellings($"Grenzfall #{new string('a', HashtagKey.MaxLength)}", new string('a', HashtagKey.MaxLength));

    [Fact]
    public void Extract_SkipsANameBeyondTheStorageLimit_AndKeepsTheRest() =>
        AssertSpellings($"Blob #{new string('a', HashtagKey.MaxLength + 1)} #ok", "ok");

    private static void AssertSpellings(string title, params string[] expected)
    {
        var spellings = HashtagExtractor.Extract(title).Select(hashtag => hashtag.Spelling);

        Assert.Equal(expected, spellings, StringComparer.Ordinal);
    }
}
