using TodoWerk.SharedKernel;
using TodoWerk.Application.Abstractions.Authentication;

namespace TodoWerk.Application.Indexing;

/// <summary>
/// Port for reading how current the index is. Separate from the inventory read model: freshness
/// is chrome the Workbench shows around the table, and it is asked for far more often than the
/// table itself.
/// </summary>
public interface IIndexStatusReader
{
    Task<Result<IndexStatusDto>> GetStatusAsync(IndexUser user, CancellationToken cancellationToken);
}
