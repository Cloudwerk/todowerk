using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.WebUtilities;
using TodoWerk.Web.Diagnostics;
using TodoWerk.Web.Endpoints;

namespace TodoWerk.Web.Security;

/// <summary>
/// What happens when Entra ID comes back to <c>/signin-oidc</c> with something other than a
/// ticket — a consent screen somebody did not approve, a correlation cookie that was not there,
/// a protocol error.
/// <para>
/// Without this the framework's default applies, and the default is to rethrow: the callback
/// answers <c>500</c> with a problem-details document, in a browser tab, to somebody who has just
/// been told their organisation needs to approve something. It is the most likely first
/// experience of TodoWerk in any tenant that has not granted Tenant Consent — a person who
/// declines a consent screen would see RFC 9110 JSON — and it is not an error page at all:
/// declining is an ordinary answer to an ordinary question.
/// </para>
/// <para>
/// So the callback never throws. It sends the browser back to a screen that says what happened
/// and what to do about it, and writes one log line saying which of the two it was.
/// </para>
/// </summary>
internal static class SignInFailure
{
    /// <summary>
    /// How the screen learns which card to draw. Client half:
    /// <c>SignInPrompt</c> in <c>ClientApp/src/components/SignInPrompt.tsx</c>, and
    /// <c>authEnd.ts</c> for the Teams popup, which reports a failure back to the tab rather than
    /// drawing anything.
    /// </summary>
    internal const string ReasonParameter = "signin";

    /// <summary>
    /// Nobody approved: the person declined, or their organisation requires an administrator to.
    /// <para>
    /// One reason for both, because Entra ID does not reliably tell them apart. A decline and a
    /// "your administrator must approve this" both arrive as <c>access_denied</c>, and which
    /// AADSTS code rides along depends on the tenant's consent policy rather than on what the
    /// person did. Two cards would mean guessing, and a guess is a sentence that is wrong for half
    /// the people who read it. The card names both readings and offers a route for each; the log
    /// line carries the code, so a real occurrence can sharpen this later.
    /// </para>
    /// </summary>
    internal const string NotApproved = "not-approved";

    /// <summary>Anything else. Nothing was refused; the round trip did not complete.</summary>
    internal const string Failed = "failed";

    /// <summary>The errors that mean "not approved" rather than "went wrong".</summary>
    private static readonly string[] Refusals = ["access_denied", "consent_required", "interaction_required"];

    /// <summary>
    /// Chains onto whatever the events already do rather than replacing them: Microsoft.Identity.Web
    /// puts its own work on some of these, and an assignment would drop it. Where something ahead
    /// of this has already answered the request, this does nothing.
    /// </summary>
    internal static void ChainOnto(OpenIdConnectOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        // Two events, because the framework splits one outcome across them: an `access_denied`
        // response is offered to OnAccessDenied first — it is frequent and not a misconfiguration
        // — and only becomes a remote failure if nothing handled it there. Everything else arrives
        // at OnRemoteFailure directly. Both are handled, so neither can fall through to the throw.
        var accessDenied = options.Events.OnAccessDenied;

        options.Events.OnAccessDenied = async context =>
        {
            await accessDenied(context);

            if (context.Result is not null)
            {
                return;
            }

            Redirect(context.HttpContext, context.Properties, NotApproved);
            context.HandleResponse();
        };

        var remoteFailure = options.Events.OnRemoteFailure;

        options.Events.OnRemoteFailure = async context =>
        {
            await remoteFailure(context);

            if (context.Result is not null)
            {
                return;
            }

            Redirect(context.HttpContext, context.Properties, ReasonFor(context.HttpContext.Request));
            context.HandleResponse();
        };
    }

    /// <summary>
    /// Which card, read off the response Entra ID sent rather than off the exception text. The
    /// parameters are the protocol's; the message wrapping them is the framework's, and matching
    /// on prose is a thing that breaks on an upgrade with nobody noticing.
    /// </summary>
    private static string ReasonFor(HttpRequest request) =>
        Refusals.Contains(Parameter(request, "error"), StringComparer.Ordinal) ? NotApproved : Failed;

    /// <summary>
    /// Where the browser goes, and one line saying why it went there.
    /// <para>
    /// Back to the screen the sign-in was started from, carrying the reason. That matters for the
    /// Teams consent popup as much as for the browser: its return URL is the tab's auth-end
    /// document, and a popup that lands anywhere else never reports back — Teams eventually calls
    /// it cancelled, which is the same silence this whole class exists to end.
    /// </para>
    /// </summary>
    private static void Redirect(HttpContext context, AuthenticationProperties? properties, string reason)
    {
        var request = context.Request;

        var logger = context.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(SignInFailure));

        if (reason is NotApproved)
        {
            // Information rather than a warning: somebody declining a consent screen, or meeting a
            // tenant that reserves the decision for an administrator, is the product working as
            // designed. The code is here because it is the only evidence anywhere of which of the
            // two it was — nothing else in TodoWerk sees it.
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation(
                    "A sign-in ended without approval and the person is being shown the card that "
                    + "says so. Entra ID answered {Error}: {ErrorDescription}",
                    LogSafe.Scalar(Parameter(request, "error")),
                    LogSafe.Scalar(Parameter(request, "error_description")));
            }
        }
        else if (logger.IsEnabled(LogLevel.Warning))
        {
            // A warning, because this one is nobody's decision: a correlation cookie that did not
            // survive, a sign-in page left open past its state, a protocol error. Often there is no
            // error parameter at all, and the absence is itself the answer.
            logger.LogWarning(
                "A sign-in could not be completed and the person is being asked to try again. "
                + "Entra ID answered {Error}: {ErrorDescription}",
                LogSafe.Scalar(Parameter(request, "error")),
                LogSafe.Scalar(Parameter(request, "error_description")));
        }

        // A local path or nothing. The return URL travels in the protected authentication
        // properties and is this application's own — but it is also the one value here that ends
        // up in a Location header, and an open redirect reached through a declined sign-in would
        // be no less open for having been hard to arrange.
        var returnUrl = AuthEndpoints.IsLocalReturnUrl(properties?.RedirectUri)
            ? properties!.RedirectUri!
            : "/";

        context.Response.Redirect(QueryHelpers.AddQueryString(returnUrl, ReasonParameter, reason));
    }

    /// <summary>
    /// One protocol parameter, from wherever this response carries it. TodoWerk runs
    /// <c>response_mode=form_post</c>, so an error arrives as a form field — but the handler
    /// accepts a query response too, and a reason read from only one of the two would be
    /// "something went wrong" for half of them.
    /// </summary>
    private static string? Parameter(HttpRequest request, string name) =>
        request.HasFormContentType ? request.Form[name] : request.Query[name];
}
