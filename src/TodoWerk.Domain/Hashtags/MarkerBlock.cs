using System.Globalization;

namespace TodoWerk.Domain.Hashtags;

/// <summary>
/// The run of Markers at the very front of a title, and where the rest of the title starts
/// (ADR-0014). One block, at position zero, followed by a single space.
/// <para>
/// A block is defined against a particular person's Markers, not against emoji in general: a title
/// that opens with an emoji nobody made a rule for has no block, and that emoji is the first thing
/// in the rest of the title. Without that rule an Apply would pick up somebody's decorative
/// sparkle and start reordering it.
/// </para>
/// </summary>
/// <param name="Markers">In the order they appear, with duplicates as written.</param>
/// <param name="Start">
/// Index in the title where the block begins, or would begin: after any whitespace the title opens
/// with. A title pasted with a leading space still has its block at the front, and reading past
/// that space is what stops a rewrite putting a second block in front of the first — while the
/// space itself stays where it was typed, because it is not the block's to remove.
/// </param>
/// <param name="RestStart">
/// Index in the title where everything that is not the block begins. The block's own trailing
/// space is behind it: it belongs to the block, so that rewriting the block does not have to reason
/// about whether to add one. Equal to <paramref name="Start"/> when there is no block.
/// </param>
public readonly record struct MarkerBlock(IReadOnlyList<Marker> Markers, int Start, int RestStart)
{
    private static readonly Marker[] None = [];

    /// <summary>
    /// How much of a title's leading emoji run is worth keeping. A block is at most one Marker per
    /// rule and a person may hold fifty, but the run this bound applies to is every emoji at the
    /// front — including ones nobody made a rule about — so it is sized for a decorated title
    /// rather than for a block.
    /// <para>
    /// A run longer than this is cut, and a block read from the cut run is then shorter than one
    /// read from the title. That direction is the safe one: a Marker goes uncounted rather than
    /// counted where it is not, so a figure is low rather than wrong and nothing is offered for
    /// removal that is not there.
    /// </para>
    /// </summary>
    public const int MaxLeadingEmojiLength = 256;

    /// <summary>
    /// The run of emoji at the very front of <paramref name="title"/>, as it is written there —
    /// every grapheme from the front, after any leading whitespace, that could be somebody's
    /// Marker, and nothing else. Empty when the title does not open with one.
    /// <para>
    /// This is the half of a block that does not depend on whose rules are being asked about, and
    /// it exists so the index can store it. A block is the longest prefix of this run whose
    /// graphemes are Markers <em>this person</em> holds, which is a question about a rules table
    /// and cannot be stored beside a task — change one rule's emoji and every stored block would
    /// be wrong. Storing the run instead keeps the rule-dependent half where it belongs and takes
    /// the rest out of the read.
    /// </para>
    /// <para>
    /// The substring is copied out verbatim rather than rebuilt from the graphemes it recognised,
    /// which is what makes <see cref="Read"/> answer the same Markers over the run as it does over
    /// the whole title: the bytes it compares are the same bytes. Two presentations of one emoji
    /// are one Marker either way, because that is settled by <see cref="Marker"/> and not here.
    /// </para>
    /// <para>
    /// It knows what an emoji is and not what a Marker is, which is the distinction that lets the
    /// Indexing module compute it. ADR-0014 put the block grammar in this shared namespace so both
    /// modules may read it; this asks less of Indexing than that — no rule, no person, no table.
    /// </para>
    /// </summary>
    public static string LeadingEmojiOf(string? title)
    {
        if (string.IsNullOrEmpty(title))
        {
            return string.Empty;
        }

        var index = 0;

        // The same whitespace the block reader steps over, so the run starts where a block would.
        while (index < title.Length && char.IsWhiteSpace(title[index]))
        {
            index++;
        }

        var start = index;

        while (index < title.Length)
        {
            var length = StringInfo.GetNextTextElementLength(title.AsSpan(index));

            if (length <= 0 || index - start + length > MaxLeadingEmojiLength)
            {
                break;
            }

            // The same gate a rule passes on the way in, asked of a grapheme in a title: is this
            // one emoji, and one that could be written down as a Marker. Whether anybody did is
            // the question this deliberately does not ask.
            if (!Marker.TryCreate(title.Substring(index, length), out _))
            {
                break;
            }

            index += length;
        }

        return index == start ? string.Empty : title[start..index];
    }

    /// <summary>A title with nothing at the front that this person made a rule about.</summary>
    public static MarkerBlock Empty => new(None, 0, 0);

    /// <summary>True when the title opens with none of this person's Markers.</summary>
    public bool IsEmpty => Markers.Count == 0;

    /// <summary>
    /// Reads the leading block of <paramref name="title"/>: graphemes are taken from the front for
    /// as long as each one is a Marker in <paramref name="known"/>, and the first that is not ends
    /// the block.
    /// </summary>
    /// <param name="known">
    /// Every Marker this person's rules hold, retired ones included — a retired Marker sitting in
    /// a block is still part of it, and treating it as ordinary text would strand it in front of
    /// the block that replaced it.
    /// </param>
    public static MarkerBlock Read(string? title, IReadOnlySet<Marker> known)
    {
        ArgumentNullException.ThrowIfNull(known);

        if (string.IsNullOrEmpty(title) || known.Count == 0)
        {
            return Empty;
        }

        List<Marker>? markers = null;
        var index = 0;

        // Whitespace before the block is part of the front of the title, not of the rest of it.
        // Read past rather than read as "no block": a rewrite that saw " 🍞 Brot" as blockless
        // would put a second 🍞 in front of the first, and the next Apply a third.
        while (index < title.Length && char.IsWhiteSpace(title[index]))
        {
            index++;
        }

        var start = index;

        while (index < title.Length)
        {
            var length = StringInfo.GetNextTextElementLength(title.AsSpan(index));

            if (length <= 0)
            {
                break;
            }

            // Restored rather than validated: these came off a rule row that was validated when it
            // was created, and re-running the grammar on every grapheme of every title would make
            // reading a block cost more than writing one.
            var grapheme = Marker.Restore(title.Substring(index, length));

            if (!known.Contains(grapheme))
            {
                break;
            }

            markers ??= [];
            markers.Add(grapheme);
            index += length;
        }

        if (markers is null)
        {
            // Nothing this person made a rule about at the front: a block would begin where the
            // whitespace ends, and the rest of the title begins there too.
            return new MarkerBlock(None, start, start);
        }

        // The one whitespace character after the block is the block's, not the title's. Exactly
        // one: a title written with two keeps the second, because the rest of the title is nobody's
        // to tidy — and whichever character it is, a rewrite puts the same one back.
        if (index < title.Length && char.IsWhiteSpace(title[index]))
        {
            index++;
        }

        return new MarkerBlock(markers, start, index);
    }
}
