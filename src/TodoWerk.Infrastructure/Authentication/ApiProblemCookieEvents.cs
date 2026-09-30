using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace TodoWerk.Infrastructure.Authentication;

/// <summary>
/// The cookie scheme never redirects. TodoWerk's only entry point to Entra ID is an explicit
/// <c>/auth/sign-in</c> navigation, so a missing or insufficient session on any cookie-protected
/// endpoint is an API condition: 401 or 403 carrying RFC 7807 problem details the SPA can act on.
/// </summary>
internal sealed class ApiProblemCookieEvents : CookieAuthenticationEvents
{
    public override Task RedirectToLogin(RedirectContext<CookieAuthenticationOptions> context) =>
        WriteProblemAsync(
            context.HttpContext,
            StatusCodes.Status401Unauthorized,
            "Not signed in",
            "Sign in to continue.",
            "Auth.SignInRequired");

    public override Task RedirectToAccessDenied(RedirectContext<CookieAuthenticationOptions> context) =>
        WriteProblemAsync(
            context.HttpContext,
            StatusCodes.Status403Forbidden,
            "Access denied",
            "Your account is not allowed to perform this action.",
            "Auth.AccessDenied");

    private static async Task WriteProblemAsync(
        HttpContext httpContext,
        int statusCode,
        string title,
        string detail,
        string code)
    {
        httpContext.Response.StatusCode = statusCode;

        var problemDetailsService = httpContext.RequestServices.GetRequiredService<IProblemDetailsService>();

        await problemDetailsService.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = new ProblemDetails
            {
                Status = statusCode,
                Title = title,
                Detail = detail,
                Extensions = { ["code"] = code },
            },
        });
    }
}
