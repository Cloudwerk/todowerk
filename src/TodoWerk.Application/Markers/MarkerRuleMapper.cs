namespace TodoWerk.Application.Markers;

/// <summary>One place a stored rule becomes the thing the Workbench renders.</summary>
internal static class MarkerRuleMapper
{
    /// <summary>
    /// Without coverage, for the answers to creating, changing and reordering a rule. Those come
    /// back to a client that is about to reload the list anyway, and counting the index for them
    /// would put an inventory-sized query behind every button. The noughts are a placeholder and
    /// not a figure: a client that rendered one of these answers as a row would say "no tagged
    /// tasks" about a Hashtag it had not counted, which is why the Workbench re-reads the list
    /// instead of patching a row into it.
    /// </summary>
    public static MarkerRuleDto ToDto(MarkerRuleRecord rule) =>
        ToDto(rule, new MarkerCoverageCounts(0, 0, 0, 0));

    public static MarkerRuleDto ToDto(MarkerRuleRecord rule, MarkerCoverageCounts coverage) =>
        new(
            rule.Id,
            rule.Key,
            rule.Spelling,
            rule.Marker,
            rule.RetiredMarker,
            rule.Position,
            coverage.Tagged,
            coverage.Marked,
            coverage.Stale,
            coverage.Retired);
}
