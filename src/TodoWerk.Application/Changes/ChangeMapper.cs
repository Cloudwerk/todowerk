namespace TodoWerk.Application.Changes;

/// <summary>
/// One place where a stored Change becomes the thing the Workbench renders, so that whether undo
/// is offered cannot differ between the queue view and the answer to confirming or cancelling one.
/// </summary>
internal static class ChangeMapper
{
    public static ChangeDto ToDto(ChangeRecord change, DateTimeOffset now, TimeSpan retention) =>
        new(
            change.Id,
            change.Kind,
            change.SourceKeys,
            change.TargetSpelling,
            change.AppliedMarkers,
            change.State,
            change.PlannedTaskCount,
            change.WrittenCount,
            change.SkippedCount,
            change.FailedCount,
            change.CancelRequested,
            change.RequestedAt,
            change.CompletedAt,
            change.FailureCode,
            change.FailureReason,
            change.UndoOfChangeId,
            ChangeUndoPolicy.CanUndo(change, now, retention));
}
