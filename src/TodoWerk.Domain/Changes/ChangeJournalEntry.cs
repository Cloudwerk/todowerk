using TodoWerk.SharedKernel;

namespace TodoWerk.Domain.Changes;

/// <summary>
/// What a Change actually did to one task: the title found immediately before the write, and the
/// title written. One row per task written, and nothing else in the system knows a task's previous
/// title.
/// <para>
/// It records the title <em>read</em> rather than the title the preview showed, because those can
/// differ — <c>todoTask</c> offers no ETag, so the run re-reads every task and applies the rewrite
/// to whatever comes back (ADR-0006). Undo restores what was really there.
/// </para>
/// <para>
/// Kept for thirty days, not the queue's seven. A queue is not an audit trail, and this is the
/// only record there is.
/// </para>
/// </summary>
public sealed class ChangeJournalEntry : Entity<Guid>
{
    private ChangeJournalEntry(
        Guid id,
        string tenantId,
        string userId,
        Guid changeId,
        string taskListId,
        string graphTaskId,
        string titleBefore,
        string titleAfter,
        DateTimeOffset writtenAt)
        : base(id)
    {
        TenantId = tenantId;
        UserId = userId;
        ChangeId = changeId;
        TaskListId = taskListId;
        GraphTaskId = graphTaskId;
        TitleBefore = titleBefore;
        TitleAfter = titleAfter;
        WrittenAt = writtenAt;
    }

    public string TenantId { get; private set; }

    public string UserId { get; private set; }

    /// <summary>The Change that wrote it. Deleting the Change deletes its journal.</summary>
    public Guid ChangeId { get; private set; }

    public string TaskListId { get; private set; }

    public string GraphTaskId { get; private set; }

    /// <summary>The full title read from Graph immediately before the write. What undo restores.</summary>
    public string TitleBefore { get; private set; }

    /// <summary>What was written. Undo restores only if the task still says exactly this.</summary>
    public string TitleAfter { get; private set; }

    public DateTimeOffset WrittenAt { get; private set; }

    public static ChangeJournalEntry Record(
        string tenantId,
        string userId,
        Guid changeId,
        string taskListId,
        string graphTaskId,
        string titleBefore,
        string titleAfter,
        DateTimeOffset writtenAt) =>
        new(
            Guid.CreateVersion7(),
            tenantId,
            userId,
            changeId,
            taskListId,
            graphTaskId,
            titleBefore,
            titleAfter,
            writtenAt);
}
