using System.Text.Json.Serialization;
using TodoWerk.Application.Abstractions.Authentication;
using TodoWerk.Application.Changes;
using TodoWerk.Infrastructure.Graph;
using TodoWerk.SharedKernel;

namespace TodoWerk.Infrastructure.Changes;

/// <summary>
/// The write path against Microsoft To Do: <c>GET</c> one task, then <c>PATCH</c> its title.
/// Sequential, one request per task, through the gateway that already knows how to hold a token,
/// wait out a throttle and word a failure.
/// </summary>
internal sealed class GraphTodoTaskWriter(GraphGateway graph) : ITodoTaskWriter
{
    public async Task<Result<TodoTaskSnapshot>> ReadTaskAsync(
        IndexUser user,
        string taskListId,
        string graphTaskId,
        CancellationToken cancellationToken)
    {
        var response = await graph.GetAsync<GraphTodoTask>(user, TaskUri(taskListId, graphTaskId), cancellationToken);

        if (response.IsFailure)
        {
            return Result.Failure<TodoTaskSnapshot>(response.Error);
        }

        // A task with no id is not a task Graph is describing; treated as gone rather than
        // written blind.
        return response.Value is { Id.Length: > 0 } task
            ? Result.Success(new TodoTaskSnapshot(task.Id, task.Title ?? string.Empty))
            : Result.Failure<TodoTaskSnapshot>(GraphErrors.NotFound);
    }

    public Task<Result> WriteTitleAsync(
        IndexUser user,
        string taskListId,
        string graphTaskId,
        string title,
        CancellationToken cancellationToken) =>
        // Only the title. Sending back the whole task would mean deciding what to do with every
        // other property Graph returned, including the ones it did not.
        graph.PatchAsync(user, TaskUri(taskListId, graphTaskId), new TodoTaskTitlePatch(title), cancellationToken);

    private static string TaskUri(string taskListId, string graphTaskId) =>
        $"v1.0/me/todo/lists/{Uri.EscapeDataString(taskListId)}/tasks/{Uri.EscapeDataString(graphTaskId)}";

    private sealed record GraphTodoTask(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("title")] string? Title);

    private sealed record TodoTaskTitlePatch([property: JsonPropertyName("title")] string Title);
}
