using TodoWerk.SharedKernel;
using TodoWerk.Application.Abstractions.Authentication;

namespace TodoWerk.Application.Indexing;

/// <summary>
/// Port for reading the tasks of one To Do list, a page at a time.
/// <para>
/// Everything goes through Graph's delta endpoint, including a first scan: called without a link
/// it returns every task in the list and, after the last page, a delta link for next time. One
/// code path serves both the initial scan and the incremental sync, and the initial scan comes
/// away with the token that makes the next one cheap.
/// </para>
/// </summary>
public interface ITodoTaskReader
{
    /// <summary>
    /// One page of tasks. <paramref name="link"/> is null to start reading a list from scratch,
    /// the previous page's <see cref="TodoTaskPage.NextLink"/> to continue, or a stored
    /// <see cref="TodoTaskPage.DeltaLink"/> to ask only for what has changed since.
    /// </summary>
    Task<Result<TodoTaskPage>> ReadTasksAsync(
        IndexUser user,
        string taskListId,
        string? link,
        CancellationToken cancellationToken);
}
