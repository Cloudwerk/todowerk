namespace TodoWerk.Application.Indexing;

/// <summary>
/// One page of tasks from Graph, with whichever link comes next.
/// </summary>
/// <param name="Tasks">The page's tasks, in the order Graph returned them.</param>
/// <param name="NextLink">More pages follow. Null on the last page.</param>
/// <param name="DeltaLink">
/// Present only on the last page: the token that makes the next sync incremental. Storing it is
/// what turns the following pass from "read every task" into "read what changed".
/// </param>
public sealed record TodoTaskPage(
    IReadOnlyList<TodoTaskDto> Tasks,
    string? NextLink,
    string? DeltaLink);

/// <summary>
/// One To Do task, reduced to what the index needs. Not a Graph type: the Application layer does
/// not depend on Graph's schema, and the index has no use for due dates, reminders or bodies.
/// </summary>
/// <param name="IsDeleted">
/// The task was removed. Delta pages report deletions as tombstones carrying an id and nothing
/// else, which is why every other member is optional.
/// </param>
public sealed record TodoTaskDto(
    string Id,
    string? Title,
    DateTimeOffset LastModifiedAt,
    bool IsDeleted);
