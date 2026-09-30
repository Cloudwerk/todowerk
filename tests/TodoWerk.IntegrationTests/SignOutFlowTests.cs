using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace TodoWerk.IntegrationTests;

/// <summary>
/// Sign-out as the browser actually performs it: a form POST carrying the antiforgery token,
/// from a session that is really signed in.
/// <para>
/// Every other test in this project stops at the boundary — 401 without a session, a challenge
/// that redirects — so this is where the one mutating endpoint in the application is exercised by
/// a caller who is signed in. A signed-in sign-out with its token must succeed, not answer 400
/// "Invalid antiforgery token".
/// </para>
/// </summary>
public sealed class SignOutFlowTests(TodoWerkWebApplicationFactory factory)
    : IClassFixture<TodoWerkWebApplicationFactory>
{
    private const string SessionCookieName = "todowerk.session";
    private const string AntiforgeryCookieName = "todowerk.antiforgery";
    private const string RequestTokenCookieName = "XSRF-TOKEN";
    private const string FormFieldName = "__RequestVerificationToken";

    [Fact]
    public async Task SignOut_FromASignedInSessionWithItsToken_RedirectsToTheEndSessionEndpoint()
    {
        var logs = new List<string>();

        // A private factory so the recording provider belongs to this test rather than the
        // class fixture every other test shares.
        using var recording = factory.WithWebHostBuilder(builder =>
            builder.ConfigureLogging(logging => logging.AddProvider(new RecordingLoggerProvider(logs))));

        using var client = recording.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,

            // This test is about which cookies travel with which request; handling them by hand
            // keeps that explicit rather than leaving it to a shared container.
            HandleCookies = false,
        });

        var session = ProtectSessionTicket(recording);

        // The safe request that publishes the antiforgery pair, exactly as a cold page load does.
        using var page = new HttpRequestMessage(HttpMethod.Get, "/health");
        page.Headers.Add("Cookie", $"{SessionCookieName}={session}");

        using var pageResponse = await client.SendAsync(page, TestContext.Current.CancellationToken);

        var requestToken = ReadSetCookie(pageResponse, RequestTokenCookieName);
        var antiforgeryCookie = ReadSetCookie(pageResponse, AntiforgeryCookieName);

        Assert.False(string.IsNullOrEmpty(requestToken), $"no {RequestTokenCookieName} cookie was published");
        Assert.False(string.IsNullOrEmpty(antiforgeryCookie), $"no {AntiforgeryCookieName} cookie was published");

        using var signOut = new HttpRequestMessage(HttpMethod.Post, "/auth/sign-out")
        {
            Content = new FormUrlEncodedContent(
                new Dictionary<string, string> { [FormFieldName] = Uri.UnescapeDataString(requestToken) }),
        };
        signOut.Headers.Add(
            "Cookie",
            $"{SessionCookieName}={session}; {AntiforgeryCookieName}={antiforgeryCookie}");

        using var response = await client.SendAsync(signOut, TestContext.Current.CancellationToken);

        Assert.True(
            response.StatusCode == HttpStatusCode.Redirect,
            $"expected a redirect to the end-session endpoint, got {(int)response.StatusCode}. "
            + $"Logged warnings: {string.Join(" | ", logs)}");

        Assert.Contains(
            "login.microsoftonline.com",
            response.Headers.Location!.ToString(),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A session cookie the real cookie handler will accept, produced by the application's own
    /// ticket format. Nothing here bypasses authentication — the handler decrypts and validates
    /// this exactly as it would one issued by a completed sign-in.
    /// </summary>
    private static string ProtectSessionTicket(WebApplicationFactory<Program> factory)
    {
        var options = factory.Services
            .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme);

        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, "00000000-0000-0000-0000-00000000dead"),
                new Claim("oid", "00000000-0000-0000-0000-00000000dead"),
                new Claim("preferred_username", "signed-in@todowerk.test"),
                new Claim(ClaimTypes.Name, "Signed In"),
            ],
            CookieAuthenticationDefaults.AuthenticationScheme,
            ClaimTypes.Name,
            ClaimTypes.Role);

        var ticket = new AuthenticationTicket(
            new ClaimsPrincipal(identity),
            CookieAuthenticationDefaults.AuthenticationScheme);

        return options.TicketDataFormat.Protect(ticket);
    }

    /// <summary>The value of one <c>Set-Cookie</c> header, or empty when it was not sent.</summary>
    private static string ReadSetCookie(HttpResponseMessage response, string name)
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

    /// <summary>
    /// Collects warning-level messages so a failure can report why the framework rejected the
    /// request, rather than leaving a bare status code to interpret.
    /// </summary>
    private sealed class RecordingLoggerProvider(List<string> sink) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new RecordingLogger(categoryName, sink);

        public void Dispose()
        {
        }

        private sealed class RecordingLogger(string category, List<string> sink) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (!IsEnabled(logLevel))
                {
                    return;
                }

                lock (sink)
                {
                    sink.Add($"{category}: {formatter(state, exception)}");
                }
            }
        }
    }
}
