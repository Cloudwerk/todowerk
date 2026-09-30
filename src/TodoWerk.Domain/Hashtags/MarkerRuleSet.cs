namespace TodoWerk.Domain.Hashtags;

/// <summary>
/// One person's Marker Rules as the block grammar needs to see them: which Marker each Hashtag
/// carries, which Markers are recognised at all, and — for one task at a time — which of them
/// anything still asks for.
/// <para>
/// Here beside the block rather than in either module, because three things ask and they must not
/// be able to disagree. The Changes module plans and runs a write from this; the Markers module
/// counts how much of somebody's marking is stale from it. A count that said "2 stale" where the
/// Change would take one is the failure ADR-0014 was guarding against when it made the count ship
/// with the action — so the count and the action read the same code over the same rules.
/// </para>
/// </summary>
public sealed class MarkerRuleSet
{
    private readonly IReadOnlyList<(string Key, Marker Marker)> _rules;

    private readonly IReadOnlySet<Marker> _known;

    private readonly IReadOnlyDictionary<Marker, Marker> _retired;

    private MarkerRuleSet(
        IReadOnlyList<(string Key, Marker Marker)> rules,
        IReadOnlySet<Marker> known,
        IReadOnlyDictionary<Marker, Marker> retired)
    {
        _rules = rules;
        _known = known;
        _retired = retired;
    }

    /// <summary>The rules that stand, in the order the block is written in.</summary>
    public IReadOnlyList<(string Key, Marker Marker)> Rules => _rules;

    /// <summary>
    /// Every Marker this person has: standing, retired, and left behind by a deleted rule. What
    /// decides where a block ends — so an emoji nobody made a rule about is the first character of
    /// the rest of the title and is never read as part of a block, nor written, nor removed.
    /// </summary>
    public IReadOnlySet<Marker> Known => _known;

    /// <summary>
    /// Markers a rule has replaced, mapped to what replaced them — only where exactly one rule has
    /// a claim. One that two rules retired, one that a rule now holds as its own, and one a deleted
    /// rule left behind are each ambiguous in a block, and a swap that picked one would put a
    /// Marker on a task that does not carry its Hashtag.
    /// </summary>
    public IReadOnlyDictionary<Marker, Marker> Retired => _retired;

    /// <summary>Reads a person's rules into the three things the block grammar needs.</summary>
    public static MarkerRuleSet From(IEnumerable<MarkerRuleEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var ordered = entries.OrderBy(entry => entry.Position).ToList();
        var rules = new List<(string Key, Marker Marker)>(ordered.Count);
        var known = new HashSet<Marker>();
        var abandoned = new HashSet<Marker>();
        var claims = new Dictionary<Marker, List<Marker>>();

        foreach (var entry in ordered)
        {
            known.Add(entry.Marker);

            // A deleted rule's Marker is recognised and nothing more: not wanted, not swapped.
            if (entry.Abandoned)
            {
                abandoned.Add(entry.Marker);
                continue;
            }

            rules.Add((entry.Key, entry.Marker));

            if (entry.Retired is { } previous)
            {
                // Known whether or not it is ever swapped: a retired Marker sitting in a block is
                // part of the block, and a reader that stopped at it would put a second block in
                // front of the first.
                known.Add(previous);

                if (!claims.TryGetValue(previous, out var claimants))
                {
                    claims[previous] = claimants = [];
                }

                claimants.Add(entry.Marker);
            }
        }

        var current = new HashSet<Marker>(rules.Select(rule => rule.Marker));
        var retired = new Dictionary<Marker, Marker>();

        foreach (var (previous, claimants) in claims)
        {
            if (claimants.Count == 1 && !current.Contains(previous) && !abandoned.Contains(previous))
            {
                retired[previous] = claimants[0];
            }
        }

        return new MarkerRuleSet(rules, known, retired);
    }

    /// <summary>
    /// The Markers anything still asks for on this task: each standing rule's own Marker where the
    /// task carries its Hashtag, and — while that Hashtag is there — the Marker that rule retired,
    /// because the next Apply swaps that one rather than leaving it.
    /// <para>
    /// The one question that decides both directions. A Marker in a block that is in here is
    /// wanted; one that is not is stale. Asked of the task's own Hashtags, so "the tag has gone" is
    /// answered from the title rather than from a table that may have moved on.
    /// </para>
    /// </summary>
    /// <param name="hashtagKeys">The folded Hashtag keys this task carries.</param>
    public IReadOnlyList<Marker> WantedIn(IReadOnlySet<string> hashtagKeys)
    {
        ArgumentNullException.ThrowIfNull(hashtagKeys);

        var wanted = new List<Marker>(_rules.Count);

        foreach (var (key, marker) in _rules)
        {
            if (!hashtagKeys.Contains(key))
            {
                continue;
            }

            wanted.Add(marker);

            // Whatever this rule's Marker used to be, where the swap has not reached yet. Only a
            // Marker exactly one rule has a claim on: an ambiguous one is never swapped by an
            // Apply, so nothing is waiting to put it right and it is stale like any other.
            foreach (var (previous, replacement) in _retired)
            {
                if (replacement == marker)
                {
                    wanted.Add(previous);
                }
            }
        }

        return wanted;
    }

    /// <summary>
    /// The Markers in this title's block that nothing asks for on this task — what a Remove Markers
    /// would be entitled to take, before any scope narrows it, and what the stale count counts.
    /// <para>
    /// Only Markers: an emoji outside every rule of this person's was never in the block, so it
    /// cannot appear here however it is written.
    /// </para>
    /// </summary>
    public IReadOnlySet<Marker> StaleIn(string? title, IReadOnlySet<string> hashtagKeys)
    {
        var block = MarkerBlock.Read(title, _known);

        if (block.IsEmpty)
        {
            return EmptyMarkers;
        }

        var wanted = new HashSet<Marker>(WantedIn(hashtagKeys));
        var stale = new HashSet<Marker>();

        foreach (var marker in block.Markers)
        {
            if (!wanted.Contains(marker))
            {
                stale.Add(marker);
            }
        }

        return stale;
    }

    /// <summary>The Markers of this title's block, whether or not anything asks for them.</summary>
    public IReadOnlyList<Marker> BlockOf(string? title) => MarkerBlock.Read(title, _known).Markers;

    private static readonly IReadOnlySet<Marker> EmptyMarkers = new HashSet<Marker>();
}

/// <summary>
/// One Marker Rule, in the only terms the block grammar has an opinion about. Neither module's own
/// shape: the Changes module reads these off the Markers a Change carries, and the Markers module
/// off the rows it stores, and both arrive here.
/// </summary>
/// <param name="Key">The folded Hashtag key the rule is about. Empty for an abandoned Marker.</param>
/// <param name="Marker">The emoji the rule carries now.</param>
/// <param name="Retired">The emoji it carried before, still out there in blocks, or null.</param>
/// <param name="Position">Where the rule sits in the person's list, which is the order of the block.</param>
/// <param name="Abandoned">
/// A Marker of a rule the person deleted. Recognised so the block is read whole, and nothing else:
/// never wanted, never swapped, and stale wherever it is.
/// </param>
public readonly record struct MarkerRuleEntry(
    string Key,
    Marker Marker,
    Marker? Retired,
    int Position,
    bool Abandoned = false);
