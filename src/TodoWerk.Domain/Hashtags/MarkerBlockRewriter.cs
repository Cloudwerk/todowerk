using System.Text;

namespace TodoWerk.Domain.Hashtags;

/// <summary>
/// What an Apply Markers does to one title, as a pure function (ADR-0014): rewrite the whole block
/// at the front, and touch nothing behind it.
/// <para>
/// The whole block on every task, never an insert into an existing one. That is what makes a
/// one-rule Apply and an all-rules Apply write the same block for the same task, so the order
/// promise holds however the user got there — and it is the only version that can collapse a
/// duplicate somebody's own typing left behind.
/// </para>
/// <para>
/// It adds and reorders; it never removes. A Marker whose Hashtag has since left the task is kept,
/// after the ones that belong, because deleting a rule or a tag must not silently rewrite titles
/// the user never asked about. The one exception is a Marker the rule itself retired, which is
/// emitted as its replacement: that is the rule's Marker changing, not a Hashtag going away.
/// </para>
/// </summary>
public static class MarkerBlockRewriter
{
    /// <summary>
    /// The title this task should carry, or null when it already reads that way — which the write
    /// path turns into the same "already read the way the change asked for" skip the other Changes
    /// use.
    /// </summary>
    /// <param name="wanted">
    /// The Markers this task should carry, in the order of the user's rules. Empty is meaningful:
    /// the task holds none of the Hashtags in scope, so only what is already in its block survives.
    /// </param>
    /// <param name="known">
    /// Every Marker the user's rules hold. What decides where the block ends, so that an emoji
    /// nobody made a rule about is left as the first character of the title and not swept into a
    /// reorder.
    /// </param>
    /// <param name="retired">
    /// Markers a rule has replaced, mapped to what replaced them. Applied to the block, never to
    /// <paramref name="wanted"/>, which already carries each rule's current Marker.
    /// </param>
    public static string? Rewrite(
        string? title,
        IReadOnlyList<Marker> wanted,
        IReadOnlyCollection<Marker> known,
        IReadOnlyDictionary<Marker, Marker> retired)
    {
        ArgumentNullException.ThrowIfNull(wanted);
        ArgumentNullException.ThrowIfNull(known);
        ArgumentNullException.ThrowIfNull(retired);

        if (title is null)
        {
            return null;
        }

        // Retired Markers count as known for the read: one sitting at the front of a title is part
        // of the block that is about to replace it, not the first character of the rest.
        var recognised = new HashSet<Marker>(known);

        foreach (var pair in retired)
        {
            recognised.Add(pair.Key);
        }

        var block = MarkerBlock.Read(title, recognised);

        var emitted = new List<Marker>(wanted.Count + block.Markers.Count);
        var seen = new HashSet<Marker>();

        // What the rules ask for, in rule order, first. A duplicate collapses here.
        foreach (var marker in wanted)
        {
            if (seen.Add(marker))
            {
                emitted.Add(marker);
            }
        }

        // Then what the block already held and no rule now asks for, in the order it was written —
        // swapped where the rule that put it there has since changed its Marker.
        foreach (var marker in block.Markers)
        {
            var replacement = retired.TryGetValue(marker, out var replaced) ? replaced : marker;

            if (seen.Add(replacement))
            {
                emitted.Add(replacement);
            }
        }

        // An Apply emits nothing only when it wanted nothing and found nothing, which Write reads
        // as the title being none of its business.
        return Write(title, block, emitted);
    }

    /// <summary>
    /// What a Remove Markers does to one title: take Markers out of the block at the front, and do
    /// nothing else. The mirror of <see cref="Rewrite"/>, and the only thing in the product that
    /// takes a Marker away on purpose.
    /// <para>
    /// It removes and nothing else. It never adds a Marker a rule asks for and the block does not
    /// hold, never reorders what is left, and never swaps a retired Marker for its replacement —
    /// all three of those are an Apply, and a Remove that quietly did one of them would write a
    /// title nobody previewed as that. What is left comes out in the order it was written in.
    /// </para>
    /// <para>
    /// An emoji nobody made a rule about is not reachable from here at all: it was never in the
    /// block, so it is the first character of the rest of the title and is copied out verbatim.
    /// That is a property of <see cref="MarkerBlock"/> rather than a rule stated here, and it is
    /// what keeps this operation to text TodoWerk itself wrote.
    /// </para>
    /// </summary>
    /// <param name="wanted">
    /// The Markers a standing rule asks for on this task — this task's Hashtags, through this
    /// person's rules. What a Marker has to fail to be stale, so a task that still carries its
    /// Hashtag keeps its Marker whatever the scope says.
    /// </param>
    /// <param name="known">
    /// Every Marker the person's rules hold, retired and abandoned ones included. What decides
    /// where the block ends, exactly as for an Apply.
    /// </param>
    /// <param name="removable">
    /// The run's scope: the Markers this Remove may take away. A stale Marker outside it stays,
    /// which is what makes a one-rule Remove narrow — and what stops an all-rules Remove being the
    /// only shape on offer.
    /// </param>
    /// <returns>
    /// The title this task should carry, or null when nothing in its block is both stale and in
    /// scope — which the write path turns into the same "already read the way the change asked
    /// for" skip the other Changes use. May be empty, for a title that was nothing but a block;
    /// the caller passes that over rather than writing it.
    /// </returns>
    public static string? Remove(
        string? title,
        IReadOnlyList<Marker> wanted,
        IReadOnlyCollection<Marker> known,
        IReadOnlyCollection<Marker> removable)
    {
        ArgumentNullException.ThrowIfNull(wanted);
        ArgumentNullException.ThrowIfNull(known);
        ArgumentNullException.ThrowIfNull(removable);

        if (title is null)
        {
            return null;
        }

        var block = MarkerBlock.Read(title, new HashSet<Marker>(known));

        if (block.IsEmpty)
        {
            // Nothing at the front this person made a rule about, so there is nothing here a
            // Remove is entitled to touch — whatever the title opens with.
            return null;
        }

        var keep = new HashSet<Marker>(wanted);
        var scope = new HashSet<Marker>(removable);

        var emitted = new List<Marker>(block.Markers.Count);
        var seen = new HashSet<Marker>();

        // What the block already held, in the order it was written in, less the Markers that are
        // both stale and in scope. A duplicate collapses on the way through, which is the one
        // tidy-up removing shares with applying.
        foreach (var marker in block.Markers)
        {
            if (scope.Contains(marker) && !keep.Contains(marker))
            {
                continue;
            }

            if (seen.Add(marker))
            {
                emitted.Add(marker);
            }
        }

        return Write(title, block, emitted);
    }

    /// <summary>
    /// The block written back into the title: whatever the title opened with, then the Markers,
    /// then the one whitespace character that separates them from the rest.
    /// <para>
    /// Shared by both directions so that the separator is reasoned about once. It is the block's
    /// own — which is what lets a block that has emptied take its space with it, rather than
    /// leaving the rest of the title starting one character further in than the person typed it.
    /// </para>
    /// </summary>
    private static string? Write(string title, MarkerBlock block, List<Marker> emitted)
    {
        if (emitted.Count == 0 && block.IsEmpty)
        {
            // No block wanted and none there: nothing this function may touch, whatever the title
            // has at its front.
            return null;
        }

        var rest = title[block.RestStart..];
        var rewritten = new StringBuilder(title.Length + (emitted.Count * 4));

        // Whatever the title opened with before the block — whitespace, if anything — stays as it
        // was typed. The block goes where the block goes: after it.
        rewritten.Append(title, 0, block.Start);

        foreach (var marker in emitted)
        {
            rewritten.Append(marker.Text);
        }

        // One whitespace character between the block and the title — the one that was there, if
        // one was, because a tab somebody typed is not this function's to turn into a space. None
        // when the title is only a block — a trailing space nobody typed is a change to somebody's
        // task all the same — unless they did type one, in which case it stays for the same reason.
        // A Marker never ends in whitespace, so whitespace just before the rest is the separator.
        var typed = !block.IsEmpty && char.IsWhiteSpace(title[block.RestStart - 1]);

        // And none at all when the block has emptied: the separator is the block's, so it goes
        // when the block goes. Keeping it would move the rest of the title one character in from
        // where the person typed it, on every task a Remove touched.
        if (emitted.Count > 0 && (rest.Length > 0 || typed))
        {
            rewritten.Append(typed ? title[block.RestStart - 1] : ' ');
        }

        rewritten.Append(rest);

        var next = rewritten.ToString();

        return string.Equals(next, title, StringComparison.Ordinal) ? null : next;
    }
}
