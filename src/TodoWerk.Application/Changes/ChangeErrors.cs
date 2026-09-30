using System.Globalization;
using TodoWerk.SharedKernel;

namespace TodoWerk.Application.Changes;

/// <summary>
/// Every way TodoWerk refuses to change somebody's tasks, in the words the UI shows. Each refusal
/// says what is wrong and what would fix it: a destructive operation declined without a reason is
/// indistinguishable from a broken one.
/// </summary>
public static class ChangeErrors
{
    /// <summary>
    /// The endpoint authorised the request but the principal carries no tenant or object id. A
    /// misconfigured token rather than an anonymous caller, whom the policy already turned away.
    /// </summary>
    public static readonly Error NotSignedIn = Error.Unauthorized(
        "Changes.NotSignedIn",
        "Sign in again to continue.");

    public static readonly Error NoSources = Error.Validation(
        "Changes.NoSources",
        "Choose at least one hashtag to change.");

    /// <summary>
    /// The target has to survive being written into a title and read back as one Hashtag whose
    /// Spelling is exactly what was typed (ADR-0006). Anything else would write a title the To Do
    /// clients no longer highlight, or one the index reads as a different tag.
    /// </summary>
    public static readonly Error InvalidTarget = Error.Validation(
        "Changes.InvalidTarget",
        "That is not a name a hashtag can have. Use letters, digits, hyphens or underscores, "
        + "with no spaces and no leading #.");

    /// <summary>
    /// One Change per user at a time (ADR-0006). Refused rather than queued behind the first,
    /// which is about to invalidate the plan the second was computed from.
    /// </summary>
    public static readonly Error ChangeAlreadyInFlight = Error.Conflict(
        "Changes.AlreadyInFlight",
        "You already have a change waiting or running. Let it finish, or cancel it, and try again.");

    /// <summary>
    /// Nothing to do. Either the Hashtag is gone from the index since the inventory was drawn, or
    /// every task carrying it is already spelled the way the Change asks for.
    /// </summary>
    public static readonly Error NothingToChange = Error.Validation(
        "Changes.NothingToChange",
        "No task needs this change. The hashtags may already be spelled this way, or the index "
        + "may have moved on since the list was drawn.");

    /// <summary>
    /// A Merge destroys a distinction the user made, so it is the one operation that asks twice.
    /// The second confirmation is a field on the request rather than a second round trip.
    /// </summary>
    public static readonly Error MergeNotConfirmed = Error.Validation(
        "Changes.MergeNotConfirmed",
        "This would merge hashtags into one and cannot be undone by renaming them apart again. "
        + "Confirm the merge to continue.");

    /// <summary>
    /// Two Hashtags being folded together each carry a Marker, and only one of them can be on the
    /// Hashtag that survives. The one question a Merge cannot answer for itself (ADR-0014), so it
    /// is refused the way an unconfirmed Merge is rather than decided by whichever rule sorts first.
    /// </summary>
    public static readonly Error MarkerSurvivorNotChosen = Error.Validation(
        "Changes.MarkerSurvivorNotChosen",
        "More than one of these hashtags carries a marker, and only one marker can stay. "
        + "Choose which one survives to continue.");

    public static readonly Error ChangeNotFound = Error.NotFound(
        "Changes.NotFound",
        "That change no longer exists.");

    /// <summary>
    /// Undo is whole-Change, one level deep, inside the retention window, and only for a Change
    /// that wrote something. Anything else has nothing to restore or nothing to restore it from.
    /// </summary>
    public static readonly Error UndoNotAvailable = Error.Conflict(
        "Changes.UndoNotAvailable",
        "This change cannot be undone. Undo is offered once, for a change that wrote something, "
        + "within 30 days of it running.");

    /// <summary>
    /// An Apply with nothing to apply. Not a failure of the Change so much as of the order things
    /// were done in, so the sentence says what to do first.
    /// </summary>
    public static readonly Error NoMarkerRules = Error.Validation(
        "Changes.NoMarkerRules",
        "You have no marker rules yet. Give a hashtag a marker first, then apply it.");

    /// <summary>
    /// An Apply scoped to one rule, naming a Hashtag that has none — a stale row in the browser, or
    /// a rule deleted in another tab.
    /// </summary>
    public static readonly Error MarkerRuleNotFound = Error.NotFound(
        "Changes.MarkerRuleNotFound",
        "That hashtag no longer has a marker rule.");

    /// <summary>
    /// Every task an Apply covers would be pushed past what Microsoft To Do stores, so the plan is
    /// empty for a reason that has nothing to do with the rules. Named separately from
    /// <see cref="NothingToChange"/>, which would send somebody looking in the wrong place.
    /// </summary>
    public static Error EveryTaskWouldBeTooLong(int taskCount) => Error.Validation(
        "Changes.EveryTaskWouldBeTooLong",
        string.Format(
            CultureInfo.InvariantCulture,
            "Adding the markers would make all {0} of these task titles longer than Microsoft To Do "
            + "stores, so none of them can be changed. Shorten the titles, or use a shorter marker.",
            taskCount));

    /// <summary>
    /// A Remove scoped to a Marker this person does not have — a stale row in the browser, or a
    /// rule changed in another tab. Named rather than answered with an empty plan, which would read
    /// as "nothing to remove" about a Marker that was never theirs.
    /// </summary>
    public static readonly Error MarkerNotFound = Error.NotFound(
        "Changes.MarkerNotFound",
        "That emoji is not one of your markers, so there is nothing of it to remove.");

    /// <summary>
    /// A Remove with nothing it could possibly take: no rules, and nothing left behind by a deleted
    /// one either.
    /// </summary>
    public static readonly Error NoMarkersToRemove = Error.Validation(
        "Changes.NoMarkersToRemove",
        "You have no markers, so there is nothing to remove.");

    /// <summary>
    /// Every task a Remove covers is nothing but the Markers it would take. Named separately from
    /// <see cref="NothingToChange"/>, which would send somebody looking in the wrong place.
    /// </summary>
    public static Error EveryTaskWouldBeEmpty(int taskCount) => Error.Validation(
        "Changes.EveryTaskWouldBeEmpty",
        string.Format(
            CultureInfo.InvariantCulture,
            "All {0} of these tasks are titled with nothing but the markers being removed, so none "
            + "of them can be changed — a task cannot be left with no title. Give them titles, or "
            + "remove the markers in Microsoft To Do.",
            taskCount));

    public static Error TooManySources(int maximum) => Error.Validation(
        "Changes.TooManySources",
        string.Format(
            CultureInfo.InvariantCulture,
            "A single change can fold at most {0} hashtags together. Do it in smaller steps.",
            maximum));

    /// <summary>
    /// Past the ceiling the Change is refused with the count, never chunked: chunking invents
    /// Changes nobody confirmed and makes undo ambiguous (ADR-0006).
    /// </summary>
    public static Error PlanTooLarge(int taskCount, int maximum) => Error.Validation(
        "Changes.PlanTooLarge",
        string.Format(
            CultureInfo.InvariantCulture,
            "This would rewrite {0} tasks, and one change is limited to {1}. Narrow it down — a "
            + "change holds your index for as long as it runs.",
            taskCount,
            maximum));
}
