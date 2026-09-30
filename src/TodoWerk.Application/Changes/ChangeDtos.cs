using TodoWerk.Application.Abstractions.Indexing;
using TodoWerk.Domain.Changes;
using TodoWerk.Domain.Failures;

namespace TodoWerk.Application.Changes;

/// <summary>
/// What the user confirms: a specific set of tasks, not an intention. The pairs are computed from
/// Occurrences and shown in full, because a bulk rewrite nobody can inspect is a bulk rewrite
/// nobody should confirm.
/// </summary>
/// <param name="TaskCount">
/// How many tasks the plan covers. Advisory — the run re-reads each task and may skip it — and the
/// UI says so rather than presenting it as a promise (ADR-0006).
/// </param>
/// <param name="RequiresMergeConfirmation">
/// This Change folds Hashtags together and destroys a distinction the user made. The only one of
/// the three operations that asks twice.
/// </param>
/// <param name="ExcludedLists">
/// Lists that contribute nothing because they have never been read end to end. Named rather than
/// silently dropped: ADR-0003 wrote "writes never run against a half-scanned list" to prevent
/// exactly the shortfall that hiding these would cause.
/// </param>
public sealed record ChangePreviewDto(
    ChangeKind Kind,
    IReadOnlyList<string> SourceKeys,
    string TargetSpelling,
    IReadOnlyList<ChangePreviewItemDto> Items,
    int TaskCount,
    bool RequiresMergeConfirmation,
    IReadOnlyList<ExcludedTaskList> ExcludedLists,
    IReadOnlyList<ChangePreviewSkipDto> Skips,
    ChangeMarkerRulesDto MarkerRules);

/// <summary>
/// What this Change would do to the Marker Rules involved in it (ADR-0014). Nothing here writes a
/// title: a rule follows a Rename to the new name, and a Merge's losing rules are deleted.
/// <para>
/// Computed server-side and shown in the confirm dialog, because "your marker moves too" is part of
/// what somebody is agreeing to and finding out afterwards is finding out too late.
/// </para>
/// </summary>
/// <param name="SourceRules">The rules on the Hashtags being folded away, in the order they list.</param>
/// <param name="TargetRule">The target's own rule, if it has one. It wins over any source rule.</param>
/// <param name="SurvivingMarker">
/// The Marker the target ends up carrying, or null — either because no rule is involved, or because
/// several sources hold one and the user has not said which survives.
/// </param>
/// <param name="RequiresSurvivorChoice">
/// Several sources hold a rule and the target holds none, so one of them has to be chosen. Refused
/// at confirmation without it, the way an unconfirmed Merge is.
/// </param>
public sealed record ChangeMarkerRulesDto(
    IReadOnlyList<ChangeMarkerRuleDto> SourceRules,
    ChangeMarkerRuleDto? TargetRule,
    string? SurvivingMarker,
    bool RequiresSurvivorChoice)
{
    /// <summary>A Change that touches nobody's rules.</summary>
    public static ChangeMarkerRulesDto None { get; } = new([], null, null, false);
}

/// <summary>One Marker Rule, as a Change's preview names it.</summary>
public sealed record ChangeMarkerRuleDto(string Key, string Spelling, string Marker);

/// <summary>
/// A task the plan already knows it will pass over, named before anybody confirms rather than
/// discovered afterwards in the outcome. Today there is one: a title the block would push past
/// what Microsoft To Do stores (ADR-0014).
/// </summary>
public sealed record ChangePreviewSkipDto(
    string TaskListId,
    string ListDisplayName,
    string CurrentTitle,
    string Reason);

/// <summary>One task the Change covers, as the preview shows it.</summary>
public sealed record ChangePreviewItemDto(
    string TaskListId,
    string ListDisplayName,
    string CurrentTitle,
    string NewTitle);

/// <summary>
/// A Change as the Workbench shows it: what it does, how far it has got, and whether it can be
/// taken back.
/// </summary>
/// <param name="CanUndo">
/// Whole-Change undo is offered one level deep, inside the retention window, and only for a Change
/// that actually wrote something (ADR-0006). Decided here rather than in the browser, because it
/// is a rule and not a rendering choice.
/// </param>
/// <param name="FailureCode">
/// What the client switches on. The sentence in <paramref name="FailureReason"/> is
/// what a reader sees; this is what decides whether a sign-in button is offered beside it.
/// </param>
/// <param name="AppliedMarkers">
/// The Marker Rules an Apply Markers carries, or empty. What lets the queue name what it is doing —
/// "🍞 on #bread", or every rule — without asking the rules table, which may have moved on.
/// </param>
public sealed record ChangeDto(
    Guid Id,
    ChangeKind Kind,
    IReadOnlyList<string> SourceKeys,
    string TargetSpelling,
    IReadOnlyList<AppliedMarker> AppliedMarkers,
    ChangeState State,
    int PlannedTaskCount,
    int WrittenCount,
    int SkippedCount,
    int FailedCount,
    bool CancelRequested,
    DateTimeOffset RequestedAt,
    DateTimeOffset? CompletedAt,
    FailureCode FailureCode,
    string? FailureReason,
    Guid? UndoOfChangeId,
    bool CanUndo);

/// <summary>
/// The change queue as CONTEXT.md has described the Workbench since M0: the one Change in flight,
/// then the history that can still be undone.
/// </summary>
/// <param name="Active">The Change that is pending or running, or null. There is at most one.</param>
public sealed record ChangeQueueDto(ChangeDto? Active, IReadOnlyList<ChangeDto> History);

/// <summary>
/// What the store hands back about one Change: facts only. Whether undo is <em>offered</em> is a
/// rule, and rules are applied a layer up.
/// </summary>
public sealed record ChangeRecord(
    Guid Id,
    ChangeKind Kind,
    IReadOnlyList<string> SourceKeys,
    string TargetSpelling,
    IReadOnlyList<AppliedMarker> AppliedMarkers,
    ChangeState State,
    int PlannedTaskCount,
    int WrittenCount,
    int SkippedCount,
    int FailedCount,
    bool CancelRequested,
    DateTimeOffset RequestedAt,
    DateTimeOffset? CompletedAt,
    FailureCode FailureCode,
    string? FailureReason,
    Guid? UndoOfChangeId,
    bool HasBeenUndone);

/// <summary>A confirmed plan on its way to the database, with the scope already fixed.</summary>
public sealed record ConfirmedChangePlan(
    IReadOnlyList<string> SourceKeys,
    string TargetSpelling,
    ChangeKind Kind,
    IReadOnlyList<PlannedTask> Tasks,
    IReadOnlyList<AppliedMarker> AppliedMarkers);

/// <summary>
/// One task a plan covers. The task id stays on this side of the wire — the browser has no use
/// for it, and a plan the browser could hand back is a list of task ids somebody could edit.
/// </summary>
public sealed record PlannedTask(
    string TaskListId,
    string ListDisplayName,
    string GraphTaskId,
    string CurrentTitle,
    string NewTitle);

/// <summary>
/// What the planner worked out, before it is either shown or stored. Not a wire shape: the preview
/// projects it, the confirmation persists it, and neither reaches for what the other needs.
/// </summary>
/// <param name="AppliedMarkers">
/// Every Marker Rule the person held when the plan was drawn, for an Apply Markers — empty for the
/// three Hashtag Changes. Every rule rather than the ones in scope, because the write is the whole
/// block (ADR-0014).
/// </param>
/// <param name="Skips">Tasks the plan covers and will not write, with the reason, named in the preview.</param>
public sealed record PlannedChange(
    ChangeKind Kind,
    IReadOnlyList<string> SourceKeys,
    string TargetSpelling,
    IReadOnlyList<PlannedTask> Tasks,
    IReadOnlyList<ExcludedTaskList> ExcludedLists,
    IReadOnlyList<AppliedMarker> AppliedMarkers,
    IReadOnlyList<ChangePreviewSkipDto> Skips,
    ChangeMarkerRulesDto MarkerRules)
{
    /// <summary>Merges destroy a distinction the user made, and are the one operation that asks twice.</summary>
    public bool RequiresMergeConfirmation => Kind is ChangeKind.Merge;

    public ChangePreviewDto ToPreview() =>
        new(
            Kind,
            SourceKeys,
            TargetSpelling,
            [.. Tasks.Select(task => new ChangePreviewItemDto(
                task.TaskListId,
                task.ListDisplayName,
                task.CurrentTitle,
                task.NewTitle))],
            Tasks.Count,
            RequiresMergeConfirmation,
            ExcludedLists,
            Skips,
            MarkerRules);
}
