using TodoWerk.SharedKernel;

namespace TodoWerk.Application.Onboarding;

/// <summary>Failures the Onboarding module reports to the caller, in the words the UI shows.</summary>
public static class OnboardingErrors
{
    /// <summary>
    /// The endpoint authorised the request but the principal carries no tenant or object id, so
    /// there is no tenant to report on and nobody to forget. A misconfigured token rather than an
    /// anonymous caller — the authorization policy has already turned that one away.
    /// </summary>
    public static readonly Error NotSignedIn = Error.Unauthorized(
        "Onboarding.NotSignedIn",
        "Sign in again to continue.");

    /// <summary>
    /// A purge stopped while it was still finding rows — something was writing for this person at
    /// the same time. Nothing was anonymised, so trying again finishes the job rather than leaving
    /// an unreachable remainder. Said plainly, because the alternative is telling somebody their
    /// data is gone when some of it is not.
    /// <para>
    /// A conflict rather than a failure: nothing is broken, something else was in the way, and the
    /// request is worth repeating. The distinction reaches the caller as 409 instead of 500, which is
    /// the difference between "try again" and "tell somebody".
    /// </para>
    /// </summary>
    public static readonly Error ErasureIncomplete = Error.Conflict(
        "Onboarding.ErasureIncomplete",
        "TodoWerk could not delete everything, because something was still working on your data. "
        + "Nothing has been half-deleted — try again in a moment.");
}
