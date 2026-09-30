using TodoWerk.SharedKernel;

namespace TodoWerk.Application.Licensing;

/// <summary>
/// The two refusals, which the client renders as two different cards. Keeping them apart is the
/// whole point: one of them is met by people who have paid, during an outage on CloudWerk's
/// side, and reading it as the other one would send them to buy something they already own.
/// <para>
/// Both are <see cref="ErrorType.Forbidden"/> rather than unauthorized. The person is signed in
/// and their session is perfectly good; offering them a sign-in would be advice that cannot help
/// and a loop they cannot leave.
/// </para>
/// </summary>
public static class LicensingErrors
{
    /// <summary>
    /// A confirmed negative from the portal — expired, suspended, or nothing held at all. The
    /// description is replaced by the portal's own sentence where it sent one, because an expired
    /// Trial and an expired subscription are different news and only the portal knows which.
    /// </summary>
    public static readonly Error Ended = Error.Forbidden(
        "Licensing.Ended",
        "Your access to TodoWerk has ended.");

    /// <summary>
    /// The portal could not be reached and no positive answer is left inside the fail-open window.
    /// Worded so that it cannot be mistaken for the other one, and so that it says out loud that
    /// nobody has to do anything: the next successful resolution clears it with no button and no
    /// reload.
    /// </summary>
    public static readonly Error CouldNotBeVerified = Error.Forbidden(
        "Licensing.CouldNotBeVerified",
        "TodoWerk could not confirm your organisation's licence. It keeps trying.");

    /// <summary>
    /// The endpoint authorised the request but the principal carries no tenant or object id, so
    /// there is nobody to resolve a Licence for. A misconfigured token rather than an anonymous
    /// caller — the authorization policy has already turned that one away.
    /// </summary>
    public static readonly Error NotSignedIn = Error.Unauthorized(
        "Licensing.NotSignedIn",
        "Sign in again to continue.");

    /// <summary>
    /// The refusal a resolution turns into, with the portal's words when it sent any. Used by the
    /// gate that answers requests and by the endpoint alike, so one outcome cannot become two
    /// different problem codes depending on who asked.
    /// </summary>
    public static Error ForDeniedResolution(bool wasConfirmedNegative, string? portalMessage) =>
        wasConfirmedNegative
            ? string.IsNullOrWhiteSpace(portalMessage)
                ? Ended
                : Ended with { Description = portalMessage }
            : CouldNotBeVerified;
}
