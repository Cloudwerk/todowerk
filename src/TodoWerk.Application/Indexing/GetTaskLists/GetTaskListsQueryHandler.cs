using TodoWerk.Application.Abstractions.Authentication;
using TodoWerk.Application.Abstractions.Messaging;
using TodoWerk.SharedKernel;

namespace TodoWerk.Application.Indexing.GetTaskLists;

internal sealed class GetTaskListsQueryHandler(ICurrentUser currentUser, ITaskListsReader taskListsReader)
    : IQueryHandler<GetTaskListsQuery, IReadOnlyList<TaskListDto>>
{
    public Task<Result<IReadOnlyList<TaskListDto>>> HandleAsync(
        GetTaskListsQuery query,
        CancellationToken cancellationToken)
    {
        var user = IndexUser.From(currentUser);

        return user is null
            ? Task.FromResult(Result.Failure<IReadOnlyList<TaskListDto>>(IndexingErrors.NotSignedIn))
            : taskListsReader.GetTaskListsAsync(user, cancellationToken);
    }
}
