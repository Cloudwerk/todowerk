using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TodoWerk.Application.Abstractions.Authentication;
using TodoWerk.Application.Abstractions.Indexing;
using TodoWerk.Application.Abstractions.Markers;
using TodoWerk.Application.Changes;
using TodoWerk.Domain.Changes;
using TodoWerk.Domain.Failures;
using TodoWerk.Domain.Hashtags;
using TodoWerk.Domain.Indexing;
using TodoWerk.Infrastructure.Changes.Persistence;
using TodoWerk.Infrastructure.Graph;
using TodoWerk.Infrastructure.Persistence;
using TodoWerk.SharedKernel;

namespace TodoWerk.Infrastructure.Changes;

/// <summary>
/// Runs one claimed Change: for each planned task, re-read it, apply the rewrite to what came
/// back, PATCH it, and journal what was written.
/// <para>
/// Never called from a request. This is the first thing TodoWerk does that changes somebody's
/// data, and every property of it follows from one fact: <c>todoTask</c> has no ETag, so there is
/// no compare-and-swap to be had at any price (ADR-0006). Hence the re-read before every write,
/// the journal of what was actually read, and the admission that a preview count is advisory.
/// </para>
/// </summary>
internal sealed class ChangeRunner(
    TodoWerkDbContext context,
    ChangeQueue queue,
    ITodoTaskWriter tasks,
    IIndexScanScheduler scans,
    IMarkerRuleMover markers,
    TimeProvider timeProvider,
    ILogger<ChangeRunner> logger)
{
    private enum Step
    {
        Continue,
        CancelRequested,
        LeaseLost,
    }

    public async Task RunAsync(ClaimedChange claimed, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(claimed);

        var change = await context.Set<Change>()
            .FirstOrDefaultAsync(row => row.Id == claimed.Id, cancellationToken);

        if (change is null)
        {
            // Purged, or deleted under us. There is no row left to record anything against.
            logger.LogWarning("The claimed change {ChangeId} was gone before it ran.", claimed.Id);
            return;
        }

        var user = new IndexUser(change.TenantId, change.UserId);
        var written = new HashSet<string>(StringComparer.Ordinal);

        // Built once for the run, not once per task: a thousand tasks would otherwise sort and
        // hash the same fifty rules a thousand times, for a plan that is a value. Both directions
        // use it — what differs is which question they ask it.
        var markerRun = change.IsMarkerChange && !change.IsUndo
            ? new MarkerRun(MarkerPlan.From(change.AppliedMarkers), change.ScopeKeys.ToHashSet(StringComparer.Ordinal))
            : null;

        while (true)
        {
            // Thrown rather than tested in the loop guard. Falling out of the loop on shutdown
            // would return normally, the background service's release path — which only fires on
            // an exception — would never run, and the row would sit Running under a live lease for
            // the whole timeout: no Change and no scan for that user until it expired.
            cancellationToken.ThrowIfCancellationRequested();

            var step = await NextStepAsync(claimed, cancellationToken);

            if (step is Step.LeaseLost)
            {
                logger.LogWarning(
                    "The change {ChangeId} lost its lease mid-run; another process owns it now.",
                    claimed.Id);

                return;
            }

            if (step is Step.CancelRequested)
            {
                await FinishAsync(claimed, change, ChangeState.Cancelled, FailureCode.None, null, user, written, cancellationToken);
                return;
            }

            var item = await context.Set<ChangePlanItem>()
                .Where(row => row.ChangeId == change.Id && row.Status == ChangePlanItemStatus.Pending)
                .OrderBy(row => row.Sequence)
                .FirstOrDefaultAsync(cancellationToken);

            if (item is null)
            {
                await FinishAsync(
                    claimed,
                    change,
                    await OutcomeAsync(change.Id, cancellationToken),
                    FailureCode.None,
                    null,
                    user,
                    written,
                    cancellationToken);
                return;
            }

            var failure = await ApplyAsync(user, change, markerRun, item, written, cancellationToken);

            await context.SaveChangesAsync(cancellationToken);

            if (failure is not null)
            {
                logger.LogWarning(
                    "The change {ChangeId} stopped on task {Sequence} of {Planned}: {Reason}",
                    change.Id,
                    item.Sequence,
                    change.PlannedTaskCount,
                    failure.Description);

                await FinishAsync(
                    claimed,
                    change,
                    ChangeState.Failed,
                    GraphErrors.CodeFor(failure),
                    failure.Description,
                    user,
                    written,
                    cancellationToken);

                return;
            }

            // A thousand tasks is a thousand journal rows and a thousand plan items; left attached,
            // every later save walks every earlier one. The scan runner detaches for exactly this
            // reason. The Change itself stays tracked — the loop reads its sources on every task.
            DetachFinishedWork();
        }
    }

    private void DetachFinishedWork()
    {
        foreach (var entry in context.ChangeTracker.Entries<ChangeJournalEntry>().ToList())
        {
            entry.State = EntityState.Detached;
        }

        foreach (var entry in context.ChangeTracker.Entries<ChangePlanItem>().ToList())
        {
            entry.State = EntityState.Detached;
        }
    }

    /// <summary>
    /// Writes one task, or works out why it should not be. Returns the failure that stops the whole
    /// run, or null — a skip is not a failure.
    /// </summary>
    private async Task<Error?> ApplyAsync(
        IndexUser user,
        Change change,
        MarkerRun? markerRun,
        ChangePlanItem item,
        HashSet<string> writtenLists,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();

        var snapshot = await tasks.ReadTaskAsync(user, item.TaskListId, item.GraphTaskId, cancellationToken);

        if (snapshot.IsFailure)
        {
            return Refuse(item, snapshot.Error, ChangeSkips.TaskGone, now);
        }

        var current = snapshot.Value.Title;
        var next = NextTitle(change, markerRun, item, current);

        if (next.Skip is not null)
        {
            item.MarkSkipped(next.Skip, now);
            return null;
        }

        var title = next.Title!;

        // Microsoft To Do keeps 255 characters and silently drops the rest, answering success
        // either way. Writing a longer title would
        // leave a Hashtag cut in half in somebody's task and journal a title that never existed
        // there — so undo would compare, fail to match, and skip, leaving the damage permanent.
        // Leaving the task alone and saying why is the only outcome that keeps undo honest.
        if (title.Length > TodoTaskLimits.TitleLength)
        {
            item.MarkSkipped(ChangeSkips.TitleTooLongForToDo, now);
            return null;
        }

        // The journal has to be able to hold both titles or undo is a promise TodoWerk cannot
        // keep. The rewrite is already bounded above; this is about what was read back, which
        // predates any bound TodoWerk enforces and could have been written by anything.
        if (current.Length > StorageConventions.DisplayTextLength)
        {
            item.MarkSkipped(ChangeSkips.TitleTooLongToRecord, now);
            return null;
        }

        // Journalled before the PATCH, not after, and saved before the request goes out.
        //
        // Something has to be durable across a crash in between, and the two orders fail
        // differently. Recording first and crashing leaves a journal row for a write that may not
        // have happened — undo compares the current title against what was recorded, finds it does
        // not match, and leaves the task alone. Writing first and crashing leaves a rewritten task
        // with no record: the resumed run re-reads it, finds no source Hashtag, calls it skipped,
        // and undo omits a task TodoWerk really did change. The first is a count that is one too
        // high; the second is a promise quietly broken.
        //
        // The title recorded is the one read immediately before the write, never the one the
        // preview showed — that is what makes undo restore what was really there (ADR-0006).
        var journal = ChangeJournalEntry.Record(
            user.TenantId,
            user.UserId,
            change.Id,
            item.TaskListId,
            item.GraphTaskId,
            current,
            title,
            now);

        context.Add(journal);
        item.MarkWritten(now);

        await context.SaveChangesAsync(cancellationToken);

        var write = await tasks.WriteTitleAsync(user, item.TaskListId, item.GraphTaskId, title, cancellationToken);

        if (write.IsFailure)
        {
            // The write did not happen, so neither did the thing the journal claims. Taken back
            // here where it is known, rather than left for undo to notice by comparing titles.
            context.Remove(journal);

            return Refuse(item, write.Error, ChangeSkips.TaskGone, now);
        }

        writtenLists.Add(item.TaskListId);

        return null;
    }

    /// <summary>
    /// A task that is gone is a skip; anything else is the run's failure, because the next task
    /// would meet the same thing.
    /// </summary>
    private static Error? Refuse(ChangePlanItem item, Error error, string goneReason, DateTimeOffset now)
    {
        if (error == GraphErrors.NotFound)
        {
            item.MarkSkipped(goneReason, now);
            return null;
        }

        item.MarkFailed(error.Description, now);

        return error;
    }

    /// <summary>
    /// What this task should say next, or why it should be left alone. The two directions differ
    /// here and nowhere else: a forward Change re-applies its rewrite to whatever Graph returned,
    /// while an undo restores a stored string and only if the task still says exactly what
    /// TodoWerk wrote.
    /// </summary>
    private static (string? Title, string? Skip) NextTitle(
        Change change,
        MarkerRun? markerRun,
        ChangePlanItem item,
        string current)
    {
        if (change.IsUndo)
        {
            return string.Equals(current, item.PreviewedTitle, StringComparison.Ordinal)
                ? (item.PreviewedNewTitle, null)
                : (null, ChangeSkips.EditedSince);
        }

        if (markerRun is not null)
        {
            return change.Kind is ChangeKind.RemoveMarkers
                ? NextRemovalTitle(markerRun, current)
                : NextMarkerTitle(markerRun, current);
        }

        var rewritten = HashtagRewriter.Rewrite(current, change.SourceKeys, change.TargetSpelling);

        if (rewritten is null)
        {
            return (null, ChangeSkips.TagGone);
        }

        return string.Equals(rewritten, current, StringComparison.Ordinal)
            ? (null, ChangeSkips.AlreadyRight)
            : (rewritten, null);
    }

    /// <summary>
    /// What an Apply Markers should write, or why this task should be left alone.
    /// <para>
    /// Computed from the title Graph just returned and from the Change's own Markers, never from
    /// the live rules table (ADR-0014) — so a rule the user edits while this runs takes effect at
    /// the next Apply, and the journal behind undo stands whatever happens to the rules afterwards.
    /// </para>
    /// </summary>
    private static (string? Title, string? Skip) NextMarkerTitle(MarkerRun markerRun, string current)
    {
        var present = MarkerPlan.HashtagKeysIn(current);

        // The scope, re-asked of the title as it is now: this task was planned because it carried a
        // Hashtag one of the rules in scope is about, and if it no longer does then the thing the
        // user asked for is not there to do.
        if (!markerRun.Scope.Overlaps(present))
        {
            return (null, ChangeSkips.TagGone);
        }

        // The Change's own Markers, through the same code the preview was computed with — which is
        // what stops a plan that showed one block and a run that writes another.
        var rewritten = markerRun.Plan.RewriteFor(current, present);

        return rewritten is null ? (null, ChangeSkips.AlreadyRight) : (rewritten, null);
    }

    /// <summary>
    /// What a Remove Markers should write, or why this task should be left alone.
    /// <para>
    /// Computed from the title Graph just returned and from the Change's own Markers, exactly as an
    /// Apply is — the same plan, asked the opposite question. There is no scope to re-ask of the
    /// title the way an Apply re-asks its Hashtags: what this run is scoped to is a set of Markers,
    /// and whether any of them is still in this task's block is the question the plan answers.
    /// </para>
    /// </summary>
    private static (string? Title, string? Skip) NextRemovalTitle(MarkerRun markerRun, string current)
    {
        var removed = markerRun.Plan.RemoveFor(current);

        if (removed is null)
        {
            // Nothing of this run's left at the front: applied since, removed since, or the Hashtag
            // put back — all of them "there is nothing here to do", which is a skip and not a
            // failure.
            return (null, ChangeSkips.MarkerGone);
        }

        // A task whose title is nothing but the Markers being removed. Left alone rather than left
        // with no title, which is not a task Microsoft To Do has. The preview names it too; this is
        // the same question asked of the title that is actually being written.
        return removed.Length == 0 ? (null, ChangeSkips.TitleWouldBeEmpty) : (removed, null);
    }

    /// <summary>The Change's own Markers and scope, read once for the whole run.</summary>
    private sealed record MarkerRun(MarkerPlan Plan, HashSet<string> Scope);

    /// <summary>
    /// Renews the lease and asks whether somebody has pressed Cancel — once per task, because both
    /// answers go stale in exactly one task's time, and a task is a Graph round trip against which
    /// two small statements do not register.
    /// </summary>
    private async Task<Step> NextStepAsync(ClaimedChange claimed, CancellationToken cancellationToken)
    {
        if (!await queue.RenewLeaseAsync(claimed, cancellationToken))
        {
            return Step.LeaseLost;
        }

        return await queue.IsCancelRequestedAsync(claimed, cancellationToken)
            ? Step.CancelRequested
            : Step.Continue;
    }

    /// <summary>
    /// Completed, or completed with skips. Distinguished because the count the user confirmed and
    /// the count that was written differ, and saying so is why preview counts are called advisory.
    /// </summary>
    private async Task<ChangeState> OutcomeAsync(Guid changeId, CancellationToken cancellationToken) =>
        await context.Set<ChangePlanItem>()
            .AnyAsync(item => item.ChangeId == changeId && item.Status == ChangePlanItemStatus.Skipped, cancellationToken)
            ? ChangeState.CompletedWithSkips
            : ChangeState.Completed;

    private async Task FinishAsync(
        ClaimedChange claimed,
        Change change,
        ChangeState state,
        FailureCode failureCode,
        string? failureReason,
        IndexUser user,
        HashSet<string> writtenLists,
        CancellationToken cancellationToken)
    {
        // What the Change does to Marker Rules happens before the row is marked finished, while the
        // lease is still held. Both operations are idempotent, so the order buys crash safety for
        // nothing: a process replaced between them leaves the row Running under an expiring lease,
        // the next claimant finds no task left to do, and it reaches this point again. The other
        // order would leave a completed Rename whose rule quietly stayed on the old name, with no
        // row left for anything to pick up.
        await CarryRulesAsync(change, user, cancellationToken);
        await RecordWrittenMarkersAsync(change, user, cancellationToken);
        await SettleRemovedMarkersAsync(change, user, cancellationToken);

        if (!await queue.FinishAsync(claimed, state, failureCode, failureReason, cancellationToken))
        {
            logger.LogWarning(
                "The change {ChangeId} finished its work but had already lost its lease.",
                claimed.Id);

            return;
        }

        if (writtenLists.Count == 0)
        {
            return;
        }

        // The index catches up through Graph rather than through index writes from this module:
        // the boundary is worth more than the latency it costs, and Graph stays the single source
        // of truth (ADR-0006). Until that scan lands the Workbench is behind by one, and says so.
        foreach (var taskListId in writtenLists)
        {
            await scans.RequestScanAsync(user, IndexScanMode.Delta, taskListId, cancellationToken);
        }
    }

    /// <summary>
    /// Tells the rules an Apply covered what their tasks now carry (ADR-0014), one rule at a time.
    /// <para>
    /// A rule is told it wrote its Marker only when every task of its own that the plan covered was
    /// reached — written, or passed over because the block already read right or the task was
    /// gone. A task still waiting, one that failed, one too long to take the block, and one whose
    /// tag had gone by the time it was read all still carry whatever they carried, the retired
    /// Marker among it, so the rule keeps that Marker armed for the next Apply. Per rule and not
    /// per run, because an all-rules Apply that reached every #coffee and no #bread has written
    /// nothing about bread. Which tasks are a rule's is read off the plan rows' titles through the
    /// same grammar that planned them.
    /// </para>
    /// <para>
    /// An undo that restored anything of a rule's put the retired Marker back in those titles, so
    /// that is what the rule is told, whatever state the undo ended in — a partial undo leaves
    /// some titles with the old Marker, and those are the ones the next Apply has to reach.
    /// </para>
    /// </summary>
    private async Task RecordWrittenMarkersAsync(Change change, IndexUser user, CancellationToken cancellationToken)
    {
        if (change.Kind is not ChangeKind.ApplyMarkers)
        {
            return;
        }

        var items = await context.Set<ChangePlanItem>()
            .AsNoTracking()
            .Where(item => item.ChangeId == change.Id)
            .Select(item => new { item.Status, item.Outcome, item.PreviewedTitle })
            .ToListAsync(cancellationToken);

        // The keys whose tasks were all reached, and the keys any of whose tasks was restored.
        var covered = new HashSet<string>(StringComparer.Ordinal);
        var unreached = new HashSet<string>(StringComparer.Ordinal);
        var restored = new HashSet<string>(StringComparer.Ordinal);

        foreach (var item in items)
        {
            var keys = MarkerPlan.HashtagKeysIn(item.PreviewedTitle);

            covered.UnionWith(keys);

            if (item.Status == ChangePlanItemStatus.Written)
            {
                restored.UnionWith(keys);
            }
            else if (!(item.Status == ChangePlanItemStatus.Skipped
                && item.Outcome is ChangeSkips.AlreadyRight or ChangeSkips.TaskGone))
            {
                unreached.UnionWith(keys);
            }
        }

        var scope = change.ScopeKeys.ToHashSet(StringComparer.Ordinal);
        var written = new List<WrittenMarker>();

        foreach (var applied in change.AppliedMarkers)
        {
            if (applied.Abandoned || !scope.Contains(applied.Key))
            {
                continue;
            }

            if (change.IsUndo)
            {
                if (restored.Contains(applied.Key) && applied.RetiredMarker is { } previous)
                {
                    written.Add(new WrittenMarker(applied.Key, Marker.Restore(previous)));
                }
            }
            else if (covered.Contains(applied.Key) && !unreached.Contains(applied.Key))
            {
                written.Add(new WrittenMarker(applied.Key, Marker.Restore(applied.Marker)));
            }
        }

        if (written.Count > 0)
        {
            await markers.RecordWrittenMarkersAsync(user, written, cancellationToken);
        }
    }

    /// <summary>
    /// What a finished Remove Markers does to the rows that were keeping its Markers known
    /// (ADR-0014). No title is written here.
    /// <para>
    /// A row exists for one reason: the emoji is at the front of real titles and the block reader
    /// has to go on recognising it. When a Remove has taken a Marker out of every block that
    /// carried one, the reason is gone and the row goes — which is the thing that had never
    /// happened for somebody who was still here.
    /// </para>
    /// <para>
    /// "Every block" is asked the way <see cref="RecordWrittenMarkersAsync"/> asks it: a Marker is
    /// settled only when every planned task whose block held it was reached — written, or passed
    /// over because it was already gone or the task was. One still waiting, one that failed, and
    /// one left alone because its title was nothing but the Markers being removed all still carry
    /// it, so the row stays. A cancelled or failed run therefore drops nothing.
    /// </para>
    /// <para>
    /// And the other direction. Undo is offered for thirty days, so an undone Remove puts these
    /// emoji back into titles the product has stopped recognising them in; every Marker it restored
    /// gets a row again, or the next Apply would read the block as ending in front of it and write
    /// a second block. Over-approximating there is harmless — it is the state we were in before.
    /// </para>
    /// </summary>
    private async Task SettleRemovedMarkersAsync(Change change, IndexUser user, CancellationToken cancellationToken)
    {
        if (change.Kind is not ChangeKind.RemoveMarkers)
        {
            return;
        }

        var plan = MarkerPlan.From(change.AppliedMarkers);

        if (plan.Removable.Count == 0)
        {
            return;
        }

        var items = await context.Set<ChangePlanItem>()
            .AsNoTracking()
            .Where(item => item.ChangeId == change.Id)
            .Select(item => new { item.Status, item.Outcome, item.PreviewedTitle, item.PreviewedNewTitle })
            .ToListAsync(cancellationToken);

        var carried = new HashSet<Marker>();
        var unreached = new HashSet<Marker>();
        var restored = new HashSet<Marker>();

        foreach (var item in items)
        {
            // The title that holds the Markers this run is about, which is not the same end of the
            // pair in the two directions: a Remove starts from the title that carries them, and its
            // undo is putting them back, so what it carries is the title it writes.
            var block = MarkerBlock.Read(
                change.IsUndo ? item.PreviewedNewTitle : item.PreviewedTitle,
                plan.EveryMarker);

            foreach (var marker in block.Markers)
            {
                if (!plan.Removable.Contains(marker))
                {
                    continue;
                }

                carried.Add(marker);

                if (item.Status == ChangePlanItemStatus.Written)
                {
                    restored.Add(marker);
                }
                else if (!(item.Status == ChangePlanItemStatus.Skipped
                    && item.Outcome is ChangeSkips.MarkerGone or ChangeSkips.TaskGone))
                {
                    unreached.Add(marker);
                }
            }
        }

        if (change.IsUndo)
        {
            var again = change.AppliedMarkers
                .Where(applied => applied.Removable && restored.Contains(Marker.Restore(applied.Marker)))
                .Select(applied => new AbandonedMarkerSnapshot(
                    applied.Key,
                    applied.Spelling,
                    Marker.Restore(applied.Marker)))
                .ToList();

            if (again.Count > 0)
            {
                await markers.RememberRemovedMarkersAsync(user, again, cancellationToken);
            }

            return;
        }

        // Only Markers whose plan knew of every task carrying them: one passed over for having no
        // title but its Markers, or sitting in a list nobody has read end to end, is still out there
        // whatever this run reached, and a row dropped over it would stop the block reader in front
        // of an emoji in a real title. The planner settled that; this run cannot see it.
        var reaching = MarkerPlan.Reaching(change.AppliedMarkers);

        var gone = carried
            .Where(marker => reaching.Contains(marker) && !unreached.Contains(marker))
            .ToList();

        if (gone.Count > 0)
        {
            await markers.ForgetRemovedMarkersAsync(user, gone, cancellationToken);
        }
    }

    private Task<bool> AnyAsync(Guid changeId, ChangePlanItemStatus status, CancellationToken cancellationToken) =>
        context.Set<ChangePlanItem>()
            .AnyAsync(item => item.ChangeId == changeId && item.Status == status, cancellationToken);

    /// <summary>
    /// What a finished Hashtag Change does to the Marker Rules it involves (ADR-0014). No title is
    /// written here: a rule follows the Change to the new name, and the rules that lost are deleted.
    /// <para>
    /// Only for a Change that actually wrote something. A Rename that wrote nothing renamed nothing
    /// — every task it planned had moved on — and moving the rule after it would leave the Marker on
    /// a Hashtag that no task carries while the tasks still carry the old one.
    /// </para>
    /// </summary>
    private async Task CarryRulesAsync(Change change, IndexUser user, CancellationToken cancellationToken)
    {
        // Normalise Casing moves nothing: the key does not change, so the rule is already where it
        // is going to be. Every state this is reached in is terminal, so what decides it is not how
        // the run ended but whether it wrote anything.
        if (change.Kind is ChangeKind.NormaliseCasing || change.IsMarkerChange)
        {
            return;
        }

        if (!await AnyAsync(change.Id, ChangePlanItemStatus.Written, cancellationToken))
        {
            return;
        }

        var targetKey = HashtagKey.Fold(change.TargetSpelling);
        var carried = change.AppliedMarkers.Count > 0 ? change.AppliedMarkers[0] : null;

        if (change.IsUndo)
        {
            // Undoing a Rename puts the rule back on the name it came from, under the same
            // condition. A Merge's losing rules are not restored: they were deleted, and undo is a
            // promise about titles.
            if (change.Kind is ChangeKind.Rename && carried is not null)
            {
                await markers.CarryRuleAsync(
                    user,
                    targetKey,
                    carried.Key,
                    carried.Spelling,
                    Marker.Restore(carried.Marker),
                    cancellationToken);
            }

            return;
        }

        var carry = carried is null
            ? MarkerRuleCarry.TargetWins
            : await markers.CarryRuleAsync(
                user,
                carried.Key,
                targetKey,
                change.TargetSpelling,
                Marker.Restore(carried.Marker),
                cancellationToken);

        // Everything that did not survive. The target's own key is never in it — a Change cannot
        // delete the rule on the Hashtag it is folding everything into — and neither is a source
        // key this run found empty.
        //
        // That second exclusion matters after a crash between here and FinishAsync: the resumed run
        // finds the rule already carried and therefore nothing on the old key, and treating that
        // key as a loser would soft-delete whatever stands there — possibly a rule the person
        // created at the old name in the meantime. An empty source key holds nothing this Change
        // is entitled to delete, whichever way it came to be empty; a target that won is the one
        // case where the source's rule really did lose.
        var losers = change.SourceKeys
            .Where(key => !string.Equals(key, targetKey, StringComparison.Ordinal))
            .Where(key => carried is null
                || carry is MarkerRuleCarry.TargetWins
                || !string.Equals(key, carried.Key, StringComparison.Ordinal))
            .ToList();

        await markers.DeleteRulesAsync(user, losers, cancellationToken);
    }
}
