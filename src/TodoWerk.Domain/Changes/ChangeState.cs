namespace TodoWerk.Domain.Changes;

/// <summary>
/// Where a Change is. Four of these are terminal, and undo is offered for whatever was written in
/// every one of them (ADR-0006) — a Change that failed halfway still wrote the tasks before the
/// failure, and those are exactly the ones somebody wants back.
/// <para>
/// There is no paused state. Cancel stops the run after the current task; a paused Change holding
/// a lease is a wedged row waiting to be discovered.
/// </para>
/// </summary>
public enum ChangeState
{
    /// <summary>Confirmed, planned, and waiting for the worker.</summary>
    Pending = 0,

    /// <summary>Claimed by a worker, which holds its lease and is writing tasks.</summary>
    Running = 1,

    /// <summary>Every planned task was written.</summary>
    Completed = 2,

    /// <summary>
    /// The run finished, but some tasks were passed over — the Hashtag was gone by the time the
    /// task was re-read, or the task itself was. Distinguished from <see cref="Completed"/>
    /// because the count the user confirmed and the count that was written differ, and saying so
    /// is the whole reason preview counts are called advisory.
    /// </summary>
    CompletedWithSkips = 3,

    /// <summary>The run stopped on something it could not get past. What it wrote stays written.</summary>
    Failed = 4,

    /// <summary>Someone asked it to stop, and it stopped after the task it was on.</summary>
    Cancelled = 5,
}
