namespace TodoWerk.Domain.Changes;

/// <summary>
/// What became of one planned task. Per-task rather than per-Change because this one column is
/// what gives progress, resume after a lost lease or a deploy, and cancel their shared mechanism
/// (ADR-0006): a Change that is interrupted resumes at the first row still
/// <see cref="Pending"/> rather than starting over.
/// </summary>
public enum ChangePlanItemStatus
{
    /// <summary>Not attempted yet. Where a resumed run picks up.</summary>
    Pending = 0,

    /// <summary>Re-read, rewritten and written back. It has a journal row.</summary>
    Written = 1,

    /// <summary>
    /// Deliberately passed over: the Hashtag was no longer in the title, the title already read
    /// the way the Change wanted it, or the task was gone. Not a failure — the user asked for a
    /// tag to change and there was no longer a tag there to change.
    /// </summary>
    Skipped = 2,

    /// <summary>
    /// Graph refused it for a reason the next task would meet as well — an expired grant, a
    /// throttle the gateway already waited out, an outage. The run stops on this row rather than
    /// spending the other nine hundred attempts to learn the same thing; the rows behind it stay
    /// <see cref="Pending"/>, and what was written before it stays written and undoable.
    /// </summary>
    Failed = 3,
}
