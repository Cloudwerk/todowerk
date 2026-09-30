using TodoWerk.Application.Abstractions.Messaging;

namespace TodoWerk.Application.Indexing.GetHashtagInventory;

/// <summary>
/// The inventory the Workbench draws. Sorting, filtering and paging are part of the question
/// because they are answered in SQL — a client-side copy of thousands of rows would be a
/// different product.
/// </summary>
/// <param name="Search">Matches anywhere in the Hashtag's name, ignoring casing.</param>
public sealed record GetHashtagInventoryQuery(
    string? Search = null,
    HashtagInventoryFilter Filter = HashtagInventoryFilter.None,
    HashtagInventorySort Sort = HashtagInventorySort.TaskCount,
    bool Descending = true,
    int Page = 1,
    int PageSize = 50) : IQuery<HashtagInventoryPage>;
