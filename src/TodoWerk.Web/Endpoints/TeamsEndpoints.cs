using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using TodoWerk.Infrastructure.Authentication;
using TodoWerk.Web.Security;

namespace TodoWerk.Web.Endpoints;

/// <summary>
/// The Teams surface: two documents and the one request that turns a Teams identity into a
/// TodoWerk session.
/// </summary>
internal static class TeamsEndpoints
{
    public static IEndpointRouteBuilder MapTeamsEndpoints(this IEndpointRouteBuilder app)
    {
        // The tab's own document. Anonymous, because a tab that answered 401 would render
        // Microsoft's error frame rather than TodoWerk's — the document loads, TeamsJS runs, and
        // the exchange below is what decides whether there is a session.
        app.MapGet(TeamsTab.BasePath, (IWebHostEnvironment environment) =>
                Document(environment, TeamsTab.TabDocument))
            .AllowAnonymous()
            .WithName("TeamsTab");

        // The consent popup's last stop. It initializes TeamsJS, calls notifySuccess() and does
        // nothing else — a slow document here is not a slow page, it is a CancelledByUser, because
        // Teams gives up on a popup that takes too long to report back.
        app.MapGet(TeamsTab.AuthEndPath, (IWebHostEnvironment environment) =>
                Document(environment, TeamsTab.AuthEndDocument))
            .AllowAnonymous()
            .WithName("TeamsAuthEnd");

        // The tab calls this once, on load, with the token getAuthToken() gave it. What comes back
        // is a session cookie and no body at all: no token of any kind reaches the client on any
        // path (ADR-0002).
        app.MapPost(
                "/api/teams/session",
                async (
                    HttpContext context,
                    TeamsSsoSignIn signIn,
                    CancellationToken cancellationToken) =>
                {
                    var exchanged = await signIn.ExchangeAsync(context.User, cancellationToken);

                    if (exchanged.IsFailure)
                    {
                        return exchanged.Error.ToProblem();
                    }

                    await context.SignInAsync(
                        CookieAuthenticationDefaults.AuthenticationScheme,
                        exchanged.Value);

                    return Results.NoContent();
                })
            .RequireAuthorization(AuthorizationPolicies.TeamsSso)
            // The exchange itself is not gated on a Licence. Being unable to sign in and being
            // unlicensed are different things with different cards, and a tab that failed here
            // would show the first when the truth is the second — with no route to erasure, which
            // lives inside the shell this exchange is what opens.
            .AllowUnlicensed()
            .AntiforgeryNotApplicable(
                "The only credential this endpoint accepts is a Teams SSO token in the "
                + "Authorization header, which a cross-site page cannot attach and cannot obtain. "
                + "Requiring the double-submit pair here would also break the one diagnosis that "
                + "depends on this endpoint succeeding — Safari blocks the cookie in the frame "
                + "outright, so the antiforgery cookie would be absent and every Safari tab would "
                + "fail here as a forgery instead of reaching the card that explains itself.")
            .WithName("ExchangeTeamsSsoToken");

        return app;
    }

    /// <summary>
    /// One of the two built entry documents, by path under the web root. A missing file is a
    /// deployment that did not build the client, and it says so rather than answering the tab with
    /// an empty 200 that TeamsJS would sit in forever.
    /// </summary>
    private static IResult Document(IWebHostEnvironment environment, string relativePath)
    {
        var file = environment.WebRootFileProvider.GetFileInfo(relativePath);

        return file.Exists
            ? Results.File(file.CreateReadStream(), "text/html; charset=utf-8")
            : Results.NotFound();
    }
}
