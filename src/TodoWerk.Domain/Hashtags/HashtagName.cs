namespace TodoWerk.Domain.Hashtags;

/// <summary>
/// What makes a Hashtag name acceptable to write down. One rule, and it is a round trip: prepend
/// the marker, run the result through the extractor, and require exactly one Hashtag back whose
/// Spelling is the name.
/// <para>
/// That is the only test that enforces ADR-0005's second compatibility property — every title a
/// write produces still highlights in the To Do clients. It inherits the extractor's conservatism,
/// so a name the clients would accept but the grammar does not is refused. Under-recognising is the
/// safe direction for the index; here it is merely restrictive, and ADR-0006 takes that trade
/// knowingly.
/// </para>
/// <para>
/// Beside the extractor rather than inside the Changes module, because two modules now ask it: a
/// Change validates the Spelling it is about to write, and a Marker Rule validates the Hashtag it
/// is about — and a rule about a name the extractor would not recognise is a rule that can never
/// match a task.
/// </para>
/// </summary>
public static class HashtagName
{
    /// <summary>Whether <paramref name="name"/> survives being written and read back.</summary>
    public static bool RoundTrips(string? name)
    {
        // By length first, before a single character is walked: the extractor would refuse a name
        // past the column's bound anyway, but only after reading all of it, and this is reached
        // from two request paths.
        if (string.IsNullOrEmpty(name) || name.Length > HashtagKey.MaxLength)
        {
            return false;
        }

        var extracted = HashtagExtractor.Extract(HashtagExtractor.Marker + name);

        return extracted.Count == 1 && string.Equals(extracted[0].Spelling, name, StringComparison.Ordinal);
    }
}
