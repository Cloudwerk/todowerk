using TodoWerk.Application.Abstractions.Authentication;
using TodoWerk.SharedKernel;

namespace TodoWerk.Application.Changes;

/// <summary>
/// Reading one task and writing its title back — the two halves of one write, together, because
/// they are inseparable: <c>todoTask</c> carries no ETag and <c>Update todoTask</c> takes no
/// <c>If-Match</c>, so re-reading immediately before the PATCH is the only concurrency control
/// Microsoft To Do offers (ADR-0006).
/// </summary>
public interface ITodoTaskWriter
{
    /// <summary>
    /// The task as it is right now. Read from Graph rather than from the index, because the
    /// indexed title is truncated to the display column and is as old as the last scan.
    /// </summary>
    Task<Result<TodoTaskSnapshot>> ReadTaskAsync(
        IndexUser user,
        string taskListId,
        string graphTaskId,
        CancellationToken cancellationToken);

    /// <summary>Writes the title and nothing else. One task, one request; never a batch (ADR-0006).</summary>
    Task<Result> WriteTitleAsync(
        IndexUser user,
        string taskListId,
        string graphTaskId,
        string title,
        CancellationToken cancellationToken);
}

/// <param name="Title">The full title, untruncated. What the journal records and what undo compares against.</param>
public sealed record TodoTaskSnapshot(string GraphTaskId, string Title);
