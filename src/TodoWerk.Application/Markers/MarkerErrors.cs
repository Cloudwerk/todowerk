using System.Globalization;
using TodoWerk.SharedKernel;

namespace TodoWerk.Application.Markers;

/// <summary>
/// Every way TodoWerk refuses a Marker Rule, in the words the UI shows. The two that name another
/// Hashtag do so deliberately: "that emoji is taken" without saying by what leaves somebody hunting
/// through their own rules for it.
/// </summary>
public static class MarkerErrors
{
    public static readonly Error NotSignedIn = Error.Unauthorized(
        "Markers.NotSignedIn",
        "Sign in again to continue.");

    /// <summary>
    /// The grammar of <see cref="Domain.Hashtags.Marker"/>, in a sentence. A flag, a keycap, a
    /// skin-toned hand and a family are each one emoji; two emoji and a letter are not.
    /// </summary>
    public static readonly Error InvalidMarker = Error.Validation(
        "Markers.InvalidMarker",
        "A marker is exactly one emoji. Pick one from the grid, or paste a single emoji — a flag, "
        + "a keycap or a skin tone counts as one.");

    /// <summary>
    /// One emoji by every other measure, and the one the Hashtag grammar would read as a tag: the
    /// hash keycap opens on the marker character. Named separately from <see cref="InvalidMarker"/>,
    /// whose sentence would be a lie about it.
    /// </summary>
    public static readonly Error MarkerOpensAHashtag = Error.Validation(
        "Markers.MarkerOpensAHashtag",
        "That begins with # and would be read as a hashtag, so it cannot be a marker. Choose another.");

    /// <summary>
    /// A PATCH that asks for a new Marker and a move at once. Refused rather than done in an order,
    /// because two writes can fail between them and a request that half-applied would be answered
    /// as though it had not applied at all.
    /// </summary>
    public static readonly Error OneChangeAtATime = Error.Validation(
        "Markers.OneChangeAtATime",
        "Change the marker, or move the rule — not both in one request.");

    public static readonly Error InvalidHashtag = Error.Validation(
        "Markers.InvalidHashtag",
        "That is not a name a hashtag can have. Use letters, digits, hyphens or underscores, "
        + "with no spaces and no leading #.");

    public static readonly Error RuleNotFound = Error.NotFound(
        "Markers.RuleNotFound",
        "That marker rule no longer exists.");

    /// <summary>
    /// The end of the list moving up, or the front moving down. Refused rather than ignored, so the
    /// client is not left redrawing an order that did not change.
    /// </summary>
    public static readonly Error RuleCannotMove = Error.Validation(
        "Markers.RuleCannotMove",
        "That rule is already at the end it is being moved towards.");

    public static readonly Error NothingToUpdate = Error.Validation(
        "Markers.NothingToUpdate",
        "Say what to change about the rule: a new marker, or a move up or down.");

    /// <summary>
    /// Why <see cref="Domain.Hashtags.Marker.TryCreate"/> said no, in the one case where "not one
    /// emoji" would be untrue: a candidate the Hashtag grammar reads as a tag.
    /// </summary>
    public static Error ForRefusedMarker(string? candidate) =>
        candidate is { Length: > 0 and <= Domain.Hashtags.Marker.MaxLength }
            && Domain.Hashtags.HashtagExtractor.Locate(candidate).Count > 0
            ? MarkerOpensAHashtag
            : InvalidMarker;

    /// <summary>
    /// One Marker, one Hashtag. Two rules sharing 🔴 would stop a block mapping one Marker onto one
    /// Hashtag, and would make the stale-Marker count a later feature promises undecidable
    /// (ADR-0014).
    /// </summary>
    public static Error MarkerInUse(string spelling) => Error.Conflict(
        "Markers.MarkerInUse",
        string.Format(
            CultureInfo.InvariantCulture,
            "That emoji is already the marker for #{0}. Choose another, or change that rule first.",
            spelling));

    public static Error RuleAlreadyExists(string spelling) => Error.Conflict(
        "Markers.RuleAlreadyExists",
        string.Format(
            CultureInfo.InvariantCulture,
            "#{0} already has a marker. Change that rule rather than adding a second one.",
            spelling));

    public static Error TooManyRules(int maximum) => Error.Validation(
        "Markers.TooManyRules",
        string.Format(
            CultureInfo.InvariantCulture,
            "You already have {0} marker rules, which is as many as TodoWerk keeps. Delete one to "
            + "make room.",
            maximum));
}
