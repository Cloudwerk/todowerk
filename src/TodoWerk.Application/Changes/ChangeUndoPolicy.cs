using TodoWerk.Domain.Changes;

namespace TodoWerk.Application.Changes;

/// <summary>
/// When undo is offered. Three rules, in one place, because they are asked twice: once to decide
/// whether the button appears, and once to decide whether pressing it is honoured.
/// </summary>
public static class ChangeUndoPolicy
{
    /// <summary>
    /// Whole-Change, one level deep, inside the retention window, and only for a Change that wrote
    /// something (ADR-0006). Every terminal state qualifies, failures and cancellations included:
    /// those wrote the tasks before they stopped, and those are exactly the ones somebody wants
    /// back.
    /// </summary>
    public static bool CanUndo(ChangeRecord change, DateTimeOffset now, TimeSpan retention)
    {
        ArgumentNullException.ThrowIfNull(change);

        return change is
        {
            State: ChangeState.Completed
                    or ChangeState.CompletedWithSkips
                    or ChangeState.Failed
                    or ChangeState.Cancelled,
            WrittenCount: > 0,
            UndoOfChangeId: null,
            HasBeenUndone: false,
        }
            && change.RequestedAt > now - retention;
    }
}
