namespace TodoWerk.Application.Indexing;

/// <summary>
/// One page of the inventory — the screen the product actually is. Paged rather than shipped
/// whole: thousands of rows per tenant is not millions (ADR-0003), but it is not small enough to
/// sort in a browser either.
/// </summary>
/// <param name="TotalCount">Rows matching the filter, not rows on this page. The pager needs both.</param>
public sealed record HashtagInventoryPage(
    IReadOnlyList<HashtagInventoryRow> Rows,
    int TotalCount,
    int Page,
    int PageSize);

/// <summary>
/// One Hashtag as the Workbench shows it: what it is called, how much it is used, and what — if
/// anything — looks wrong with it.
/// </summary>
/// <param name="CanonicalSpelling">
/// The Spelling shown to the user: the most frequent one, ties broken by most recent use
/// (ADR-0005). Lower-casing everything is not an option — <c>#ProjectAlpha</c> is a
/// CamelCase tag whose casing carries meaning.
/// </param>
/// <param name="Spellings">Every Spelling observed, most used first. One row, many spellings.</param>
/// <param name="HasMultipleSpellings">
/// This one Hashtag is written more than one way. Resolving it is Normalise Casing, never a Merge.
/// </param>
/// <param name="HasNearDuplicates">
/// Another Hashtag is one character away. Advice, not a verdict — the pair may well be two real
/// tags.
/// </param>
/// <param name="IsStale">None of its tasks has been modified inside the configured window.</param>
public sealed record HashtagInventoryRow(
    string Key,
    string CanonicalSpelling,
    IReadOnlyList<string> Spellings,
    int TaskCount,
    int ListCount,
    DateTimeOffset LastUsedAt,
    bool HasMultipleSpellings,
    bool HasNearDuplicates,
    bool IsStale);

/// <summary>What the Workbench's column headers sort by. A closed set, because it reaches SQL.</summary>
public enum HashtagInventorySort
{
    /// <summary>Busiest first, which is the order the inventory is most useful in.</summary>
    TaskCount = 0,

    /// <summary>Alphabetical by Canonical Spelling.</summary>
    Spelling = 1,

    LastUsed = 2,

    ListCount = 3,
}

/// <summary>
/// A flag to narrow the inventory to. Filtering happens in the query rather than over a page, so
/// "show me the casing problems" means all of them and not the ones that happen to be on screen.
/// </summary>
public enum HashtagInventoryFilter
{
    None = 0,

    /// <summary>One Hashtag written more than one way.</summary>
    MultipleSpellings = 1,

    NearDuplicates = 2,
    Stale = 3,
}
