using Microsoft.Extensions.Options;
using TodoWerk.Application.Abstractions.Authentication;
using TodoWerk.Application.Abstractions.Indexing;
using TodoWerk.Application.Abstractions.Markers;
using TodoWerk.Domain.Changes;
using TodoWerk.Domain.Hashtags;
using TodoWerk.SharedKernel;

namespace TodoWerk.Application.Changes;

/// <summary>
/// Turns "these Hashtags, that Spelling" into the exact set of tasks a Change would rewrite —
/// or into the reason it will not.
/// <para>
/// One class for both the preview and the confirmation, because they must not be able to disagree.
/// The confirmation does not trust the preview it was shown: it plans again, server-side, and
/// persists what <em>it</em> computed. A plan posted back by the browser would be a list of task
/// ids somebody could edit.
/// </para>
/// </summary>
internal sealed class ChangePlanner(
    ITaggedTaskReader tasks,
    IMarkerRuleReader markerRules,
    IOptions<ChangeOptions> options)
{
    public async Task<Result<PlannedChange>> PlanAsync(
        IndexUser user,
        IReadOnlyList<string>? sourceKeys,
        string? targetSpelling,
        CancellationToken cancellationToken)
    {
        var settings = options.Value;

        if (sourceKeys is null || sourceKeys.Count == 0)
        {
            return Result.Failure<PlannedChange>(ChangeErrors.NoSources);
        }

        if (sourceKeys.Count > settings.MaxSourcesPerChange)
        {
            return Result.Failure<PlannedChange>(ChangeErrors.TooManySources(settings.MaxSourcesPerChange));
        }

        if (!ChangeTarget.RoundTrips(targetSpelling))
        {
            return Result.Failure<PlannedChange>(ChangeErrors.InvalidTarget);
        }

        var target = targetSpelling!;

        // Folded here rather than trusted from the caller: the client sends the keys it read off
        // the inventory, and a key that is not the fold of anything would silently match nothing.
        var keys = Distinct(sourceKeys);

        if (keys.Count == 0)
        {
            return Result.Failure<PlannedChange>(ChangeErrors.NoSources);
        }

        var targetKey = HashtagKey.Fold(target);

        // Whether the target names a Hashtag that already exists is what turns a rename into a
        // Merge — unless the target is one of the sources, in which case this is a casing
        // clean-up and the Hashtag it "already exists as" is the one being cleaned up.
        var targetExists = keys.Contains(targetKey)
            || await tasks.HashtagExistsAsync(user, targetKey, cancellationToken);

        var kind = Change.Classify(keys, target, targetExists);

        var matching = await tasks.ReadTasksTaggedAsync(
            user,
            keys,
            target,
            settings.MaxTasksPerChange,
            cancellationToken);

        if (matching.TotalCount > settings.MaxTasksPerChange)
        {
            return Result.Failure<PlannedChange>(
                ChangeErrors.PlanTooLarge(matching.TotalCount, settings.MaxTasksPerChange));
        }

        var planned = new List<PlannedTask>(matching.Tasks.Count);

        foreach (var task in matching.Tasks)
        {
            var rewritten = HashtagRewriter.Rewrite(task.Title, keys, target);

            // The query already excluded tasks that read the way the Change wants, so this is
            // belt and braces — but the two decisions are made by different engines under
            // different rules, and a pointless PATCH against somebody's task is not the place to
            // find out they disagreed.
            if (rewritten is null || string.Equals(rewritten, task.Title, StringComparison.Ordinal))
            {
                continue;
            }

            planned.Add(new PlannedTask(
                task.TaskListId,
                task.ListDisplayName,
                task.GraphTaskId,
                task.Title,
                rewritten));
        }

        if (planned.Count == 0)
        {
            return Result.Failure<PlannedChange>(ChangeErrors.NothingToChange);
        }

        return Result.Success(new PlannedChange(
            kind,
            keys,
            target,
            planned,
            matching.ExcludedLists,
            AppliedMarkers: [],
            Skips: [],
            MarkerRulesFor(await markerRules.ReadRulesAsync(user, cancellationToken), kind, keys, targetKey)));
    }

    /// <summary>
    /// What this Hashtag Change would do to the Marker Rules involved (ADR-0014).
    /// <para>
    /// A rule belongs to a Hashtag, so folding one Hashtag into another has to say what becomes of
    /// its Marker. The target's own rule wins, because the target is the Hashtag that survives; a
    /// single source rule follows the Change to the new name; and several source rules with no
    /// target rule is the one case nobody but the user can settle.
    /// </para>
    /// </summary>
    private static ChangeMarkerRulesDto MarkerRulesFor(
        IReadOnlyList<MarkerRuleSnapshot> rules,
        ChangeKind kind,
        IReadOnlyCollection<string> sourceKeys,
        string targetKey)
    {
        // Normalise Casing moves nothing and deletes nothing: the key does not change, so the rule
        // is already on the Hashtag it will still be on afterwards.
        if (kind is ChangeKind.NormaliseCasing || rules.Count == 0)
        {
            return ChangeMarkerRulesDto.None;
        }

        var target = rules.FirstOrDefault(rule => string.Equals(rule.Key, targetKey, StringComparison.Ordinal));

        var sources = rules
            .Where(rule => sourceKeys.Contains(rule.Key)
                && !string.Equals(rule.Key, targetKey, StringComparison.Ordinal))
            .Select(Describe)
            .ToList();

        if (sources.Count == 0 && target is null)
        {
            return ChangeMarkerRulesDto.None;
        }

        if (target is not null)
        {
            // The target's own rule wins, and every source rule goes. Nothing to choose.
            return new ChangeMarkerRulesDto(sources, Describe(target), target.Marker.Text, false);
        }

        return sources.Count == 1
            ? new ChangeMarkerRulesDto(sources, null, sources[0].Marker, false)
            : new ChangeMarkerRulesDto(sources, null, null, true);
    }

    private static ChangeMarkerRuleDto Describe(MarkerRuleSnapshot rule) =>
        new(rule.Key, rule.Spelling, rule.Marker.Text);

    /// <summary>
    /// Turns "these rules, those tasks" into the exact set of tasks an Apply Markers would rewrite.
    /// <para>
    /// The scope and the write are two different things, and this is where they part. The scope is
    /// <paramref name="markerRuleKey"/> — one rule's Hashtag, or all of them — and it decides which
    /// tasks are covered. What is written into each of those tasks is the whole block, computed
    /// from every rule the person has, so a one-rule Apply and an all-rules Apply put the same
    /// block on the same task (ADR-0014).
    /// </para>
    /// </summary>
    public async Task<Result<PlannedChange>> PlanMarkersAsync(
        IndexUser user,
        string? markerRuleKey,
        CancellationToken cancellationToken)
    {
        var settings = options.Value;

        var rules = await markerRules.ReadRulesAsync(user, cancellationToken);

        if (rules.Count == 0)
        {
            return Result.Failure<PlannedChange>(ChangeErrors.NoMarkerRules);
        }

        // Which tasks are covered, and — separately — what the Change records about its own scope.
        // They differ for an all-rules Apply on purpose: the query needs every key, and the row
        // stores none, because "all of them" is a shorter and more durable thing to write down than
        // fifty folded keys in a bounded column. The Change carries the rules themselves either way.
        List<string> scopeKeys;
        List<string> recordedScope;

        if (markerRuleKey is null)
        {
            scopeKeys = [.. rules.Select(rule => rule.Key)];
            recordedScope = [];
        }
        else
        {
            // Folded here as well, although the client reads the key off a rule that is already
            // folded: a key that is not the fold of anything would silently match no rule, and the
            // answer for that is a named refusal rather than an empty plan. Bounded first, so a key
            // no rule could have is refused without being normalised.
            var wanted = markerRuleKey.Length <= HashtagKey.MaxLength ? HashtagKey.Fold(markerRuleKey) : null;
            var one = wanted is null
                ? null
                : rules.FirstOrDefault(rule => string.Equals(rule.Key, wanted, StringComparison.Ordinal));

            if (one is null)
            {
                return Result.Failure<PlannedChange>(ChangeErrors.MarkerRuleNotFound);
            }

            scopeKeys = [one.Key];
            recordedScope = [one.Key];
        }

        // Every Spelling qualifies, so nothing is excluded for already reading the right way: what
        // an Apply asks of a title is about its block, not about how a tag is spelled, and the
        // rewrite answers that per task below.
        var matching = await tasks.ReadTasksTaggedAsync(
            user,
            scopeKeys,
            alreadySpelled: string.Empty,
            settings.MaxTasksPerChange,
            cancellationToken);

        // Measured against every task carrying a Hashtag in scope, including those already marked
        // the right way — the reader can exclude a Spelling in SQL but not a block, which would
        // take the whole grammar with it. An all-rules Apply past the ceiling is refused with the
        // count, and a per-rule Apply is the way through it (ADR-0014).
        if (matching.TotalCount > settings.MaxTasksPerChange)
        {
            return Result.Failure<PlannedChange>(
                ChangeErrors.PlanTooLarge(matching.TotalCount, settings.MaxTasksPerChange));
        }

        // The Markers this Change will carry, built before anything is planned from them: the plan
        // the preview shows and the write the runner makes are then the same code over the same
        // list, which is the only way the two cannot come apart.
        var applied = rules
            .Select(rule => new AppliedMarker(
                rule.Key,
                rule.Spelling,
                rule.Marker.Text,
                rule.RetiredMarker?.Text,
                rule.Position))
            .ToList();

        // And the Markers of the rules this person has deleted, so the block reader keeps reading
        // them as part of a block: deleting wrote nothing, so they are still out there (ADR-0014).
        foreach (var marker in await markerRules.ReadAbandonedMarkersAsync(user, cancellationToken))
        {
            applied.Add(new AppliedMarker(
                marker.Key,
                marker.Spelling,
                marker.Marker.Text,
                RetiredMarker: null,
                Position: int.MaxValue,
                Abandoned: true));
        }

        var plan = MarkerPlan.From(applied);

        var planned = new List<PlannedTask>(matching.Tasks.Count);
        var skips = new List<ChangePreviewSkipDto>();

        foreach (var task in matching.Tasks)
        {
            var rewritten = plan.RewriteFor(task.Title);

            if (rewritten is null)
            {
                continue;
            }

            // Named in the preview rather than discovered in the outcome: a block is added to the
            // front of a title somebody else wrote, and how close that title already sits to the
            // limit is not theirs to guess. The runner asks again of the title Graph returns, which
            // is the one that is actually written.
            if (rewritten.Length > TodoTaskLimits.TitleLength)
            {
                skips.Add(new ChangePreviewSkipDto(
                    task.TaskListId,
                    task.ListDisplayName,
                    task.Title,
                    ChangeSkips.TitleTooLongForToDo));

                continue;
            }

            planned.Add(new PlannedTask(
                task.TaskListId,
                task.ListDisplayName,
                task.GraphTaskId,
                task.Title,
                rewritten));
        }

        if (planned.Count == 0)
        {
            // "No task needs this change" is false when every task that needed it was passed over
            // for being too long, and a preview that said it would send somebody looking for a
            // problem in their rules rather than in their titles.
            return Result.Failure<PlannedChange>(
                skips.Count == 0
                    ? ChangeErrors.NothingToChange
                    : ChangeErrors.EveryTaskWouldBeTooLong(skips.Count));
        }

        return Result.Success(new PlannedChange(
            ChangeKind.ApplyMarkers,
            recordedScope,
            TargetSpelling: string.Empty,
            planned,
            matching.ExcludedLists,
            applied,
            skips,
            ChangeMarkerRulesDto.None));
    }

    /// <summary>
    /// Turns "this Marker, or all of them" into the exact set of tasks a Remove Markers would
    /// rewrite (ADR-0014).
    /// <para>
    /// The scope is a Marker rather than a Hashtag, which is the one structural difference from an
    /// Apply. A stale Marker is stale precisely because its Hashtag has left the task, so no key
    /// selects the tasks that carry one — and a Marker a deleted rule left behind has no key at
    /// all. Tasks are found by what is written in their titles instead, and whether a match is
    /// really in a block is decided here, per task, by the same plan the runner will use.
    /// </para>
    /// </summary>
    /// <param name="marker">The one Marker to remove, or null for every stale Marker the person has.</param>
    public async Task<Result<PlannedChange>> PlanRemovalAsync(
        IndexUser user,
        string? marker,
        CancellationToken cancellationToken)
    {
        var settings = options.Value;

        var rules = await markerRules.ReadRulesAsync(user, cancellationToken);
        var abandoned = await markerRules.ReadAbandonedMarkersAsync(user, cancellationToken);

        if (rules.Count == 0 && abandoned.Count == 0)
        {
            return Result.Failure<PlannedChange>(ChangeErrors.NoMarkersToRemove);
        }

        // Which Markers this run may take. Null is every one of them, which is the only scope that
        // can reach a Marker no rule is left to name.
        Marker? scoped = null;

        if (marker is not null)
        {
            if (!Marker.TryCreate(marker, out var wanted))
            {
                return Result.Failure<PlannedChange>(ChangeErrors.MarkerNotFound);
            }

            scoped = wanted;
        }

        var applied = RemovableMarkers(rules, abandoned, scoped);
        var plan = MarkerPlan.From(applied);

        if (plan.Removable.Count == 0)
        {
            // Every Marker the person has was read, and the one named is not among them.
            return Result.Failure<PlannedChange>(ChangeErrors.MarkerNotFound);
        }

        // Every Marker the person has, not only the ones in scope: a title carrying any of them may
        // hold one that is in scope behind it, and the block has to be read whole to find out.
        //
        // Narrowed on the first scalar of each, exactly as the stale count beside the button is:
        // two presentations of one emoji are one Marker and two byte sequences in a title, so a
        // search for the Marker's own text would miss every title typed in the other — and the
        // count would offer a Change that then reported nothing to do.
        var needles = plan.EveryMarker
            .Select(each => Marker.FirstScalarOf(each.Text))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var matching = await tasks.ReadTasksTitledWithAnyAsync(
            user,
            needles,
            settings.MaxTasksPerChange,
            cancellationToken);

        // Measured against every task whose title holds any Marker, the same way an Apply's ceiling
        // is measured against every tagged task: whether a Marker is in a block, and whether it is
        // stale, are both grammar the database cannot run. Removing one Marker at a time is the way
        // through it, as a per-rule Apply is for the other direction (ADR-0014).
        if (matching.TotalCount > settings.MaxTasksPerChange)
        {
            return Result.Failure<PlannedChange>(
                ChangeErrors.PlanTooLarge(matching.TotalCount, settings.MaxTasksPerChange));
        }

        var planned = new List<PlannedTask>(matching.Tasks.Count);
        var skips = new List<ChangePreviewSkipDto>();

        foreach (var task in matching.Tasks)
        {
            var rewritten = plan.RemoveFor(task.Title);

            if (rewritten is null)
            {
                continue;
            }

            // A title that is nothing but the Markers being removed. Named in the preview rather
            // than discovered in the outcome, and left alone either way: Microsoft To Do has no
            // task without a title, and a Change is not the place to invent one.
            if (rewritten.Length == 0)
            {
                skips.Add(new ChangePreviewSkipDto(
                    task.TaskListId,
                    task.ListDisplayName,
                    task.Title,
                    ChangeSkips.TitleWouldBeEmpty));

                continue;
            }

            planned.Add(new PlannedTask(
                task.TaskListId,
                task.ListDisplayName,
                task.GraphTaskId,
                task.Title,
                rewritten));
        }

        if (planned.Count == 0)
        {
            return Result.Failure<PlannedChange>(
                skips.Count == 0
                    ? ChangeErrors.NothingToChange
                    : ChangeErrors.EveryTaskWouldBeEmpty(skips.Count));
        }

        return Result.Success(new PlannedChange(
            ChangeKind.RemoveMarkers,
            SourceKeys: [],
            TargetSpelling: string.Empty,
            planned,
            matching.ExcludedLists,
            Reaching(applied, plan, skips, matching.ExcludedLists.Count > 0),
            skips,
            ChangeMarkerRulesDto.None));
    }

    /// <summary>
    /// Which of a Remove's Markers this plan covers completely — every task the index knows of that
    /// carries one is in the plan — so that a run which reaches them all has taken them out of every
    /// block there is.
    /// <para>
    /// Only then may the row that was keeping a Marker known be dropped when the run finishes. Two
    /// things make a plan short of that, and both are known here and nowhere later: a task whose
    /// title is nothing but the Markers being removed never becomes a plan row, and a list that has
    /// never been read end to end contributes no tasks at all (ADR-0003). Either leaves the emoji at
    /// the front of a real title afterwards, and a row dropped over it would stop the block reader
    /// in front of that emoji — which is the artefact those rows exist to prevent.
    /// </para>
    /// </summary>
    private static List<AppliedMarker> Reaching(
        IReadOnlyList<AppliedMarker> applied,
        MarkerPlan plan,
        IReadOnlyList<ChangePreviewSkipDto> skips,
        bool listsExcluded)
    {
        // A list nobody has read end to end may hold this Marker anywhere, so nothing is complete.
        var left = new HashSet<Marker>();

        if (!listsExcluded)
        {
            foreach (var skip in skips)
            {
                // What each passed-over task goes on carrying. Read as a block, so an emoji further
                // along the title is not mistaken for one.
                foreach (var marker in plan.BlockOf(skip.CurrentTitle))
                {
                    left.Add(marker);
                }
            }
        }

        return
        [
            .. applied.Select(marker => marker with
            {
                Reaches = marker.Removable
                    && !listsExcluded
                    && !left.Contains(Marker.Restore(marker.Marker))
                    && (marker.RetiredMarker is not { } retired || !left.Contains(Marker.Restore(retired))),
            }),
        ];
    }

    /// <summary>
    /// Every Marker the person has, with the ones a Remove may take marked removable.
    /// <para>
    /// All of them and not only the scope, because the block reader stops at the first grapheme it
    /// does not recognise: a Marker left out would end the block early and hide whatever sits
    /// behind it, including the one being removed.
    /// </para>
    /// </summary>
    private static List<AppliedMarker> RemovableMarkers(
        IReadOnlyList<MarkerRuleSnapshot> rules,
        IReadOnlyList<AbandonedMarkerSnapshot> abandoned,
        Marker? scoped)
    {
        // In scope when nothing was named, or when this is the Marker that was — its retirement
        // included, because a rule's retired Marker is that rule's own residue and MarkerPlan takes
        // the two together.
        bool InScope(Marker marker, Marker? retired) =>
            scoped is not { } wanted || marker == wanted || retired == wanted;

        var applied = rules
            .Select(rule => new AppliedMarker(
                rule.Key,
                rule.Spelling,
                rule.Marker.Text,
                rule.RetiredMarker?.Text,
                rule.Position,
                Abandoned: false,
                Removable: InScope(rule.Marker, rule.RetiredMarker)))
            .ToList();

        foreach (var marker in abandoned)
        {
            applied.Add(new AppliedMarker(
                marker.Key,
                marker.Spelling,
                marker.Marker.Text,
                RetiredMarker: null,
                Position: int.MaxValue,
                Abandoned: true,
                Removable: InScope(marker.Marker, null)));
        }

        return applied;
    }

    /// <summary>
    /// The sources as keys, de-duplicated and in a stable order. Ordinal throughout, because these
    /// are already folded values and the database compares the same bytes.
    /// <para>
    /// A key longer than the grammar allows is dropped rather than carried: the extractor refuses
    /// such a name, so nothing in the index can match it — and the Change row stores its sources
    /// as one bounded column, which a caller sending arbitrary text would otherwise overflow on
    /// the insert. Dropped rather than refused outright, because the result is the same refusal
    /// one step later, worded better: no source left, or no task to change.
    /// </para>
    /// </summary>
    private static List<string> Distinct(IReadOnlyList<string> sourceKeys)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var keys = new List<string>(sourceKeys.Count);

        foreach (var source in sourceKeys)
        {
            if (source.Length > HashtagKey.MaxLength)
            {
                continue;
            }

            var key = HashtagKey.Fold(source);

            if (key.Length is > 0 and <= HashtagKey.MaxLength && seen.Add(key))
            {
                keys.Add(key);
            }
        }

        return keys;
    }
}
