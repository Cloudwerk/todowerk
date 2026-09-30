using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Identity.Web;
using TodoWerk.Application.Abstractions.Messaging;
using TodoWerk.Application.Onboarding;
using TodoWerk.Application.Onboarding.EraseSelf;
using TodoWerk.Application.Onboarding.GetTenantOverview;
using TodoWerk.Infrastructure.Authentication;
using TodoWerk.Infrastructure.Onboarding;
using TodoWerk.Web.Diagnostics;
using TodoWerk.Web.Security;

namespace TodoWerk.Web.Endpoints;

internal static class OnboardingEndpoints
{
    /// <summary>Where the browser is put back after a consent round trip: the screen it started on.</summary>
    private const string TenantOverviewRoute = "/tenant";

    /// <summary>
    /// How the front door learns how a consent round trip ended, for the one that began there:
    /// somebody with no session, sent by the card that says their sign-in was not approved. Client
    /// half: <c>SignInPrompt</c> in <c>ClientApp/src/components/SignInPrompt.tsx</c>.
    /// </summary>
    internal const string ConsentParameter = "consent";

    internal const string Granted = "granted";

    /// <summary>
    /// Declined, abandoned, or answered by somebody who could not approve. One value for all of
    /// them: Microsoft's decline says no more than that, and the card offers the same next step
    /// whichever it was.
    /// </summary>
    internal const string NotGranted = "not-granted";

    /// <summary>
    /// Remembers where a consent round trip should end when it is not the Tenant Overview — which
    /// in practice means the Teams tab's auth-end page. Scoped to the callback path and cleared
    /// there, exactly like the state cookie it travels beside.
    /// </summary>
    internal const string ReturnCookieName = "todowerk.tenant-consent.return";

    public static IEndpointRouteBuilder MapOnboardingEndpoints(this IEndpointRouteBuilder app)
    {
        // Counts about the caller's own tenant. No role check and no administrator concept: it
        // discloses four numbers and a date, and a gate would cost a permission-denied path and a
        // support question for a screen that names nobody.
        app.MapGet(
                "/api/tenant/overview",
                async (
                    IQueryHandler<GetTenantOverviewQuery, TenantOverviewDto> handler,
                    CancellationToken cancellationToken) =>
                {
                    var result = await handler.HandleAsync(new GetTenantOverviewQuery(), cancellationToken);

                    return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblem();
                })
            .RequireAuthorization(AuthorizationPolicies.Api)
            .WithName("GetTenantOverview");

        // A form POST answered with a sign-out, for the same reason /auth/sign-out is one: the
        // response ends in a redirect to the Entra ID end-session endpoint, and only a browser
        // navigation can follow that.
        app.MapPost(
                "/api/me/erasure",
                async (
                    HttpContext context,
                    ICommandHandler<EraseSelfCommand> handler,
                    CancellationToken cancellationToken) =>
                {
                    var result = await handler.HandleAsync(new EraseSelfCommand(), cancellationToken);

                    if (result.IsFailure)
                    {
                        return result.Error.ToProblem();
                    }

                    // Two answers for two callers, and the difference is whether anybody can follow
                    // a redirect. The browser SPA submits a real form, so its answer ends in the
                    // navigation to Entra ID's end-session endpoint that only a browser can follow.
                    // The Teams tab cannot: it sits in an iframe that sign-in pages refuse to be
                    // rendered in, so it calls this with fetch, has its cookie deleted here, and
                    // runs the Entra leg through /auth/teams-end-session in a popup.
                    if (WantsJson(context.Request))
                    {
                        await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

                        return Results.NoContent();
                    }

                    return Results.SignOut(
                        new AuthenticationProperties { RedirectUri = "/" },
                        [
                            CookieAuthenticationDefaults.AuthenticationScheme,
                            OpenIdConnectDefaults.AuthenticationScheme,
                        ]);
                })
            .RequireAuthorization(AuthorizationPolicies.Api)
            // Reachable whether or not the caller is licensed. Erasure is an obligation, not a
            // feature of the product, and a person shut out of TodoWerk is exactly the person most
            // likely to want everything it holds about them destroyed.
            .AllowUnlicensed()
            .ValidateAntiforgery()
            .WithName("EraseSelf");

        // Browser navigation, not fetch: the response is a redirect to Microsoft's admin-consent
        // endpoint. The tenant being approved is the caller's own — never one they could name in a
        // parameter — so the session says which it is when there is one.
        //
        // Anonymous rather than behind the API policy, and that is load-bearing rather than a
        // convenience: the second person this link is for is the one whose sign-in has just ended
        // without approval, who by definition has no session and cannot be sent to get one — the
        // sign-in they would be sent to is the one that just refused them. Their flow goes to this
        // deployment's own authority and Microsoft asks whoever presses it to sign in, which is
        // also the only way an administrator who is not this person can approve from this screen.
        // Nothing is recorded when such a flow comes back; see TenantConsentFlow.Start.
        app.MapGet(
                TenantConsentFlow.StartPath,
                async (
                    HttpContext context,
                    TenantConsentFlow flow,
                    IHostEnvironment environment,
                    string? returnUrl = null) =>
                {
                    var session = await context.AuthenticateAsync(
                        CookieAuthenticationDefaults.AuthenticationScheme);

                    var tenantId = session.Principal?.GetTenantId() is { Length: > 0 } signedInTenant
                        ? signedInTenant
                        : null;

                    var request = context.Request;

                    var started = flow.Start(
                        tenantId,
                        new Uri($"{request.Scheme}://{request.Host}{TenantConsentFlow.CallbackPath}"));

                    context.Response.Cookies.Append(
                        TenantConsentFlow.StateCookieName,
                        started.StateCookie,
                        StateCookie(environment, request));

                    // Where to put the browser once Microsoft has come back. The Tenant Overview
                    // for an ordinary navigation; the tab's auth-end page when the Teams tab ran
                    // this in a popup, because a popup that lands anywhere else never reports back
                    // and Teams eventually calls it cancelled.
                    //
                    // Its own cookie rather than a fourth field in the protected state, so a flow
                    // already in the air when a deploy lands is not refused on a payload shape.
                    // Local paths only, checked again on the way out: a value read from a cookie is
                    // a value from the client, and an unchecked one here is an open redirect.
                    if (AuthEndpoints.IsLocalReturnUrl(returnUrl))
                    {
                        context.Response.Cookies.Append(
                            ReturnCookieName,
                            returnUrl!,
                            StateCookie(environment, request));
                    }

                    return Results.Redirect(started.Url.ToString());
                })
            .AllowAnonymous()
            .WithName("StartTenantConsent");

        // Where Microsoft comes back. Anonymous, because it is a redirect from another origin and
        // what makes it believable is the state cookie rather than a session — and because an
        // administrator who lost their session on the way would otherwise be shown a 401 in a
        // browser tab instead of the screen they started from.
        app.MapGet(
                TenantConsentFlow.CallbackPath,
                async (
                    HttpContext context,
                    TenantConsentFlow flow,
                    ITenantConsentStore consent,
                    IHostEnvironment environment,
                    ILoggerFactory loggerFactory,
                    CancellationToken cancellationToken,
                    string? admin_consent = null,
                    string? tenant = null,
                    string? state = null,
                    string? error = null) =>
                {
                    // Read once and cleared either way: the state is good for one round trip.
                    var stateCookie = context.Request.Cookies[TenantConsentFlow.StateCookieName];
                    context.Response.Cookies.Delete(
                        TenantConsentFlow.StateCookieName,
                        StateCookie(environment, context.Request));

                    var returnUrl = context.Request.Cookies[ReturnCookieName];
                    context.Response.Cookies.Delete(ReturnCookieName, StateCookie(environment, context.Request));

                    var startedFlow = flow.Started(stateCookie, state);
                    var startedFor = flow.TenantStartedFor(stateCookie, state, tenant);

                    var approved = error is null
                        && string.Equals(admin_consent, "True", StringComparison.OrdinalIgnoreCase);

                    // Two different questions, and conflating them is how a forged tenant would get
                    // itself reported as an approval that landed: what may be recorded is an
                    // approval on a flow that named its tenant and got the same one back, and what
                    // may be said is that plus a flow which named no tenant to compare — one that
                    // began with nobody signed in, and can therefore be believed about nothing but
                    // itself. A callback naming somebody else's tenant is neither.
                    var recordFor = approved ? startedFor : null;

                    var accepted = approved
                        && (startedFor is not null || startedFlow is { TenantId: null });

                    // "Anything but success records nothing" — and admin_consent=True alone is not
                    // success. A real decline arrives as admin_consent=True&error=consent_required
                    // (AADSTS65004) with no tenant parameter, so that flag only says which flow this
                    // was. Approval is the absence of an error on a state that matches and names
                    // the tenant the flow started for — which is strictly more than what is needed
                    // to tell somebody what happened, and deliberately so: a flow that began
                    // without a session binds to no tenant, so it can be reported and never
                    // recorded.
                    if (recordFor is not null)
                    {
                        await consent.RecordGrantAsync(recordFor, cancellationToken);
                    }

                    Log(loggerFactory, recordFor, accepted, startedFlow, error, tenant, state, stateCookie);

                    // Back to the screen the link was on, whatever happened. On success the
                    // invitation is gone, which is the feedback; on anything else it is still there,
                    // which is also the truth.
                    // The outcome travels with it. For the Teams tab that return URL is the
                    // popup's auth-end document, which reports back to the tab — and a popup that
                    // reported success for an approval nobody granted would have the tab redraw the
                    // same invitation with no explanation.
                    if (AuthEndpoints.IsLocalReturnUrl(returnUrl))
                    {
                        return Results.Redirect(QueryHelpers.AddQueryString(
                            returnUrl!,
                            ConsentParameter,
                            accepted ? Granted : NotGranted));
                    }

                    // A flow that began without a session has no screen of its own to go back to —
                    // the Tenant Overview is behind a sign-in, and arriving at a sign-in prompt is
                    // no answer to "did that work?". So the answer travels to the front door, where
                    // the card that sent them reads it.
                    return Results.Redirect(startedFlow is { TenantId: null }
                        ? QueryHelpers.AddQueryString(
                            "/",
                            ConsentParameter,
                            accepted ? Granted : NotGranted)
                        : TenantOverviewRoute);
                })
            .AllowAnonymous()
            .WithName("CompleteTenantConsent");

        return app;
    }

    /// <summary>
    /// One line per consent callback, so an approval attempt that failed leaves a trace somewhere
    /// other than the administrator's browser.
    /// <para>
    /// Logging, not domain storage: ADR-0008's "anything but success records nothing" concerns the
    /// grant table and is untouched. What is written here is observability over untrusted input —
    /// every parameter on this callback is forgeable by anybody who can reach it — so it is never
    /// proof, never a trigger for behaviour, and every value that came from the caller goes
    /// through <see cref="LogSafe"/> so a crafted <c>error_description</c> cannot forge a line of
    /// its own.
    /// </para>
    /// </summary>
    private static void Log(
        ILoggerFactory loggerFactory,
        string? recordedFor,
        bool accepted,
        StartedConsentFlow? startedFlow,
        string? error,
        string? tenant,
        string? state,
        string? stateCookie)
    {
        var logger = loggerFactory.CreateLogger(typeof(OnboardingEndpoints));

        if (recordedFor is not null)
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation(
                    "Tenant Consent was approved for tenant {TenantId} and recorded.",
                    recordedFor);
            }

            return;
        }

        if (accepted)
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation(
                    "Tenant Consent was approved on a round trip that began with nobody signed in, "
                    + "so nothing was recorded: the state named no tenant to check the answer "
                    + "against. Whoever approved will stop seeing the invitation until somebody "
                    + "signs in and approves from inside the product.");
            }

            return;
        }

        // A warning rather than information: somebody set out to approve TodoWerk for an
        // organisation and did not arrive. Whether that was a decline, an expired state or a
        // callback nobody started, the operator's question is the same one — "did last night's
        // approval attempt work, and if not, why?" — and this is the only place it can be asked.
        if (logger.IsEnabled(LogLevel.Warning))
        {
            logger.LogWarning(
                "A Tenant Consent callback did not end in an approval. Microsoft answered "
                + "{Error}; a state was {StatePresent} and {StateMatched} one this deployment "
                + "issued; a tenant was {TenantNamed}. Every value here is the caller's and is "
                + "evidence rather than proof.",
                LogSafe.Scalar(error) ?? "no error",
                string.IsNullOrEmpty(state) && string.IsNullOrEmpty(stateCookie) ? "absent" : "present",
                startedFlow is null ? "did not match" : "matched one this deployment issued",
                TenantSaid(startedFlow, tenant));
        }
    }

    /// <summary>
    /// What the callback said about a tenant, in the words an operator needs: a redirect naming a
    /// different tenant than the flow began for is the impersonation Microsoft's documentation
    /// warns about, and it must not read like an ordinary decline.
    /// </summary>
    private static string TenantSaid(StartedConsentFlow? startedFlow, string? tenant) =>
        string.IsNullOrEmpty(tenant)
            ? "not named"
            : startedFlow is { TenantId: { } began }
                && !string.Equals(began, tenant, StringComparison.OrdinalIgnoreCase)
                    ? "named, and not the one the flow began for"
                    : "named";

    /// <summary>
    /// Whether the caller is the SPA's fetch rather than a browser following a form. Erasure is the
    /// one control with both kinds of caller, and they need different answers: only a navigation
    /// can follow the redirect to the identity provider's end-session endpoint.
    /// </summary>
    private static bool WantsJson(HttpRequest request) =>
        request.Headers.Accept.Any(value =>
            value?.Contains("application/json", StringComparison.OrdinalIgnoreCase) == true);

    /// <summary>
    /// The state cookie's shape. <c>Lax</c> because the callback is a top-level navigation from
    /// Microsoft and <c>Strict</c> would drop it on the way in. Deliberately not the session
    /// cookie's <c>None</c>: this one never travels inside the Teams frame — the consent flow it
    /// belongs to runs in a popped-out window from start to finish — so it keeps the stricter
    /// setting the session cookie had to give up (ADR-0010). Scoped to the callback path so it
    /// travels with nothing else, and marked with the same secure policy every other cookie here
    /// follows.
    /// </summary>
    private static CookieOptions StateCookie(IHostEnvironment environment, HttpRequest request) => new()
    {
        HttpOnly = true,
        SameSite = SameSiteMode.Lax,
        Secure = SecureCookiePolicy.For(environment) is not CookieSecurePolicy.SameAsRequest
            || request.IsHttps,
        Path = TenantConsentFlow.CallbackPath,
    };
}
