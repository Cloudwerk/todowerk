using TodoWerk.Application.Abstractions.Messaging;

namespace TodoWerk.Application.Indexing.GetTaskLists;

public sealed record GetTaskListsQuery : IQuery<IReadOnlyList<TaskListDto>>;
