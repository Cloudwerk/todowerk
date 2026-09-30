using System.Security.Claims;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Client;
using Microsoft.Identity.Web;
using TodoWerk.Application.Abstractions.Authentication;
using TodoWerk.Application.Abstractions.Licensing;
using TodoWerk.Application.Onboarding;
using TodoWerk.SharedKernel;

// Microsoft.Identity.Client publishes a LogLevel of its own, and it is not the one this file
// logs through.
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace TodoWerk.Infrastructure.Authentication;

/// <summary>
/// Turns a validated Teams single sign-on token into the principal the ordinary session cookie
/// carries, by exchanging it on-behalf-of for the same Graph scopes every other path uses.
/// <para>
/// The exchange happens here and the token it produces never leaves the server, exactly as
/// [ADR-0002](../../../docs/adr/0002-backend-held-tokens.md) said the Teams path would work from
/// M0. What the browser ends up holding is <c>todowerk.session</c> and nothing else — the same
/// cookie, the same scheme, the same eight hours
/// ([ADR-0010](../../../docs/adr/0010-teams-tab-session-and-framing.md)).
/// </para>
/// </summary>
public sealed class TeamsSsoSignIn(
    ITokenAcquisition tokenAcquisition,
    ITenantMemberStore members,
    ISeatUsageReporter seats,
    ILogger<TeamsSsoSignIn> logger)
{
    /// <summary>
    /// Exchanges the caller's token and returns the principal to sign in, or the reason not to.
    /// </summary>
    /// <param name="ssoUser">
    /// The principal the Teams SSO authentication scheme produced. Its bootstrap token is the
    /// assertion the exchange is made with, which is why this takes a principal rather than a
    /// string: the raw token is validated in one place and read off the identity afterwards.
    /// </param>
    public async Task<Result<ClaimsPrincipal>> ExchangeAsync(
        ClaimsPrincipal ssoUser,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ssoUser);

        var tenantId = ssoUser.GetTenantId();
        var objectId = ssoUser.GetObjectId();

        if (string.IsNullOrEmpty(tenantId) || string.IsNullOrEmpty(objectId))
        {
            return Result.Failure<ClaimsPrincipal>(TeamsSsoErrors.IdentityIncomplete);
        }

        AuthenticationResult result;

        try
        {
            // A long-running exchange rather than a plain one, and the difference is whether
            // anything can find the result afterwards. A plain on-behalf-of call files its tokens
            // under a hash of this request's assertion; nothing else ever holds that assertion, so
            // the next request, and every background worker, would look in the partition MSAL
            // keeps authorization-code results in and find nothing. Naming the session key files
            // them somewhere both ends can compute (TeamsSsoDefaults.SessionKeyFor).
            result = await tokenAcquisition.GetAuthenticationResultForUserAsync(
                GraphScopes.SignIn,
                authenticationScheme: TeamsSsoDefaults.AuthenticationScheme,
                user: ssoUser,
                tokenAcquisitionOptions: new TokenAcquisitionOptions
                {
                    LongRunningWebApiSessionKey = TeamsSsoDefaults.SessionKeyFor(tenantId, objectId),
                });
        }
        catch (MicrosoftIdentityWebChallengeUserException exception)
        {
            // Microsoft.Identity.Web's own wrapper for "this one needs a human". Unwrapped rather
            // than taken on its face, because the inner exception is what says whether a consent
            // screen would actually help.
            return ConsentOrFailure(exception.MsalUiRequiredException, exception);
        }
        catch (MsalUiRequiredException exception)
        {
            return ConsentOrFailure(exception, exception);
        }
        catch (MsalException exception)
        {
            // Entra ID having a bad moment, a client secret that has expired, a transport fault
            // inside MSAL. None of them is the user's to fix and none of them is fixed by a
            // consent screen, so none of them may come back looking like the one that is — a
            // tenant meeting this would otherwise be sent through the popup on a loop.
            logger.LogError(
                exception,
                "The Teams on-behalf-of exchange failed with {ErrorCode}. This is not a consent "
                + "condition, so the tab is told to retry rather than to raise the consent popup.",
                exception.ErrorCode);

            return Result.Failure<ClaimsPrincipal>(TeamsSsoErrors.ExchangeFailed);
        }

        await RecordSignInAsync(tenantId, objectId, cancellationToken);

        return Result.Success(SessionPrincipal(ssoUser, result, tenantId, objectId));
    }

    /// <summary>
    /// Whether the popup would fix this, which is a wider question than whether it is literally
    /// about consent — and the honest name for the line this method draws.
    /// <para>
    /// The dominant case is consent, and it is why the client's card is written the way it is: the
    /// Teams SSO token carries only the OpenID scopes and never <c>Tasks.ReadWrite</c>, so a tenant
    /// without Tenant Consent fails here on its first run every time, with <c>invalid_grant</c>,
    /// <c>AADSTS65001</c> and a consent-required classification. That is the expected path rather
    /// than an error.
    /// </para>
    /// <para>
    /// The other conditions MSAL raises the same exception for — a refresh token that was revoked,
    /// a Conditional Access policy asking for something, a grant that has lapsed — are folded in
    /// deliberately, because what the popup runs is TodoWerk's whole interactive sign-in rather
    /// than a consent screen, and that is the fix for all of them. What must <em>not</em> be folded
    /// in is a failure no interaction resolves — a client secret that has expired, Entra ID being
    /// unreachable — because a tenant offered a popup that cannot help meets it again on every
    /// press. Those are <see cref="MsalException"/> without the UI-required shape, and they take
    /// the other branch.
    /// </para>
    /// <para>
    /// One thing this does not do is count attempts. Somebody whose Conditional Access policy will
    /// never be satisfied can press the button, complete the popup, and meet the same card — each
    /// time by their own action rather than in a spin. Left as it is: a client-side attempt counter
    /// would be state to get wrong, and the failure it would guard is one nobody has yet seen.
    /// </para>
    /// </summary>
    private Result<ClaimsPrincipal> ConsentOrFailure(MsalUiRequiredException? uiRequired, Exception thrown)
    {
        var consent = uiRequired is not null
            && (uiRequired.Classification is UiRequiredExceptionClassification.ConsentRequired
                || string.Equals(uiRequired.ErrorCode, "invalid_grant", StringComparison.Ordinal)
                || string.Equals(uiRequired.ErrorCode, "interaction_required", StringComparison.Ordinal)
                || string.Equals(uiRequired.ErrorCode, "consent_required", StringComparison.Ordinal));

        if (consent)
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation(
                    "A Teams tab reached the on-behalf-of exchange without consent for the Graph "
                    + "scopes ({ErrorCode}/{Classification}); the tab will offer the consent popup.",
                    uiRequired!.ErrorCode,
                    uiRequired.Classification.ToString());
            }

            return Result.Failure<ClaimsPrincipal>(TeamsSsoErrors.ConsentRequired);
        }

        logger.LogError(
            thrown,
            "The Teams on-behalf-of exchange needed interaction for a reason the consent popup "
            + "does not fix; the tab is told to retry instead.");

        return Result.Failure<ClaimsPrincipal>(TeamsSsoErrors.ExchangeFailed);
    }

    /// <summary>
    /// The claims the rest of TodoWerk reads a person from — the same set the OpenID Connect
    /// callback leaves in the cookie, so nothing downstream can tell the two sign-ins apart.
    /// <para>
    /// <c>uid</c> and <c>utid</c> come from the account MSAL resolved rather than from the SSO
    /// token, because they are the <em>home</em> identifiers MSAL keys its cache on, and a cookie
    /// carrying the directory pair instead authenticates perfectly and then fails every Graph
    /// call — the same trap <c>GraphGateway.AccountPrincipal</c> documents from the other side.
    /// </para>
    /// </summary>
    private static ClaimsPrincipal SessionPrincipal(
        ClaimsPrincipal ssoUser,
        AuthenticationResult result,
        string tenantId,
        string objectId)
    {
        var home = result.Account?.HomeAccountId;

        var claims = new List<Claim>
        {
            new(ClaimConstants.UniqueObjectIdentifier, home?.ObjectId ?? objectId),
            new(ClaimConstants.UniqueTenantIdentifier, home?.TenantId ?? tenantId),
            new(ClaimConstants.Oid, objectId),
            new(ClaimConstants.Tid, tenantId),
        };

        if (First(ssoUser, "preferred_username", ClaimTypes.Upn) is { Length: > 0 } username)
        {
            claims.Add(new Claim("preferred_username", username));
        }

        if (First(ssoUser, "name", ClaimTypes.Name) is { Length: > 0 } name)
        {
            claims.Add(new Claim(ClaimTypes.Name, name));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(
            claims,
            TeamsSsoDefaults.AuthenticationScheme,
            ClaimTypes.Name,
            ClaimTypes.Role));
    }

    /// <summary>
    /// Both spellings of one claim. A JWT handler that maps inbound claims rewrites the short
    /// names to the WS-Federation URIs and one that does not leaves them alone, and which of the
    /// two is in force is a library default rather than something this code should depend on.
    /// </summary>
    private static string? First(ClaimsPrincipal principal, string shortName, string uri) =>
        principal.FindFirstValue(shortName) ?? principal.FindFirstValue(uri);

    /// <summary>
    /// Somebody opening the tab is somebody using the product, so the record the browser sign-in
    /// writes is written here too
    /// ([ADR-0009](../../../docs/adr/0009-what-todowerk-stores-about-a-person.md)).
    /// <para>
    /// The failure policy is deliberately the same as <c>TenantMemberSignInRecorder</c>'s, and it
    /// is repeated rather than shared: sharing it would point this namespace at the Onboarding
    /// module while Onboarding already post-configures the options in this one, which is a cycle
    /// bought to save a dozen lines. Logged and swallowed, because refusing somebody the product
    /// over a statistics row is the worse failure — what it costs is one uncounted visit and a
    /// retention clock that did not move.
    /// </para>
    /// </summary>
    private async Task RecordSignInAsync(string tenantId, string objectId, CancellationToken cancellationToken)
    {
        var user = new IndexUser(tenantId, objectId);

        try
        {
            await members.RecordSignInAsync(user, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(
                exception,
                "Recording the membership record for a completed Teams sign-in failed. The person "
                + "is signed in; the tenant's statistics and their retention clock are behind by "
                + "this visit.");
        }

        try
        {
            // The seat, on the same moment and under the same policy. The reporter itself is
            // fire-and-forget and swallows its own failures, so what this catch covers is the
            // registration being wrong rather than the portal being slow. On a Self-Host the
            // reporter does nothing and nothing leaves the building.
            await seats.ReportSignInAsync(user, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Could not start the seat report for a completed Teams sign-in.");
        }
    }
}
