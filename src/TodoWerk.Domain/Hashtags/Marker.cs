using System.Buffers;
using System.Globalization;
using System.Text;

namespace TodoWerk.Domain.Hashtags;

/// <summary>
/// One emoji as a reader sees it — the thing a Marker Rule puts at the front of a title
/// (ADR-0014). Exactly one extended grapheme cluster, so a skin-toned hand, a family, a flag and
/// a keycap are each one Marker and nothing else is.
/// <para>
/// Beside the extractor rather than inside the Markers module, for the reason the extractor is
/// here: two modules read the same titles — Markers writes the block, Changes rewrites it — and a
/// second opinion about what one Marker is would put two different blocks on the same task.
/// </para>
/// <para>
/// Held as NFC text, and compared as a reader compares: two Markers are the same when they differ
/// only by an emoji presentation selector. <c>✉</c> and <c>✉️</c> are U+2709 with and without
/// U+FE0F, render as one glyph in most places, and must not be two rules or two Markers in one
/// block — and a rule written one way has to recognise a title somebody typed the other way. The
/// text is kept as it was given, so what is written into a title is what the person chose.
/// </para>
/// <para>
/// The database column compares bytes under its binary collation, so the uniqueness index alone
/// would let both spellings in; the store's own check, made with this equality, is what refuses
/// the second, and a race between two tabs is the only way past it.
/// </para>
/// </summary>
public readonly struct Marker : IEquatable<Marker>
{
    /// <summary>
    /// Longest sequence a Marker can be, in UTF-16 units, and the size of the column that holds
    /// one. The longest thing that is still one emoji is a tag sequence — the England flag is
    /// fourteen units — and a ZWJ family of four is eleven. Bounded rather than open so that a
    /// pasted paragraph is refused by length before anything walks it.
    /// </summary>
    public const int MaxLength = 32;

    /// <summary>Combines the scalars of a keycap: <c>1</c>, an optional VS16, then this.</summary>
    private const char CombiningEnclosingKeycap = '⃣';

    private const char VariationSelector16 = '️';

    private const char ZeroWidthJoiner = '\u200D';

    /// <summary>The two selectors that ask for emoji or text presentation without changing the emoji.</summary>
    private const string EmojiPresentation = "\uFE0F";

    private const string TextPresentation = "\uFE0E";

    private readonly string? _text;

    /// <summary>What equality and hashing look at: the text with presentation selectors removed.</summary>
    private readonly string? _identity;

    private Marker(string text)
    {
        _text = text;
        _identity = text
            .Replace(EmojiPresentation, string.Empty, StringComparison.Ordinal)
            .Replace(TextPresentation, string.Empty, StringComparison.Ordinal);
    }

    /// <summary>The emoji itself, in NFC. Empty for a default-constructed value, which is not a Marker.</summary>
    public string Text => _text ?? string.Empty;

    /// <summary>True for <c>default(Marker)</c>, which no factory here produces.</summary>
    public bool IsEmpty => string.IsNullOrEmpty(_text);

    /// <summary>
    /// Reads one Marker out of <paramref name="candidate"/>, or refuses it. Everything that is not
    /// exactly one emoji grapheme is refused: two emoji, a letter, a punctuation mark, an empty
    /// string, a lone surrogate. The wording a UI shows lives a layer up, because the Domain has no
    /// business holding sentences.
    /// </summary>
    public static bool TryCreate(string? candidate, out Marker marker)
    {
        marker = default;

        if (string.IsNullOrEmpty(candidate) || candidate.Length > MaxLength)
        {
            return false;
        }

        string text;

        try
        {
            // NFC on the way in, so a decomposed form and a composed one are not two Markers. It
            // throws on an unpaired surrogate, which is exactly the input that has no emoji in it.
            text = candidate.Normalize(NormalizationForm.FormC);
        }
        catch (ArgumentException)
        {
            return false;
        }

        if (text.Length is 0 || text.Length > MaxLength)
        {
            return false;
        }

        // One cluster and no more: "🍞🥐" is two Markers and "🍞x" is a Marker with a letter stuck
        // to it, and neither is a value this type may hold.
        if (StringInfo.GetNextTextElementLength(text) != text.Length)
        {
            return false;
        }

        if (!IsEmojiCluster(text))
        {
            return false;
        }

        // Nor may it end in a zero-width joiner. A block is Markers written back to back, and a
        // trailing joiner would fuse this one with the next into a single cluster that matches no
        // rule — so every Apply would read the block as text and write it again in front of itself.
        if (text[^1] == ZeroWidthJoiner)
        {
            return false;
        }

        // And nothing the Hashtag grammar would read as a tag. The hash keycap opens on the marker
        // character and its two combining marks are name runes, so a block of it at position zero
        // would be indexed as a Hashtag spelled U+FE0F U+20E3 the moment it was written — the one
        // thing ADR-0014 says a block can never become. The round-trip test a Spelling has to pass,
        // in the other direction: a Marker must survive being written and read back as no tag.
        if (HashtagExtractor.Locate(text).Count > 0)
        {
            return false;
        }

        marker = new Marker(text);

        return true;
    }

    /// <summary>
    /// The first Unicode scalar of <paramref name="text"/>, which every spelling of one emoji
    /// shares.
    /// <para>
    /// What a caller looks for in a title when it has to find Markers with a substring match. Two
    /// presentations of one emoji are one Marker here and two byte sequences in somebody's title —
    /// <c>U+2709</c> and <c>U+2709 U+FE0F</c> — so a search for the Marker's own text would miss
    /// every title typed in the other, while the opening scalar is in both and still narrows hard.
    /// Whether a match is really a Marker is decided afterwards, by the block grammar.
    /// </para>
    /// <para>
    /// Here rather than in either caller because two ask — the coverage figure and the planner
    /// behind a Remove Markers — and a narrowing that found fewer titles than the count was drawn
    /// from would offer somebody a Change that then reported nothing to do.
    /// </para>
    /// </summary>
    public static string FirstScalarOf(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return System.Text.Rune.DecodeFromUtf16(text, out var first, out _) == System.Buffers.OperationStatus.Done
            ? first.ToString()
            : text;
    }

    /// <summary>
    /// The value a store already holds, taken at its word. Materialising a row must not fail
    /// because a Unicode table moved under a Marker that was valid when it was written; the gate
    /// is <see cref="TryCreate"/>, on the way in.
    /// </summary>
    public static Marker Restore(string text)
    {
        ArgumentException.ThrowIfNullOrEmpty(text);

        return new Marker(text);
    }

    public override string ToString() => Text;

    public bool Equals(Marker other) => string.Equals(_identity, other._identity, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is Marker other && Equals(other);

    public override int GetHashCode() => _identity is null ? 0 : StringComparer.Ordinal.GetHashCode(_identity);

    public static bool operator ==(Marker left, Marker right) => left.Equals(right);

    public static bool operator !=(Marker left, Marker right) => !left.Equals(right);

    /// <summary>
    /// Whether one grapheme cluster reads as an emoji. Three shapes, because the Unicode property
    /// alone does not cover two of them: a flag is a pair of regional indicators, and a keycap
    /// opens on a digit, <c>#</c> or <c>*</c> — none of which are Extended_Pictographic.
    /// </summary>
    private static bool IsEmojiCluster(string cluster)
    {
        if (Rune.DecodeFromUtf16(cluster, out var first, out var consumed) != OperationStatus.Done)
        {
            return false;
        }

        if (EmojiRanges.IsExtendedPictographic(first))
        {
            return true;
        }

        if (EmojiRanges.IsRegionalIndicator(first))
        {
            return IsFlag(cluster, consumed);
        }

        return IsKeycap(cluster, first, consumed);
    }

    /// <summary>Two regional indicators and nothing else. One on its own is a letter in a box.</summary>
    private static bool IsFlag(string cluster, int consumed)
    {
        return Rune.DecodeFromUtf16(cluster.AsSpan(consumed), out var second, out var secondConsumed) == OperationStatus.Done
            && EmojiRanges.IsRegionalIndicator(second)
            && consumed + secondConsumed == cluster.Length;
    }

    /// <summary>A digit, <c>#</c> or <c>*</c>, an optional VS16, and the enclosing keycap.</summary>
    private static bool IsKeycap(string cluster, Rune first, int consumed)
    {
        if (first.Value is not ((>= '0' and <= '9') or '#' or '*'))
        {
            return false;
        }

        var rest = cluster.AsSpan(consumed);

        if (rest.Length > 0 && rest[0] == VariationSelector16)
        {
            rest = rest[1..];
        }

        return rest.Length == 1 && rest[0] == CombiningEnclosingKeycap;
    }
}
