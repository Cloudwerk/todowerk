using System.Buffers;
using System.Globalization;
using System.Text;

namespace TodoWerk.Domain.Hashtags;

/// <summary>
/// Reads Hashtags out of a Microsoft To Do task title, following the grammar ADR-0005 settled.
/// Pure: no Graph types, no EF types, no clock, no culture.
/// <para>
/// Microsoft specifies nothing here — the hashtag is a client-side rendering affordance over an
/// opaque title string — so the grammar is deliberately conservative. Under-recognising leaves a
/// tag unmanaged; over-recognising offers to rewrite text the user never saw as a tag, which is
/// the failure that loses trust.
/// </para>
/// <para>
/// Outside any vertical module, because two of them read the same titles and must not answer
/// differently: the index counts what this finds, and a Change rewrites exactly what this finds.
/// A second copy of the grammar would be a second opinion about what a Hashtag is.
/// </para>
/// </summary>
public static class HashtagExtractor
{
    /// <summary>What starts a Hashtag. Not a name character itself, which is why <c>##x</c> yields nothing.</summary>
    public const char Marker = '#';

    /// <summary>
    /// Every Hashtag written in <paramref name="title"/>, in order and with its position — one
    /// entry per appearance, so a Spelling written twice appears twice. This is the walk; the
    /// distinct view below is a projection of it.
    /// </summary>
    public static IReadOnlyList<HashtagSpan> Locate(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return [];
        }

        List<HashtagSpan>? found = null;

        for (var index = 0; index < title.Length; index++)
        {
            if (title[index] != Marker || !IsBoundary(title, index))
            {
                continue;
            }

            var nameLength = NameLengthAt(title, index + 1);

            if (nameLength == 0)
            {
                continue;
            }

            var spelling = title.Substring(index + 1, nameLength);

            // A name longer than the columns can hold is not a Hashtag anyone manages — it is a
            // pasted blob. Skipped rather than truncated, because truncation would silently merge
            // distinct blobs into one invented Hashtag; and skipped before anything is stored,
            // because one oversized value would otherwise fail the save of the whole page it
            // rides in. The folded key is checked too: normalisation may change the length.
            if (spelling.Length <= HashtagKey.MaxLength)
            {
                var key = HashtagKey.Fold(spelling);

                if (key.Length <= HashtagKey.MaxLength)
                {
                    found ??= [];
                    found.Add(new HashtagSpan(index, nameLength + 1, key, spelling));
                }
            }

            // Resume after the name: a marker inside it could not open a tag anyway, and the
            // characters are already accounted for.
            index += nameLength;
        }

        return found is null ? [] : found;
    }

    /// <summary>
    /// Every distinct Hashtag written in <paramref name="title"/>, in the order it first appears.
    /// One Occurrence is one Spelling in one task, so a spelling repeated in the same title
    /// appears once, while two casings of one Hashtag appear as two entries sharing a key.
    /// </summary>
    public static IReadOnlyList<ExtractedHashtag> Extract(string? title)
    {
        var spans = Locate(title);

        if (spans.Count == 0)
        {
            return [];
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var found = new List<ExtractedHashtag>(spans.Count);

        foreach (var span in spans)
        {
            if (seen.Add(span.Spelling))
            {
                found.Add(new ExtractedHashtag(span.Key, span.Spelling));
            }
        }

        return found;
    }

    /// <summary>
    /// A Hashtag opens at the start of the title or after whitespace, never mid-word. This one
    /// rule is what keeps <c>C#</c> and <c>foo#bar</c> out of the index; it also means a marker
    /// after a bracket or a quote is not recognised, which ADR-0005 records as unverified against
    /// the clients and deliberately answers on the conservative side.
    /// </summary>
    private static bool IsBoundary(string title, int markerIndex) =>
        markerIndex == 0 || char.IsWhiteSpace(title[markerIndex - 1]);

    /// <summary>Length in UTF-16 units of the name starting at <paramref name="start"/>; zero if there is none.</summary>
    private static int NameLengthAt(string title, int start)
    {
        var index = start;

        while (index < title.Length)
        {
            if (Rune.DecodeFromUtf16(title.AsSpan(index), out var rune, out var consumed) != OperationStatus.Done
                || !IsNameRune(rune))
            {
                break;
            }

            index += consumed;
        }

        return index - start;
    }

    /// <summary>
    /// Letters, marks, digits, <c>_</c> and <c>-</c>. Not an ASCII class: that truncates
    /// <c>#Prüfung</c> to <c>#Pr</c> and silently corrupts the index of any German-speaking
    /// tenant. Marks are in because a decomposed <c>ü</c>
    /// arrives as a letter followed by a combining mark, and splitting there would produce a
    /// Hashtag whose spelling is missing its accent.
    /// </summary>
    private static bool IsNameRune(Rune rune) =>
        Rune.IsLetter(rune)
        || Rune.IsDigit(rune)
        || rune.Value is '_' or '-'
        || Rune.GetUnicodeCategory(rune)
            is UnicodeCategory.NonSpacingMark
            or UnicodeCategory.SpacingCombiningMark
            or UnicodeCategory.EnclosingMark;
}
