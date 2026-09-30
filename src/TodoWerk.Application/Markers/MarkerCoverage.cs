using TodoWerk.Application.Abstractions.Authentication;
using TodoWerk.Application.Abstractions.Indexing;
using TodoWerk.Application.Abstractions.Markers;
using TodoWerk.Domain.Hashtags;

namespace TodoWerk.Application.Markers;

/// <summary>
/// How far each rule has been applied — "n of m tagged tasks carry 🍞" — and how much of it
/// is stale: Markers sitting at the front of tasks no rule asks them of any more.
/// <para>
/// Computed here rather than in SQL, because what counts as marked is the block grammar and the
/// database has no way to read one. What the index is asked for is a count and a narrowed set of
/// titles; what those titles mean is settled on this side of the boundary.
/// </para>
/// <para>
/// The marked figure is deliberately about the block and not about the title: an emoji somebody
/// typed in the middle of a sentence is text, and counting it would tell somebody a rule had been
/// applied to a task it had never touched. The stale figure is about the same block, read the same
/// way, so that the count and the Remove Markers it sits beside can never disagree about what is
/// there — the count exists because the action does, which is the condition ADR-0014 put on it.
/// </para>
/// </summary>
internal sealed class MarkerCoverage(ITaggedTitleReader titles)
{
    /// <param name="abandoned">
    /// The Markers of rules the person has deleted, which are still in the blocks they were applied
    /// to and have to be read as part of them. Every one of them is stale everywhere by definition:
    /// no standing rule asks for it anywhere.
    /// </param>
    public async Task<MarkerMeasurements> MeasureAsync(
        IndexUser user,
        IReadOnlyList<MarkerRuleRecord> rules,
        IReadOnlyList<AbandonedMarkerSnapshot> abandoned,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(abandoned);

        if (rules.Count == 0 && abandoned.Count == 0)
        {
            return MarkerMeasurements.None;
        }

        var keys = rules.Select(rule => rule.Key).Distinct(StringComparer.Ordinal).ToList();
        var tagged = keys.Count == 0
            ? new Dictionary<string, int>(StringComparer.Ordinal)
            : await titles.CountTasksPerKeyAsync(user, keys, cancellationToken);

        // The person's rules as the block grammar sees them — the same reading a Remove Markers is
        // planned and run from, so that "2 stale" and the Change standing next to it cannot mean
        // two different things. ADR-0014 held this count back until there was an action beside it;
        // sharing the derivation is what keeps the two honest about each other.
        var known = MarkerRuleSet.From(
        [
            .. rules.Select(rule => new MarkerRuleEntry(
                rule.Key,
                Marker.Restore(rule.Marker),
                rule.RetiredMarker is { } retired ? Marker.Restore(retired) : null,
                rule.Position)),
            .. abandoned.Select(marker => new MarkerRuleEntry(
                marker.Key,
                marker.Marker,
                null,
                int.MaxValue,
                Abandoned: true)),
        ]);

        // One read for every figure at once, and it asks for nothing but "which of my tasks open
        // with an emoji". No needles: the index stores the run itself, so nothing hands over one
        // string per Marker the person has ever used. Whether a run is a block, and whether
        // anything still asks for what is in it, are both decided here.
        var candidates = await titles.ReadMarkedTasksAsync(user, cancellationToken);

        var marked = new Dictionary<string, int>(StringComparer.Ordinal);
        var stale = new Dictionary<string, int>(StringComparer.Ordinal);
        var retired = new Dictionary<string, int>(StringComparer.Ordinal);
        var staleAbandoned = new Dictionary<Marker, int>();

        foreach (var candidate in candidates)
        {
            var tags = new HashSet<string>(candidate.Keys, StringComparer.Ordinal);

            // The block, read out of the stored run rather than out of the title. The run is copied
            // from the front of the title verbatim, so the grammar compares the same bytes and
            // answers the same Markers either way — which is the property the stored run rests
            // on, and the one LeadingEmojiTests holds.
            var block = known.BlockOf(candidate.LeadingEmoji);

            if (block.Count == 0)
            {
                continue;
            }

            var carries = new HashSet<Marker>(block);
            var staleHere = known.StaleIn(candidate.LeadingEmoji, tags);

            foreach (var rule in rules)
            {
                var own = Marker.Restore(rule.Marker);

                // The rule's current Marker only: a title still carrying the retired one is not yet
                // marked the way the rule now says, and "carries 🍞" should stay false until it is.
                // Anywhere in the block counts — "carries" is about presence, not order, so a block
                // an Apply would still reorder carries the Marker all the same.
                if (tags.Contains(rule.Key) && carries.Contains(own))
                {
                    marked[rule.Key] = marked.GetValueOrDefault(rule.Key) + 1;
                }

                // And stale where one of this rule's own Markers is in the block and nothing asks
                // for it here. One task counted once, however many of them are there: the figure is
                // the number of tasks a Remove scoped to this rule would change.
                var wasOwn = rule.RetiredMarker is { } previous ? Marker.Restore(previous) : (Marker?)null;

                if (staleHere.Contains(own) || (wasOwn is { } old && staleHere.Contains(old)))
                {
                    stale[rule.Key] = stale.GetValueOrDefault(rule.Key) + 1;
                }

                // And how much of the swap this rule is still waiting for. Counted only where an
                // Apply would really make it: a retired Marker two rules have a claim on is never
                // swapped, so a figure that counted it would promise a change nothing will make.
                if (wasOwn is { } swapping
                    && carries.Contains(swapping)
                    && known.Retired.TryGetValue(swapping, out var replacement)
                    && replacement == own)
                {
                    retired[rule.Key] = retired.GetValueOrDefault(rule.Key) + 1;
                }
            }

            foreach (var marker in abandoned)
            {
                if (staleHere.Contains(marker.Marker))
                {
                    staleAbandoned[marker.Marker] = staleAbandoned.GetValueOrDefault(marker.Marker) + 1;
                }
            }
        }

        var coverage = new Dictionary<Guid, MarkerCoverageCounts>();

        foreach (var rule in rules)
        {
            coverage[rule.Id] = new MarkerCoverageCounts(
                tagged.GetValueOrDefault(rule.Key),
                marked.GetValueOrDefault(rule.Key),
                stale.GetValueOrDefault(rule.Key),
                retired.GetValueOrDefault(rule.Key));
        }

        // A Marker nothing carries any more has nothing to offer and no button to offer it on. Left
        // out rather than shown as nought: the row exists only because the emoji is out there, and
        // one that is not is a row waiting to be dropped rather than something to tell somebody
        // about.
        var leftBehind = abandoned
            .Where(marker => staleAbandoned.ContainsKey(marker.Marker))
            .Select(marker => new AbandonedMarkerCounts(marker.Marker, staleAbandoned[marker.Marker]))
            .ToList();

        return new MarkerMeasurements(coverage, leftBehind);
    }

}

/// <param name="Tagged">Tasks carrying the Hashtag.</param>
/// <param name="Marked">Those of them whose block already carries the Marker.</param>
/// <param name="Stale">
/// Tasks that do <em>not</em> carry the Hashtag and whose block carries this rule's Marker, or the
/// one it retired. What a Remove Markers scoped to this rule would take away.
/// </param>
/// <param name="Retired">
/// Tasks whose block still carries the Marker this rule replaced, and which the next Apply would
/// swap. Nought when the rule has retired nothing, when the swap has already reached everywhere,
/// and when the retired Marker is one two rules have a claim on — no Apply swaps that, so nothing
/// is waiting on one.
/// </param>
internal sealed record MarkerCoverageCounts(int Tagged, int Marked, int Stale, int Retired);

/// <param name="Marker">A Marker of a rule the person deleted, still in at least one block.</param>
/// <param name="Stale">
/// How many tasks carry it. Every one of them, because nothing standing asks for it anywhere — a
/// deleted rule's Marker is stale wherever it is.
/// </param>
internal sealed record AbandonedMarkerCounts(Marker Marker, int Stale);

/// <summary>Both figures, from one walk over the same titles.</summary>
internal sealed record MarkerMeasurements(
    IReadOnlyDictionary<Guid, MarkerCoverageCounts> Coverage,
    IReadOnlyList<AbandonedMarkerCounts> Abandoned)
{
    public static MarkerMeasurements None { get; } = new(new Dictionary<Guid, MarkerCoverageCounts>(), []);
}
