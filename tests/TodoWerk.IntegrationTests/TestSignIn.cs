using System.Web;

namespace TodoWerk.IntegrationTests;

/// <summary>
/// An interactive sign-in, walked end to end: the challenge, the authorization request it
/// redirects to, and the callback that redeems the code — through the application's own OpenID
/// Connect handler, with the correlation cookie and the nonce it issued travelling back exactly
/// as a browser would carry them.
/// <para>
/// <see cref="TestSession"/> is the other half of the pair and stays: a test that is about
/// something else mints a session cookie and skips all of this. This one exists because several
/// things now happen <em>because</em> somebody signed in, and a suite that never signs in cannot
/// tell a working wiring from a missing one.
/// </para>
/// </summary>
internal static class TestSignIn
{
    /// <summary>
    /// Completes a sign-in and returns the <c>Cookie</c> header value holding the session it
    /// produced — several cookies when the ticket was chunked, which is why this is a header
    /// fragment rather than one value.
    /// </summary>
    internal static async Task<string> CompleteAsync(
        HttpClient client,
        CancellationToken cancellationToken,
        string returnUrl = "/")
    {
        var challenge = await ChallengeAsync(client, returnUrl, cancellationToken);

        return await CallbackAsync(
            client,
            challenge.State,
            FakeEntraAndGraphHandler.AuthorizationCodeFor(challenge.Nonce),
            challenge.Cookies,
            cancellationToken);
    }

    /// <summary>
    /// The first leg on its own, for the tests that then tamper with what comes back: the state,
    /// the nonce Entra ID is expected to echo, and the correlation cookies the handler set.
    /// </summary>
    internal static async Task<AuthorizationRequest> ChallengeAsync(
        HttpClient client,
        string returnUrl,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/auth/sign-in?returnUrl={Uri.EscapeDataString(returnUrl)}");

        using var response = await client.SendAsync(request, cancellationToken);

        var location = response.Headers.Location
            ?? throw new InvalidOperationException(
                $"/auth/sign-in answered {(int)response.StatusCode} with no redirect; expected an "
                + "authorization request to the identity provider.");

        var query = HttpUtility.ParseQueryString(location.Query);

        return new AuthorizationRequest(
            query["state"] ?? throw new InvalidOperationException("The authorization request carried no state."),
            query["nonce"] ?? throw new InvalidOperationException("The authorization request carried no nonce."),
            CookieHeader(response));
    }

    /// <summary>
    /// The callback leg: the form POST Entra ID makes to <c>/signin-oidc</c> under
    /// <c>response_mode=form_post</c>, carrying whatever cookies the caller chose to send.
    /// </summary>
    /// <returns>
    /// The session cookie header the callback issued, or an empty string when it issued none —
    /// which is the assertable outcome for a sign-in that failed validation.
    /// </returns>
    internal static async Task<string> CallbackAsync(
        HttpClient client,
        string state,
        string authorizationCode,
        string cookies,
        CancellationToken cancellationToken)
    {
        using var response = await CallbackResponseAsync(
            client,
            state,
            authorizationCode,
            cookies,
            cancellationToken);

        return SessionCookieHeader(response);
    }

    /// <summary>
    /// The same leg, kept whole, for a test whose subject is the answer rather than the session —
    /// a callback that cannot succeed still has to end somewhere a person can read.
    /// </summary>
    internal static async Task<HttpResponseMessage> CallbackResponseAsync(
        HttpClient client,
        string state,
        string authorizationCode,
        string cookies,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/signin-oidc")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["code"] = authorizationCode,
                ["state"] = state,
            }),
        };

        if (!string.IsNullOrEmpty(cookies))
        {
            request.Headers.Add("Cookie", cookies);
        }

        return await client.SendAsync(request, cancellationToken);
    }

    /// <summary>
    /// The callback Entra ID makes when nobody approved anything: the same form POST, carrying an
    /// error where a code would be. This is the shape a declined consent screen comes back in, and
    /// the response is the subject rather than the session — there is no session in it.
    /// </summary>
    internal static async Task<HttpResponseMessage> CallbackErrorAsync(
        HttpClient client,
        string state,
        string error,
        string errorDescription,
        string cookies,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/signin-oidc")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["error"] = error,
                ["error_description"] = errorDescription,
                ["state"] = state,
            }),
        };

        if (!string.IsNullOrEmpty(cookies))
        {
            request.Headers.Add("Cookie", cookies);
        }

        return await client.SendAsync(request, cancellationToken);
    }

    /// <summary>
    /// Every cookie a response set, as one <c>Cookie</c> header. Names and values only: the
    /// attributes are the browser's business, and this is standing in for the browser.
    /// </summary>
    private static string CookieHeader(HttpResponseMessage response) =>
        string.Join("; ", SetCookiePairs(response, prefix: null));

    /// <summary>
    /// Only the session cookies, so a test can tell "signed in" from "the handler answered but
    /// authenticated nobody". Chunked tickets arrive as <c>todowerk.sessionC1</c>,
    /// <c>todowerk.sessionC2</c> and so on, so this matches on the prefix rather than the name.
    /// </summary>
    private static string SessionCookieHeader(HttpResponseMessage response) =>
        string.Join("; ", SetCookiePairs(response, TestSession.SessionCookieName));

    private static List<string> SetCookiePairs(HttpResponseMessage response, string? prefix)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var cookies))
        {
            return [];
        }

        return cookies
            .Select(cookie =>
            {
                var end = cookie.IndexOf(';', StringComparison.Ordinal);

                return end < 0 ? cookie : cookie[..end];
            })
            // A cookie the response deleted comes back as an empty value with an expiry in the
            // past. Carrying it forward would send "" for a cookie the handler still needs.
            .Where(pair => !pair.EndsWith('=')
                && (prefix is null || pair.StartsWith(prefix, StringComparison.Ordinal)))
            .ToList();
    }

    /// <summary>What the challenge produced, and what the callback has to be given back.</summary>
    /// <param name="State">The opaque state the handler will match its correlation cookie against.</param>
    /// <param name="Nonce">The nonce the id token must repeat, or the sign-in is refused.</param>
    /// <param name="Cookies">The correlation and nonce cookies, as a <c>Cookie</c> header.</param>
    internal sealed record AuthorizationRequest(string State, string Nonce, string Cookies);
}
