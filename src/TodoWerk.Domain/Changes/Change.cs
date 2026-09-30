using TodoWerk.Domain.Failures;
using TodoWerk.Domain.Hashtags;
using TodoWerk.SharedKernel;

namespace TodoWerk.Domain.Changes;

/// <summary>
/// One instruction to rewrite Hashtags in tasks: a set of source Hashtags and the one Spelling
/// they are all to take. Previewed, confirmed, queued, run and undone as a whole (ADR-0006).
/// <para>
/// A row rather than a queue in memory, for the same reason the scan queue is one: a Change over a
/// thousand tasks is minutes of sequential PATCHes, the process can be replaced mid-flight by a
/// deploy, and what it already wrote has to be findable afterwards — both to carry on and to undo.
/// </para>
/// </summary>
public sealed class Change : Entity<Guid>
{
    private Change(
        Guid id,
        string tenantId,
        string userId,
        IReadOnlyList<string> sourceKeys,
        string targetSpelling,
        ChangeKind kind,
        IReadOnlyList<AppliedMarker> appliedMarkers,
        int plannedTaskCount,
        Guid? undoOfChangeId,
        DateTimeOffset requestedAt)
        : base(id)
    {
        TenantId = tenantId;
        UserId = userId;
        SourceKeys = sourceKeys;
        TargetSpelling = targetSpelling;
        Kind = kind;
        AppliedMarkers = appliedMarkers;
        PlannedTaskCount = plannedTaskCount;
        UndoOfChangeId = undoOfChangeId;
        RequestedAt = requestedAt;
    }

    public string TenantId { get; private set; }

    /// <summary>
    /// The Entra ID object id. With the tenant id it forms the account identifier MSAL keys the
    /// token cache on, which is how a worker with no HTTP request in sight still gets a token to
    /// write this person's tasks (ADR-0002).
    /// </summary>
    public string UserId { get; private set; }

    /// <summary>
    /// The folded keys of the Hashtags being rewritten. Stored as written text rather than as a
    /// child table: nothing queries by them, and the set is read whole every time it is read.
    /// </summary>
    public IReadOnlyList<string> SourceKeys { get; private set; }

    /// <summary>
    /// The one Spelling every Occurrence of every source comes to take. Empty for an Apply
    /// Markers, which rewrites the block at the front of a title and no Hashtag at all — the
    /// column stays required rather than becoming nullable, because "no target" is a property of
    /// one kind and not a value any of the other three may be missing.
    /// </summary>
    public string TargetSpelling { get; private set; }

    /// <summary>
    /// The Marker Rules this Change carries, copied at confirmation. Empty when no rule is involved.
    /// <para>
    /// For an Apply Markers: every rule the person had, which is what gets written. Every rule and
    /// not only the ones in scope, because the write is the whole block — a Marker whose Hashtag is
    /// on the task belongs in rule order even when the Apply was started from a different rule's
    /// row, and that is what makes a one-rule Apply and an all-rules Apply write the same block for
    /// the same task (ADR-0014). <see cref="SourceKeys"/> is the scope; these are what goes into it.
    /// </para>
    /// <para>
    /// For a Rename or a Merge: at most one — the source rule that follows the Change to the target
    /// name, which for a Merge with several source rules is the one the user chose. It is carried
    /// on the row rather than worked out again at completion because the choice was made at
    /// confirmation and the rules table can have moved on by the time the run finishes.
    /// </para>
    /// </summary>
    public IReadOnlyList<AppliedMarker> AppliedMarkers { get; private set; }

    /// <summary>Derived from the shape at confirmation, never chosen by the user.</summary>
    public ChangeKind Kind { get; private set; }

    public ChangeState State { get; private set; } = ChangeState.Pending;

    /// <summary>
    /// How many tasks the confirmed plan covers. Fixed here as well as in the plan rows, so the
    /// queue can show "31 of 812" without counting a thousand rows for every poll.
    /// </summary>
    public int PlannedTaskCount { get; private set; }

    /// <summary>
    /// Set by someone watching the run. Not a state, because the row is still Running and still
    /// holding a lease: the runner reads this between tasks and stops after the one it is on.
    /// </summary>
    public bool CancelRequested { get; private set; }

    /// <summary>The Change this one reverses, or null. One level deep: an undo cannot be undone.</summary>
    public Guid? UndoOfChangeId { get; private set; }

    public DateTimeOffset RequestedAt { get; private set; }

    /// <summary>
    /// The lease. Written by the worker's claim rather than by a method here, because claiming has
    /// to be one conditional update — read, decide, write would let two processes run the same
    /// Change and PATCH every task twice.
    /// </summary>
    public DateTimeOffset? StartedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>What the client switches on. The sentence beside it is what a reader sees.</summary>
    public FailureCode FailureCode { get; private set; } = FailureCode.None;

    public string? FailureReason { get; private set; }

    /// <summary>Whether this Change reverses another one, which is what makes it undo.</summary>
    public bool IsUndo => UndoOfChangeId is not null;

    /// <summary>
    /// This Change is about Markers rather than about a Hashtag: it carries its own Markers, has no
    /// target Spelling, and writes the block at the front of a title instead of a Hashtag inside it.
    /// <para>
    /// One predicate rather than two comparisons everywhere the two kinds are alike, which is
    /// almost everywhere: the plan table, the journal, the queue, exclusivity, cancel, undo and the
    /// ceiling are shared. What the two do to a title is the only thing that differs, and the code
    /// that cares about that difference names the Kind itself.
    /// </para>
    /// </summary>
    public bool IsMarkerChange => Kind is ChangeKind.ApplyMarkers or ChangeKind.RemoveMarkers;

    /// <summary>
    /// The Hashtags whose tasks this Change covers. The same as <see cref="SourceKeys"/> for the
    /// three Hashtag Changes and for a one-rule Apply; for an all-rules Apply it is every rule the
    /// Change carries, which the row records as no keys at all.
    /// </summary>
    public IReadOnlyList<string> ScopeKeys => Kind is ChangeKind.ApplyMarkers && SourceKeys.Count == 0
        ? [.. AppliedMarkers.Where(marker => !marker.Abandoned).Select(marker => marker.Key)]
        : SourceKeys;

    /// <summary>Nothing more will happen to it on its own.</summary>
    public bool IsFinished => State
        is ChangeState.Completed
        or ChangeState.CompletedWithSkips
        or ChangeState.Failed
        or ChangeState.Cancelled;

    /// <summary>
    /// A confirmed Change, with its plan about to be written alongside it in the same save.
    /// </summary>
    /// <param name="carriedRule">
    /// The Marker Rule that follows this Change to the target name, as a single-element list, or
    /// empty. Never more than one: only one rule can end up on the Hashtag that survives.
    /// </param>
    public static Change Plan(
        string tenantId,
        string userId,
        IReadOnlyList<string> sourceKeys,
        string targetSpelling,
        ChangeKind kind,
        IReadOnlyList<AppliedMarker> carriedRule,
        int plannedTaskCount,
        DateTimeOffset requestedAt) =>
        // Version 7 rather than 4: the value is the clustered key, and the plan rows and journal
        // rows hang off it, so scattered inserts would cost on every write of every Change.
        new(
            Guid.CreateVersion7(),
            tenantId,
            userId,
            sourceKeys,
            targetSpelling,
            kind,
            carriedRule,
            plannedTaskCount,
            undoOfChangeId: null,
            requestedAt);

    /// <summary>
    /// A confirmed Apply Markers. Stated rather than classified: nothing about its shape could
    /// imply it, because it has no target Spelling to read (ADR-0014).
    /// </summary>
    /// <param name="scopeKeys">
    /// The Hashtags whose tasks this Apply covers, or empty for every rule it carries. Empty rather
    /// than fifty folded keys, because the scope is stored in a bounded column and "all of them" is
    /// both shorter to write down and still true after a rule is added or deleted. Read through
    /// <see cref="ScopeKeys"/>, never off the field.
    /// </param>
    /// <param name="appliedMarkers">Every rule the person had at confirmation, in their order.</param>
    public static Change PlanMarkers(
        string tenantId,
        string userId,
        IReadOnlyList<string> scopeKeys,
        IReadOnlyList<AppliedMarker> appliedMarkers,
        int plannedTaskCount,
        DateTimeOffset requestedAt) =>
        new(
            Guid.CreateVersion7(),
            tenantId,
            userId,
            scopeKeys,
            targetSpelling: string.Empty,
            ChangeKind.ApplyMarkers,
            appliedMarkers,
            plannedTaskCount,
            undoOfChangeId: null,
            requestedAt);

    /// <summary>
    /// A confirmed Remove Markers, the mirror of <see cref="PlanMarkers"/>: same plan, journal,
    /// queue, cancel and undo, opposite direction (ADR-0014).
    /// <para>
    /// It records no scope keys at all, unlike an Apply. Its scope is a set of Markers, carried on
    /// <paramref name="appliedMarkers"/> themselves — because the tasks a Remove is looking for are
    /// the ones that no longer carry the Hashtag, so there is no key that would select them, and a
    /// Marker a deleted rule left behind has no key to be selected by either.
    /// </para>
    /// </summary>
    /// <param name="appliedMarkers">
    /// Every Marker the person has, with the ones in scope marked removable. Every one of them and
    /// not only those, because the block is read whole: a Marker outside the scope still decides
    /// where the block ends, and a reader that stopped at it would leave the rest of the block
    /// unread.
    /// </param>
    public static Change PlanRemoval(
        string tenantId,
        string userId,
        IReadOnlyList<AppliedMarker> appliedMarkers,
        int plannedTaskCount,
        DateTimeOffset requestedAt) =>
        new(
            Guid.CreateVersion7(),
            tenantId,
            userId,
            sourceKeys: [],
            targetSpelling: string.Empty,
            ChangeKind.RemoveMarkers,
            appliedMarkers,
            plannedTaskCount,
            undoOfChangeId: null,
            requestedAt);

    /// <summary>
    /// The reverse of <paramref name="original"/>, queued like any other Change. It carries the
    /// original's sources and target so the queue can name what is being taken back; the plan rows
    /// come from the journal, not from the index, because only the journal knows what was written.
    /// </summary>
    public static Change Undo(Change original, int plannedTaskCount, DateTimeOffset requestedAt)
    {
        ArgumentNullException.ThrowIfNull(original);

        return new Change(
            Guid.CreateVersion7(),
            original.TenantId,
            original.UserId,
            original.SourceKeys,
            original.TargetSpelling,
            original.Kind,
            original.AppliedMarkers,
            plannedTaskCount,
            original.Id,
            requestedAt);
    }

    /// <summary>
    /// Which of the three operations this is, read off the shape rather than chosen (ADR-0006) —
    /// with one addition: a single source landing on a Hashtag that already
    /// exists is a Merge however it was reached, because the two become one and a distinction the
    /// user made is destroyed.
    /// </summary>
    public static ChangeKind Classify(
        IReadOnlyCollection<string> sourceKeys,
        string targetSpelling,
        bool targetHashtagExists)
    {
        ArgumentNullException.ThrowIfNull(sourceKeys);
        ArgumentNullException.ThrowIfNull(targetSpelling);

        if (sourceKeys.Count != 1)
        {
            return ChangeKind.Merge;
        }

        var targetKey = HashtagKey.Fold(targetSpelling);

        foreach (var source in sourceKeys)
        {
            if (string.Equals(source, targetKey, StringComparison.Ordinal))
            {
                return ChangeKind.NormaliseCasing;
            }
        }

        return targetHashtagExists ? ChangeKind.Merge : ChangeKind.Rename;
    }

    /// <summary>
    /// Asks the runner to stop after the task it is on. Idempotent, and allowed on a Pending
    /// Change too — that one never starts.
    /// </summary>
    public void RequestCancel() => CancelRequested = true;
}
