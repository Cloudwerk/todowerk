using TodoWerk.Domain.Indexing;
using Xunit;
using TodoWerk.Domain.Hashtags;

namespace TodoWerk.UnitTests.Indexing;

/// <summary>
/// The identity rule from
/// <see href="https://github.com/Cloudwerk/todowerk/blob/main/docs/adr/0005-what-a-hashtag-is.md">ADR-0005</see>:
/// <c>Key = NFC(ToUpperInvariant(NFC(name)))</c>. The pairs below are the ADR's measured
/// equality table, asserted as the behaviour TodoWerk chose rather than as what any one
/// comparer happens to do — which is the whole reason the fold lives in C# and the column is
/// binary-collated.
/// </summary>
public sealed class HashtagKeyTests
{
    private const char CombiningDiaeresis = '̈';

    /// <summary>ADR-0005, row 1: casing never distinguishes a Hashtag.</summary>
    [Theory]
    [InlineData("Work", "WORK")]
    [InlineData("work", "WORK")]
    [InlineData("ProjectAlpha", "PROJECTALPHA")]
    public void Fold_IgnoresCasing(string spelling, string expectedKey) =>
        Assert.Equal(expectedKey, HashtagKey.Fold(spelling));

    /// <summary>
    /// Greek lower-case sigma has two forms, one used only at the end of a word. A reader sees
    /// one word; folding upwards agrees with them, and folding downwards would not.
    /// </summary>
    [Fact]
    public void Fold_TreatsBothFormsOfLowerCaseSigma_AsOneHashtag() =>
        Assert.Equal(HashtagKey.Fold("τέλος"), HashtagKey.Fold("τέλοσ"), StringComparer.Ordinal);

    /// <summary>
    /// ADR-0005, row 3: precomposed and decomposed "Prüfung" are one Hashtag. They are
    /// indistinguishable on screen, and Graph stores whichever byte sequence the client sent.
    /// </summary>
    [Fact]
    public void Fold_TreatsCanonicallyEquivalentSpellings_AsOneHashtag()
    {
        var precomposed = "Prüfung";
        var decomposed = "Pru" + CombiningDiaeresis + "fung";

        Assert.NotEqual(precomposed, decomposed, StringComparer.Ordinal);
        Assert.Equal(HashtagKey.Fold(precomposed), HashtagKey.Fold(decomposed), StringComparer.Ordinal);
    }

    /// <summary>
    /// ADR-0005, row 2: SQL's default collation calls "Straße" and "Strasse" equal; every .NET
    /// comparison calls them different, and TodoWerk follows .NET. Two Hashtags, and the binary
    /// collation on the key column is what stops SQL from overruling that.
    /// </summary>
    [Fact]
    public void Fold_KeepsEszettAndDoubleS_Apart() =>
        Assert.NotEqual(HashtagKey.Fold("Straße"), HashtagKey.Fold("Strasse"), StringComparer.Ordinal);

    /// <summary>ADR-0005, row 4: an umlaut is not its unaccented letter. Two Hashtags.</summary>
    [Fact]
    public void Fold_KeepsAccentedAndUnaccentedSpellings_Apart() =>
        Assert.NotEqual(HashtagKey.Fold("Prüfung"), HashtagKey.Fold("Prufung"), StringComparer.Ordinal);

    /// <summary>
    /// Turkish dotted capital I: the classic case-mapping trap. The property that matters is not
    /// which key comes out but that the key is normalised afterwards, so two spellings that fold
    /// to the same characters always produce the same bytes — the belt-and-braces outer pass
    /// ADR-0005 argues for.
    /// </summary>
    [Fact]
    public void Fold_NormalisesAfterCaseMapping()
    {
        var key = HashtagKey.Fold("İstanbul");

        Assert.Equal(key.Normalize(System.Text.NormalizationForm.FormC), key, StringComparer.Ordinal);
    }

    [Fact]
    public void Fold_IsIdempotent()
    {
        var once = HashtagKey.Fold("PrÜfung");

        Assert.Equal(once, HashtagKey.Fold(once), StringComparer.Ordinal);
    }

    /// <summary>
    /// The fold takes the name, not the written token: the marker is the extractor's business and
    /// never reaches the key. Passing one in would make a second Hashtag out of the same word.
    /// </summary>
    [Fact]
    public void Fold_FoldsTheNameWithoutTheMarker() =>
        Assert.NotEqual(HashtagKey.Fold("Work"), HashtagKey.Fold("#Work"), StringComparer.Ordinal);
}
