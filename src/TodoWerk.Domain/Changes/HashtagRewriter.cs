using System.Text;
using TodoWerk.Domain.Hashtags;

namespace TodoWerk.Domain.Changes;

/// <summary>
/// The edit a Change performs on one title, as a pure function: replace each Occurrence of a
/// source Hashtag with the target Spelling, and touch nothing else (ADR-0006).
/// <para>
/// It reads the title through the same grammar the index does, so "what a Change rewrites" and
/// "what the inventory counted" cannot come apart. That inheritance is deliberate down to its
/// awkward consequences: <c>C#</c> is not a Hashtag to the index and is therefore not a Hashtag
/// to a rename either.
/// </para>
/// </summary>
public static class HashtagRewriter
{
    /// <summary>
    /// Rewrites <paramref name="title"/>, or returns null when it carries no Occurrence of any
    /// source Hashtag — which the write path reads as "skip this task", because the user asked
    /// for a tag to change and there is no longer a tag there to change.
    /// </summary>
    /// <param name="sourceKeys">Folded keys, per <see cref="HashtagKey"/>. Compared as bytes.</param>
    /// <param name="targetSpelling">The Spelling every matched Occurrence takes, without the marker.</param>
    public static string? Rewrite(string? title, IReadOnlyCollection<string> sourceKeys, string targetSpelling)
    {
        ArgumentNullException.ThrowIfNull(sourceKeys);
        ArgumentNullException.ThrowIfNull(targetSpelling);

        if (title is null || sourceKeys.Count == 0)
        {
            return null;
        }

        var spans = HashtagExtractor.Locate(title);

        if (spans.Count == 0)
        {
            return null;
        }

        StringBuilder? rewritten = null;
        var copied = 0;

        foreach (var span in spans)
        {
            if (!Matches(sourceKeys, span.Key))
            {
                continue;
            }

            rewritten ??= new StringBuilder(title.Length + targetSpelling.Length);

            // Everything between the last replacement and this one, verbatim — the spacing and
            // punctuation the user typed, which is not this function's to tidy.
            rewritten.Append(title, copied, span.Start - copied);
            rewritten.Append(HashtagExtractor.Marker).Append(targetSpelling);

            copied = span.Start + span.Length;
        }

        if (rewritten is null)
        {
            return null;
        }

        rewritten.Append(title, copied, title.Length - copied);

        return rewritten.ToString();
    }

    /// <summary>
    /// Ordinal, because the keys are already folded and the database compares the same bytes
    /// (ADR-0005). A collation-aware comparison here would make C# and SQL Server disagree about
    /// which tasks a Change covers.
    /// </summary>
    private static bool Matches(IReadOnlyCollection<string> sourceKeys, string key)
    {
        if (sourceKeys is IReadOnlySet<string> set)
        {
            return set.Contains(key);
        }

        foreach (var source in sourceKeys)
        {
            if (string.Equals(source, key, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
