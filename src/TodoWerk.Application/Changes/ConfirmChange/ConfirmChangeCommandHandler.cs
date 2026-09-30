using Microsoft.Extensions.Options;
using TodoWerk.Application.Abstractions.Authentication;
using TodoWerk.Application.Abstractions.Messaging;
using TodoWerk.Domain.Changes;
using TodoWerk.Domain.Hashtags;
using TodoWerk.SharedKernel;

namespace TodoWerk.Application.Changes.ConfirmChange;

internal sealed class ConfirmChangeCommandHandler(
    ICurrentUser currentUser,
    IChangeStore changes,
    ChangePlanner planner,
    IOptions<ChangeOptions> options,
    TimeProvider timeProvider)
    : ICommandHandler<ConfirmChangeCommand, ChangeDto>
{
    public async Task<Result<ChangeDto>> HandleAsync(
        ConfirmChangeCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var user = IndexUser.From(currentUser);

        if (user is null)
        {
            return Result.Failure<ChangeDto>(ChangeErrors.NotSignedIn);
        }

        if (await changes.HasUnfinishedChangeAsync(user, cancellationToken))
        {
            return Result.Failure<ChangeDto>(ChangeErrors.ChangeAlreadyInFlight);
        }

        var planned = command switch
        {
            { RemoveMarkers: true } => await planner.PlanRemovalAsync(user, command.Marker, cancellationToken),
            { ApplyMarkers: true } => await planner.PlanMarkersAsync(user, command.MarkerRuleKey, cancellationToken),
            _ => await planner.PlanAsync(user, command.SourceKeys, command.TargetSpelling, cancellationToken),
        };

        if (planned.IsFailure)
        {
            return Result.Failure<ChangeDto>(planned.Error);
        }

        var plan = planned.Value;

        // The second confirmation, checked against what the server classified rather than against
        // what the browser thought it was confirming. A rename can become a Merge between the
        // preview and the click — somebody else's Change, or a scan, can put the target Hashtag in
        // the index — and that is precisely the case this must not wave through.
        if (plan.RequiresMergeConfirmation && !command.ConfirmMerge)
        {
            return Result.Failure<ChangeDto>(ChangeErrors.MergeNotConfirmed);
        }

        var carried = CarriedRule(plan, command.SurvivingMarker);

        if (carried.IsFailure)
        {
            return Result.Failure<ChangeDto>(carried.Error);
        }

        var changeId = await changes.AddAsync(
            user,
            new ConfirmedChangePlan(
                plan.SourceKeys,
                plan.TargetSpelling,
                plan.Kind,
                plan.Tasks,
                plan.Kind is ChangeKind.ApplyMarkers or ChangeKind.RemoveMarkers
                    ? plan.AppliedMarkers
                    : carried.Value),
            cancellationToken);

        var stored = await changes.FindAsync(user, changeId, cancellationToken);

        return stored is null
            ? Result.Failure<ChangeDto>(ChangeErrors.ChangeNotFound)
            : Result.Success(ChangeMapper.ToDto(stored, timeProvider.GetUtcNow(), options.Value.ChangeRetention));
    }

    /// <summary>
    /// Which Marker Rule, if any, follows this Change to the target name — settled here, at
    /// confirmation, and written onto the Change so the run does not have to ask a rules table that
    /// may have moved on (ADR-0014).
    /// <para>
    /// Nothing follows when the target already has a rule: its own wins, and every source rule is
    /// deleted at completion. Otherwise a single source rule follows on its own, and several source
    /// rules follow whichever one the user chose — refused if they did not choose, which is checked
    /// against what the server planned rather than against what the browser thought it was
    /// confirming.
    /// </para>
    /// </summary>
    private static Result<IReadOnlyList<AppliedMarker>> CarriedRule(PlannedChange plan, string? survivingMarker)
    {
        var rules = plan.MarkerRules;

        if (plan.Kind is ChangeKind.ApplyMarkers or ChangeKind.RemoveMarkers
            || rules.TargetRule is not null
            || rules.SourceRules.Count == 0)
        {
            return Result.Success<IReadOnlyList<AppliedMarker>>([]);
        }

        // Matched as a Marker rather than as text, so the two presentations of one emoji are one
        // answer — the same equality that made them one Marker in the first place.
        var chosen = Marker.TryCreate(survivingMarker, out var choice) ? choice : default;

        var surviving = rules.SourceRules.Count == 1
            ? rules.SourceRules[0]
            : chosen.IsEmpty
                ? null
                : rules.SourceRules.FirstOrDefault(rule => Marker.Restore(rule.Marker) == chosen);

        if (surviving is null)
        {
            return Result.Failure<IReadOnlyList<AppliedMarker>>(ChangeErrors.MarkerSurvivorNotChosen);
        }

        // Position zero: the rule keeps its place in the person's list, which the mover reads off
        // the row it is carrying rather than off this copy. Nothing here reorders anything.
        return Result.Success<IReadOnlyList<AppliedMarker>>(
            [new AppliedMarker(surviving.Key, surviving.Spelling, surviving.Marker, null, 0)]);
    }
}
