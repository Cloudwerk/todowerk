using TodoWerk.Application.Indexing.GetHashtagInventory;
using TodoWerk.SharedKernel;
using TodoWerk.Application.Abstractions.Authentication;

namespace TodoWerk.Application.Indexing;

/// <summary>Port for the inventory read model. Implemented in Infrastructure, computed in SQL.</summary>
public interface IHashtagInventoryReader
{
    Task<Result<HashtagInventoryPage>> GetInventoryAsync(
        IndexUser user,
        GetHashtagInventoryQuery query,
        CancellationToken cancellationToken);
}
