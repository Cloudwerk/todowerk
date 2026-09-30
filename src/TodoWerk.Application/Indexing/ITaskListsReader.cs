using TodoWerk.SharedKernel;
using TodoWerk.Application.Abstractions.Authentication;

namespace TodoWerk.Application.Indexing;

/// <summary>
/// Port for reading a user's To Do task lists from Microsoft Graph.
/// Implemented in Infrastructure; the Application layer never sees Graph types.
/// </summary>
public interface ITaskListsReader
{
    Task<Result<IReadOnlyList<TaskListDto>>> GetTaskListsAsync(IndexUser user, CancellationToken cancellationToken);
}
