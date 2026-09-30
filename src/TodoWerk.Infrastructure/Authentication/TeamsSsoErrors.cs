using TodoWerk.SharedKernel;

namespace TodoWerk.Infrastructure.Authentication;

/// <summary>
/// What can go wrong when the Teams tab trades its single sign-on token for a TodoWerk session.
/// <para>
/// The important one is <see cref="ConsentRequired"/>, and it is important because it is not an
/// error: the Teams SSO token carries <c>openid</c>, <c>profile</c>, <c>email</c> and
/// <c>offline_access</c> and never <c>Tasks.ReadWrite</c>, so in any tenant that has not granted
/// Tenant Consent the on-behalf-of exchange fails and that is the expected first run. The client
/// switches on the code to raise the consent popup, so every other failure here must carry a
/// different one — a broken tenant that looked like this one would loop through a popup forever.
/// </para>
/// </summary>
internal static class TeamsSsoErrors
{
    /// <summary>
    /// Nobody has consented to the Graph permission for this person yet. Answered as 401 rather
    /// than 403: the caller has no usable session, and the thing that fixes it is an interactive
    /// sign-in — which is what the consent popup runs.
    /// </summary>
    internal static readonly Error ConsentRequired = Error.Unauthorized(
        "TeamsSso.ConsentRequired",
        "TodoWerk has not been approved for your account yet. Approve it once and this tab will "
        + "sign you in silently from then on.");

    /// <summary>
    /// The token was absent or malformed before anything reached Entra ID. Distinct from
    /// <see cref="ConsentRequired"/>, and distinct from a rejected token — the authentication
    /// scheme answers that one and never reaches here.
    /// </summary>
    internal static readonly Error TokenMissing = Error.Validation(
        "TeamsSso.TokenMissing",
        "The Teams sign-in token was missing from the request.");

    /// <summary>
    /// The token validated but carries no tenant or object id, so there is nobody to be. Not
    /// reachable from a real Entra ID token; here because the alternative is a session cookie
    /// with no identity in it.
    /// </summary>
    internal static readonly Error IdentityIncomplete = Error.Unauthorized(
        "TeamsSso.IdentityIncomplete",
        "The Teams sign-in token did not identify an account TodoWerk can use.");

    /// <summary>
    /// Entra ID refused the exchange for a reason no consent screen fixes, or did not answer at
    /// all. Retryable, unlike <see cref="ConsentRequired"/>, and the client says so rather than
    /// offering a popup that would change nothing.
    /// </summary>
    internal static readonly Error ExchangeFailed = Error.Failure(
        "TeamsSso.ExchangeFailed",
        "TodoWerk could not complete sign-in with Microsoft. Try again in a moment.");
}
