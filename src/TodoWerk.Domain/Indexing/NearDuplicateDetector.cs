namespace TodoWerk.Domain.Indexing;

/// <summary>
/// Finds Hashtags that look like typos of each other — <c>#kunde</c> against <c>#kuned</c> — so
/// the inventory can point them out.
/// <para>
/// Deliberately conservative, because the two mistakes are not symmetric: a missed pair is a tag
/// the user tidies up by hand later, while a false pair invites them to merge two Hashtags that
/// were never the same, and a merge rewrites their tasks. Only single-character differences count,
/// and only on names long enough for one character not to be most of the word.
/// </para>
/// </summary>
public static class NearDuplicateDetector
{
    /// <summary>
    /// Below this, one edit is too much of the name to mean anything: <c>#q1</c> and <c>#q2</c>
    /// differ by one character and are obviously two different quarters.
    /// </summary>
    public const int DefaultMinimumLength = 4;

    /// <summary>
    /// Every key that is within one character of another key. Keys are the folded form, so casing
    /// can never produce a pair here — <c>#Work</c> and <c>#work</c> are one key, and one Hashtag
    /// with two Spellings is the casing flag, not this one (ADR-0005).
    /// </summary>
    public static IReadOnlySet<string> Flag(
        IEnumerable<string> keys,
        int minimumLength = DefaultMinimumLength)
    {
        ArgumentNullException.ThrowIfNull(keys);

        var candidates = keys
            .Where(key => key.Length >= minimumLength)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var flagged = new HashSet<string>(StringComparer.Ordinal);

        if (candidates.Count < 2)
        {
            return flagged;
        }

        // Two names are one edit apart exactly when they share a spelling with one character
        // dropped. Indexing those instead of comparing every pair turns a quadratic sweep over
        // thousands of tags — which this is, on every page load — into one pass over their
        // characters. Adjacent transpositions fall out for free: dropping either of the swapped
        // characters leaves the same string, which is why "#kuned" finds "#kunde".
        var byVariant = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var key in candidates)
        {
            foreach (var variant in VariantsOf(key))
            {
                if (!byVariant.TryGetValue(variant, out var sharing))
                {
                    byVariant[variant] = [key];
                    continue;
                }

                foreach (var other in sharing)
                {
                    flagged.Add(key);
                    flagged.Add(other);
                }

                sharing.Add(key);
            }
        }

        return flagged;
    }

    /// <summary>
    /// The name itself, then the name with each single character dropped. The name is in there so
    /// a pair differing only in length — <c>#projekt</c> and <c>#projekte</c> — meets the longer
    /// one's deletions.
    /// </summary>
    private static IEnumerable<string> VariantsOf(string key)
    {
        yield return key;

        for (var index = 0; index < key.Length; index++)
        {
            // Dropping half a surrogate pair yields a variant that matches nothing, which is the
            // harmless outcome: an astral character simply never contributes a near-duplicate.
            yield return string.Concat(key.AsSpan(0, index), key.AsSpan(index + 1));
        }
    }
}
