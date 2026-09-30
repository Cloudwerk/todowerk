using System.Text.Json.Serialization;
using TodoWerk.Application.Indexing;
using TodoWerk.SharedKernel;
using TodoWerk.Infrastructure.Graph;
using TodoWerk.Application.Abstractions.Authentication;

namespace TodoWerk.Infrastructure.Indexing;

/// <summary>
/// Reads a user's To Do lists from Microsoft Graph with the delegated token held in the
/// server-side cache. The browser never sees a Graph token (ADR-0002).
/// </summary>
internal sealed class GraphTaskListsReader(GraphGateway graph) : ITaskListsReader
{
    public async Task<Result<IReadOnlyList<TaskListDto>>> GetTaskListsAsync(
        IndexUser user,
        CancellationToken cancellationToken)
    {
        var lists = new List<TaskListDto>();
        string? link = "v1.0/me/todo/lists";

        // Paged, even though most accounts have a handful of lists. The scan treats a list it did
        // not see as deleted and drops its indexed tasks, so a second page silently ignored would
        // not merely hide those lists — it would delete their half of the index on every scan.
        while (link is not null)
        {
            var response = await graph.GetAsync<GraphCollection<GraphTodoTaskList>>(
                user,
                link,
                cancellationToken);

            if (response.IsFailure)
            {
                return Result.Failure<IReadOnlyList<TaskListDto>>(response.Error);
            }

            lists.AddRange((response.Value.Value ?? [])
                .Select(list => new TaskListDto(
                    list.Id ?? string.Empty,
                    list.DisplayName ?? string.Empty,
                    ToKind(list.WellknownListName),
                    list.IsShared ?? false,
                    list.IsOwner ?? false)));

            link = response.Value.NextLink;
        }

        // The Workbench's list picker needs a stable order; Graph does not promise one. Invariant
        // culture rather than ordinal: ordinal compares UTF-16 code units, which puts every umlaut
        // above 'Z' and exiles "Änderungen" to the bottom of a German user's picker. Invariant is
        // still independent of the server's locale — the property that mattered — while collating
        // the way a reader expects.
        IReadOnlyList<TaskListDto> ordered =
        [
            .. lists.OrderBy(list => list.DisplayName, StringComparer.InvariantCultureIgnoreCase),
        ];

        return Result.Success(ordered);
    }

    /// <summary>
    /// An unrecognised well-known name is deliberately not treated as an ordinary list: Graph only
    /// assigns one to a list the service owns, so a name added after this code was written is
    /// still something TodoWerk must not offer to rename.
    /// </summary>
    private static TaskListKind ToKind(string? wellknownListName) => wellknownListName switch
    {
        null or "" or "none" => TaskListKind.Normal,
        "defaultList" => TaskListKind.Default,
        "flaggedEmails" => TaskListKind.FlaggedEmails,
        _ => TaskListKind.Unknown,
    };

    private sealed record GraphTodoTaskList(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("displayName")] string? DisplayName,
        [property: JsonPropertyName("wellknownListName")] string? WellknownListName,
        [property: JsonPropertyName("isShared")] bool? IsShared,
        [property: JsonPropertyName("isOwner")] bool? IsOwner);
}
