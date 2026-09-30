using System.Net;
using Xunit;

namespace TodoWerk.IntegrationTests;

/// <summary>
/// Sign-in as a browser really performs it: a challenge, an authorization request, and a callback
/// that redeems a code — through the application's own OpenID Connect handler and its own validation.
/// <para>
/// A suite that only minted session cookies would never verify that Entra ID returning a ticket
/// led anywhere at all. Several things happen <em>because</em> somebody signed in, and a wiring
/// that was never exercised would zero every one of them silently.
/// </para>
/// </summary>
public sealed class SignInFlowTests(SqlServerDatabaseFixture database) : IClassFixture<SqlServerDatabaseFixture>
{
    [Fact]
    public async Task SignIn_CompletedEndToEnd_ProducesASessionTheApplicationAuthenticates()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var host = await SignInTestHost.StartAsync(database.ConnectionString, cancellationToken);

        var session = await host.SignInAsync(cancellationToken);

        Assert.False(string.IsNullOrEmpty(session), "the completed sign-in issued no session cookie");

        // The session is only worth having if the application accepts it on a later request, so the
        // assertion is a second request rather than the shape of the cookie.
        var me = await host.GetAsync<SessionUserPayload>("/api/me", session, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        Assert.Equal("Signed In", me.Body?.DisplayName);
    }

    /// <summary>
    /// The correlation cookie is what ties a callback to a challenge this application issued. Without
    /// it the handler has no reason to believe the response was solicited, and a session must not
    /// come out of it — which is the same protection that stops a login-CSRF.
    /// </summary>
    [Fact]
    public async Task SignIn_WithoutTheCorrelationCookie_ProducesNoSession()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var host = await SignInTestHost.StartAsync(database.ConnectionString, cancellationToken);

        var challenge = await TestSignIn.ChallengeAsync(host.Client, "/", cancellationToken);

        var session = await TestSignIn.CallbackAsync(
            host.Client,
            challenge.State,
            FakeEntraAndGraphHandler.AuthorizationCodeFor(challenge.Nonce),
            cookies: string.Empty,
            cancellationToken);

        Assert.True(string.IsNullOrEmpty(session), "a callback with no correlation cookie signed somebody in");
    }

    /// <summary>
    /// A tampered state does not check out against the correlation cookie either, and is the other
    /// half of the same protection: the cookie names one authorization request, and the state has to
    /// be the one that request carried.
    /// </summary>
    [Fact]
    public async Task SignIn_WithAStateThatWasNotTheOneIssued_ProducesNoSession()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var host = await SignInTestHost.StartAsync(database.ConnectionString, cancellationToken);

        var challenge = await TestSignIn.ChallengeAsync(host.Client, "/", cancellationToken);

        var session = await TestSignIn.CallbackAsync(
            host.Client,
            state: "CfDJ8-not-a-state-this-application-issued",
            FakeEntraAndGraphHandler.AuthorizationCodeFor(challenge.Nonce),
            challenge.Cookies,
            cancellationToken);

        Assert.True(string.IsNullOrEmpty(session), "a callback carrying an invented state signed somebody in");
    }

    /// <summary>
    /// What the nonce does and does not do here, pinned deliberately rather than assumed.
    /// <para>
    /// TodoWerk runs pure authorization-code flow: the id token arrives from the token endpoint, not
    /// through the browser. ASP.NET Core still mints a nonce, sends it, and reads its cookie back —
    /// and with <c>RequireNonce</c> on — but on this path nothing compares the two. A callback whose
    /// token repeats another flow's nonce, or carries none at all, authenticates anybody the code
    /// redemption identified. Measured, not read: both were tried.
    /// </para>
    /// <para>
    /// It is not a hole TodoWerk opened, and it is not one to plug by hand-rolling protocol
    /// validation in an event handler. What actually protects this callback is the correlation cookie
    /// above, PKCE, and the fact that the identity comes from a single-use code redeemed server-side
    /// with the client secret rather than from a token a browser handed over. The nonce matters in
    /// the hybrid and implicit flows, which TodoWerk does not use.
    /// </para>
    /// <para>
    /// The test asserts the weaker truth on purpose, so nobody reads the nonce cookie in the traffic
    /// and concludes it is load-bearing. Should a framework upgrade start enforcing it, this fails —
    /// and that failure is good news to be acted on rather than a regression.
    /// </para>
    /// </summary>
    [Fact]
    public async Task SignIn_WithAnIdTokenRepeatingTheWrongNonce_StillProducesASession_BecauseCodeFlowDoesNotCompareIt()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var host = await SignInTestHost.StartAsync(database.ConnectionString, cancellationToken);

        var challenge = await TestSignIn.ChallengeAsync(host.Client, "/", cancellationToken);

        var session = await TestSignIn.CallbackAsync(
            host.Client,
            challenge.State,
            // Everything else about this callback is in order: the state matches, the correlation
            // cookie travels, the token is signed by the key the host trusts. Only the nonce is
            // another flow's.
            FakeEntraAndGraphHandler.AuthorizationCodeFor("a-nonce-from-somewhere-else"),
            challenge.Cookies,
            cancellationToken);

        Assert.False(
            string.IsNullOrEmpty(session),
            "the nonce is now being enforced on the authorization-code path. That is an improvement: "
            + "delete this test, and assert that a mismatched nonce produces no session instead.");
    }

    /// <summary>Mirrors <c>SessionUserResponse</c>; asserted on so a rename cannot pass unnoticed.</summary>
    private sealed record SessionUserPayload(string DisplayName, string Username);
}
