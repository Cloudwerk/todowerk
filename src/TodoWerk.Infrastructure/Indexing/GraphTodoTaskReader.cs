using System.Text.Json.Serialization;
using TodoWerk.Application.Indexing;
using TodoWerk.SharedKernel;
using TodoWerk.Infrastructure.Graph;
using TodoWerk.Application.Abstractions.Authentication;

namespace TodoWerk.Infrastructure.Indexing;

/// <summary>
/// Pages the tasks of one To Do list through Graph's delta endpoint.
/// <para>
/// Delta serves the first scan as well as every later one. Called with no link it returns the
/// whole list, page by page, and ends with a delta link; called with that link it returns only
/// what has changed. One path, and the expensive first pass pays for the cheap ones after it.
/// </para>
/// </summary>
internal sealed class GraphTodoTaskReader(GraphGateway graph) : ITodoTaskReader
{
    public async Task<Result<TodoTaskPage>> ReadTasksAsync(
        IndexUser user,
        string taskListId,
        string? link,
        CancellationToken cancellationToken)
    {
        // A link from Graph is absolute and already carries its own paging or delta token; only
        // the first request of a pass is built here.
        var requestUri = link ?? $"v1.0/me/todo/lists/{Uri.EscapeDataString(taskListId)}/tasks/delta";

        var response = await graph.GetAsync<GraphCollection<GraphTodoTask>>(user, requestUri, cancellationToken);

        if (response.IsFailure)
        {
            return Result.Failure<TodoTaskPage>(response.Error);
        }

        IReadOnlyList<TodoTaskDto> tasks =
        [
            .. (response.Value.Value ?? [])
                .Where(task => !string.IsNullOrEmpty(task.Id))
                .Select(task => new TodoTaskDto(
                    task.Id!,
                    task.Title,
                    task.LastModifiedDateTime ?? DateTimeOffset.MinValue,
                    task.Removed is not null)),
        ];

        return Result.Success(new TodoTaskPage(tasks, response.Value.NextLink, response.Value.DeltaLink));
    }

    /// <summary>
    /// A deleted task arrives as a tombstone: an id, the <c>@removed</c> annotation, and nothing
    /// else. Every other member is therefore optional, and reading one as required would make the
    /// whole page fail on the first deletion.
    /// </summary>
    private sealed record GraphTodoTask(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("title")] string? Title,
        [property: JsonPropertyName("lastModifiedDateTime")] DateTimeOffset? LastModifiedDateTime,
        [property: JsonPropertyName("@removed")] GraphRemoved? Removed);

    private sealed record GraphRemoved([property: JsonPropertyName("reason")] string? Reason);
}
