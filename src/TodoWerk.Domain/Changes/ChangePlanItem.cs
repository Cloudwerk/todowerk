using TodoWerk.SharedKernel;

namespace TodoWerk.Domain.Changes;

/// <summary>
/// One task a confirmed Change covers, with the titles the preview showed and what became of it.
/// <para>
/// Persisting the plan is what fixes the scope to what the user actually confirmed: a task that
/// acquires the tag while the Change waits in the queue is not swept in. ADR-0006 makes this one
/// row the shared mechanism behind progress, resume and cancel.
/// </para>
/// <para>
/// The titles here are advisory for a forward Change — the run re-reads the task and applies the
/// rewrite to whatever comes back — and exact for an undo, where <see cref="PreviewedTitle"/> is
/// the title TodoWerk wrote and is compared before anything is restored.
/// </para>
/// </summary>
public sealed class ChangePlanItem : Entity<Guid>
{
    private ChangePlanItem(
        Guid id,
        string tenantId,
        string userId,
        Guid changeId,
        int sequence,
        string taskListId,
        string graphTaskId,
        string previewedTitle,
        string previewedNewTitle)
        : base(id)
    {
        TenantId = tenantId;
        UserId = userId;
        ChangeId = changeId;
        Sequence = sequence;
        TaskListId = taskListId;
        GraphTaskId = graphTaskId;
        PreviewedTitle = previewedTitle;
        PreviewedNewTitle = previewedNewTitle;
    }

    public string TenantId { get; private set; }

    public string UserId { get; private set; }

    /// <summary>The Change this row belongs to. Deleting the Change deletes its plan.</summary>
    public Guid ChangeId { get; private set; }

    /// <summary>
    /// The order the run walks in. Explicit rather than left to the clustered key, because resume
    /// has to mean "carry on where it stopped" and not "wherever the database feels like".
    /// </summary>
    public int Sequence { get; private set; }

    public string TaskListId { get; private set; }

    public string GraphTaskId { get; private set; }

    /// <summary>The title as the preview read it out of the index.</summary>
    public string PreviewedTitle { get; private set; }

    /// <summary>The title the preview showed the user. What is actually written is recomputed.</summary>
    public string PreviewedNewTitle { get; private set; }

    public ChangePlanItemStatus Status { get; private set; } = ChangePlanItemStatus.Pending;

    /// <summary>Why this one task was skipped or failed, in the words the UI shows.</summary>
    public string? Outcome { get; private set; }

    public DateTimeOffset? AttemptedAt { get; private set; }

    public static ChangePlanItem Plan(
        string tenantId,
        string userId,
        Guid changeId,
        int sequence,
        string taskListId,
        string graphTaskId,
        string previewedTitle,
        string previewedNewTitle) =>
        new(
            Guid.CreateVersion7(),
            tenantId,
            userId,
            changeId,
            sequence,
            taskListId,
            graphTaskId,
            previewedTitle,
            previewedNewTitle);

    public void MarkWritten(DateTimeOffset attemptedAt)
    {
        Status = ChangePlanItemStatus.Written;
        Outcome = null;
        AttemptedAt = attemptedAt;
    }

    public void MarkSkipped(string reason, DateTimeOffset attemptedAt)
    {
        Status = ChangePlanItemStatus.Skipped;
        Outcome = reason;
        AttemptedAt = attemptedAt;
    }

    public void MarkFailed(string reason, DateTimeOffset attemptedAt)
    {
        Status = ChangePlanItemStatus.Failed;
        Outcome = reason;
        AttemptedAt = attemptedAt;
    }
}
