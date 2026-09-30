using TodoWerk.Domain.Hashtags;

namespace TodoWerk.Domain.Changes;

/// <summary>
/// A set of Marker Rules, ready to be applied to a title — or taken back out of one. Give it the
/// Markers a Change carries and ask it what one title should say next (ADR-0014).
/// <para>
/// One place, because two ask: the planner works out what the preview shows, and the runner works
/// out what is actually written into the task Graph handed back. They must agree — a plan that
/// showed one block and a run that wrote another would make the preview a lie — and the only way to
/// be sure of that is for both to be the same code over the same list.
/// </para>
/// <para>
/// What a block is, and which of its Markers anything still asks for, is <see cref="MarkerRuleSet"/>
/// in the shared Hashtags namespace rather than anything worked out here: the Markers module counts
/// the stale ones for the figure beside each rule, and a count that disagreed with the Change
/// standing next to it is the failure ADR-0014 held that count back to avoid.
/// </para>
/// </summary>
public sealed class MarkerPlan
{
    private readonly MarkerRuleSet _rules;

    private readonly IReadOnlySet<Marker> _removable;

    private MarkerPlan(MarkerRuleSet rules, IReadOnlySet<Marker> removable)
    {
        _rules = rules;
        _removable = removable;
    }

    /// <summary>
    /// Reads the Markers a Change carries into what a rewrite needs, and — for a Remove — which of
    /// them this run may take away.
    /// </summary>
    public static MarkerPlan From(IReadOnlyCollection<AppliedMarker> markers)
    {
        ArgumentNullException.ThrowIfNull(markers);

        var entries = new List<MarkerRuleEntry>(markers.Count);
        var removable = new HashSet<Marker>();

        foreach (var applied in markers)
        {
            var marker = Marker.Restore(applied.Marker);
            var retired = applied.RetiredMarker is { } previous ? Marker.Restore(previous) : (Marker?)null;

            entries.Add(new MarkerRuleEntry(applied.Key, marker, retired, applied.Position, applied.Abandoned));

            // The scope of a Remove, taken whole: the Marker and whatever it replaced. A rule's
            // retired Marker is that rule's own residue, and a scope that named one without the
            // other would offer to clear a rule's Markers and then leave half of them behind.
            if (applied.Removable)
            {
                removable.Add(marker);

                if (retired is { } dropped)
                {
                    removable.Add(dropped);
                }
            }
        }

        return new MarkerPlan(MarkerRuleSet.From(entries), removable);
    }

    /// <summary>
    /// Every Marker this person has: the ones their standing rules hold, the ones those rules have
    /// retired, and the ones their deleted rules left behind. What the planner narrows its search
    /// for candidate tasks with, and what a block is read against.
    /// </summary>
    public IReadOnlySet<Marker> EveryMarker => _rules.Known;

    /// <summary>
    /// The Markers a Remove Markers may take away: the ones its scope named, each with the Marker
    /// its rule retired. Empty for an Apply, which removes nothing.
    /// <para>
    /// Read off the Change's own copy like everything else here, so the scope somebody confirmed is
    /// the scope that runs however the rules table has moved on since.
    /// </para>
    /// </summary>
    public IReadOnlySet<Marker> Removable => _removable;

    /// <summary>
    /// The folded keys of the Hashtags written in <paramref name="title"/>, read through the same
    /// grammar the index counted them with — so "this task has that tag" cannot mean two things.
    /// <para>
    /// Handed out rather than kept private because the runner asks it a second question of its own:
    /// whether the task still carries a Hashtag the Change was scoped to. Walking the title twice
    /// for two answers would be one walk too many.
    /// </para>
    /// </summary>
    public static IReadOnlySet<string> HashtagKeysIn(string? title) =>
        new HashSet<string>(
            HashtagExtractor.Extract(title).Select(hashtag => hashtag.Key),
            StringComparer.Ordinal);

    /// <summary>
    /// What this title should say once the block is right, or null when it already says it — the
    /// "already read the way the change asked for" skip the other three Changes share.
    /// </summary>
    public string? RewriteFor(string? title) => RewriteFor(title, HashtagKeysIn(title));

    /// <param name="hashtagKeys">
    /// What <see cref="HashtagKeysIn"/> answered for this title, when the caller has already asked.
    /// </param>
    public string? RewriteFor(string? title, IReadOnlySet<string> hashtagKeys)
    {
        ArgumentNullException.ThrowIfNull(hashtagKeys);

        var wanted = new List<Marker>(_rules.Rules.Count);

        // In rule order, and every rule whose Hashtag is on the task — not only the ones a Change
        // was scoped to, because the write is the whole block (ADR-0014). The rule's own Marker
        // only: a retired one is what the swap below replaces, not something to write.
        foreach (var (key, marker) in _rules.Rules)
        {
            if (hashtagKeys.Contains(key))
            {
                wanted.Add(marker);
            }
        }

        return MarkerBlockRewriter.Rewrite(title, wanted, _rules.Known, _rules.Retired);
    }

    /// <summary>
    /// What this title should say once the stale Markers in this Change's scope are gone, or null
    /// when it holds none — the same "already read the way the change asked for" skip the other
    /// Changes share. May be empty, for a title that was nothing but stale Markers; the caller
    /// names that as a skip rather than writing it.
    /// <para>
    /// Stale means one thing, and <see cref="MarkerRuleSet.WantedIn"/> is where it is decided: no
    /// standing rule asks for this Marker on this task.
    /// </para>
    /// </summary>
    public string? RemoveFor(string? title) => RemoveFor(title, HashtagKeysIn(title));

    /// <param name="hashtagKeys">
    /// What <see cref="HashtagKeysIn"/> answered for this title, when the caller has already asked.
    /// </param>
    public string? RemoveFor(string? title, IReadOnlySet<string> hashtagKeys)
    {
        ArgumentNullException.ThrowIfNull(hashtagKeys);

        return MarkerBlockRewriter.Remove(title, _rules.WantedIn(hashtagKeys), _rules.Known, _removable);
    }

    /// <summary>
    /// The Markers of this Change that a completed run may be said to have taken out of every block
    /// there was: in scope, and covered by a plan that knew of every task carrying them.
    /// </summary>
    public static IReadOnlySet<Marker> Reaching(IReadOnlyCollection<AppliedMarker> markers)
    {
        ArgumentNullException.ThrowIfNull(markers);

        var reaching = new HashSet<Marker>();

        foreach (var applied in markers)
        {
            if (!applied.Removable || !applied.Reaches)
            {
                continue;
            }

            reaching.Add(Marker.Restore(applied.Marker));

            if (applied.RetiredMarker is { } dropped)
            {
                reaching.Add(Marker.Restore(dropped));
            }
        }

        return reaching;
    }

    /// <summary>
    /// The Markers this title's block holds that nothing asks for on this task, whatever this
    /// Change's scope is. What the runner reads to decide whether a Marker it took is out of the
    /// titles everywhere it was.
    /// </summary>
    public IReadOnlySet<Marker> StaleIn(string? title) => _rules.StaleIn(title, HashtagKeysIn(title));

    /// <summary>The Markers at the front of this title, asked for or not.</summary>
    public IReadOnlyList<Marker> BlockOf(string? title) => _rules.BlockOf(title);
}
