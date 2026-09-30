using TodoWerk.Application.Abstractions.Authentication;
using TodoWerk.Application.Abstractions.Messaging;
using TodoWerk.SharedKernel;

namespace TodoWerk.Application.Indexing.GetHashtagInventory;

internal sealed class GetHashtagInventoryQueryHandler(
    ICurrentUser currentUser,
    IHashtagInventoryReader reader) : IQueryHandler<GetHashtagInventoryQuery, HashtagInventoryPage>
{
    /// <summary>
    /// Bounds what one request can ask for. Without it a caller can turn a paged read model back
    /// into "ship the whole inventory", which is the thing paging exists to prevent.
    /// </summary>
    internal const int MaxPageSize = 200;

    /// <summary>
    /// Far past any real inventory, and low enough that page × page-size stays comfortably inside
    /// an integer. An unbounded page number is not a bigger request — it is arithmetic overflow
    /// wearing a query parameter.
    /// </summary>
    internal const int MaxPage = 1_000_000;

    public Task<Result<HashtagInventoryPage>> HandleAsync(
        GetHashtagInventoryQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var user = IndexUser.From(currentUser);

        if (user is null)
        {
            return Task.FromResult(Result.Failure<HashtagInventoryPage>(IndexingErrors.NotSignedIn));
        }

        var bounded = query with
        {
            Page = Math.Clamp(query.Page, 1, MaxPage),
            PageSize = Math.Clamp(query.PageSize, 1, MaxPageSize),
            Search = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim(),
        };

        return reader.GetInventoryAsync(user, bounded, cancellationToken);
    }
}
