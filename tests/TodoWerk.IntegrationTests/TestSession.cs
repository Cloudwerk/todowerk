using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace TodoWerk.IntegrationTests;

/// <summary>
/// A signed-in browser, without a sign-in. Mints the session cookie the real cookie handler
/// accepts and fetches the antiforgery pair the SPA gets on any safe request, so a test that is
/// about something else does not have to re-enact both.
/// </summary>
internal static class TestSession
{
    internal const string SessionCookieName = "todowerk.session";

    internal const string AntiforgeryCookieName = "todowerk.antiforgery";

    internal const string RequestTokenCookieName = "XSRF-TOKEN";

    internal const string AntiforgeryFieldName = "__RequestVerificationToken";

    /// <summary>
    /// The claims matter: <c>uid</c> and <c>utid</c> are what MSAL resolves the token cache entry
    /// from, and a cookie without them authenticates fine and then fails every Graph call.
    /// </summary>
    /// <param name="objectId">
    /// Whose session this is. Defaults to the one user the Entra fake ever signs in; a test that
    /// is about two people in one tenant — which is the whole of the per-person licence model —
    /// names a second one.
    /// </param>
    internal static string ProtectTicket(TodoWerkWebApplicationFactory factory, string? objectId = null)
    {
        var user = objectId ?? FakeEntraAndGraphHandler.UserObjectId;

        var options = factory.Services
            .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme);

        var identity = new ClaimsIdentity(
            [
                new Claim("uid", user),
                new Claim("utid", TodoWerkWebApplicationFactory.TenantId),
                new Claim("oid", user),
                new Claim("tid", TodoWerkWebApplicationFactory.TenantId),
                new Claim("preferred_username", "signed-in@todowerk.test"),
                new Claim(ClaimTypes.Name, "Signed In"),
            ],
            CookieAuthenticationDefaults.AuthenticationScheme,
            ClaimTypes.Name,
            ClaimTypes.Role);

        return options.TicketDataFormat.Protect(new AuthenticationTicket(
            new ClaimsPrincipal(identity),
            CookieAuthenticationDefaults.AuthenticationScheme));
    }

    /// <summary>
    /// The antiforgery cookie and request token, obtained the way the SPA obtains them: on a safe
    /// request, from the middleware that publishes them.
    /// </summary>
    internal static async Task<(string Cookie, string RequestToken)> GetAntiforgeryAsync(
        HttpClient client,
        string session,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add("Cookie", $"{SessionCookieName}={session}");

        using var response = await client.SendAsync(request, cancellationToken);

        return (
            ReadSetCookie(response, AntiforgeryCookieName),
            Uri.UnescapeDataString(ReadSetCookie(response, RequestTokenCookieName)));
    }

    /// <summary>The value of one <c>Set-Cookie</c> header, or empty when it was not sent.</summary>
    internal static string ReadSetCookie(HttpResponseMessage response, string name)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var cookies))
        {
            return string.Empty;
        }

        var prefix = $"{name}=";
        var match = cookies.FirstOrDefault(cookie => cookie.StartsWith(prefix, StringComparison.Ordinal));

        if (match is null)
        {
            return string.Empty;
        }

        var value = match[prefix.Length..];
        var end = value.IndexOf(';', StringComparison.Ordinal);

        return end < 0 ? value : value[..end];
    }
}
