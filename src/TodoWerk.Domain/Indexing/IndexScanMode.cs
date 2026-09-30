namespace TodoWerk.Domain.Indexing;

/// <summary>How much of a list a scan reads.</summary>
public enum IndexScanMode
{
    /// <summary>
    /// Every task in the list, page by page. What a list gets the first time, and what it falls
    /// back to when Graph refuses a delta token.
    /// </summary>
    Full = 0,

    /// <summary>
    /// Only what changed since the last pass, using Graph's delta token. Degrades to
    /// <see cref="Full"/> per list when there is no usable token.
    /// </summary>
    Delta = 1,
}
