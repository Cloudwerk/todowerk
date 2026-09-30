using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging;
using TodoWerk.Web.Security;
using Xunit;

namespace TodoWerk.IntegrationTests;

/// <summary>
/// What <c>/signin-oidc</c> answers when Entra ID sends back anything but a ticket.
/// <para>
/// A failed callback answers with a card redirect, never HTTP 500 problem JSON. The framework's
/// default for a remote failure is to rethrow, and left unhandled the global exception handler
/// would serve RFC 9110 JSON to a browser — for a declined consent screen, which is not an error
/// at all and is the expected first run of every tenant that has not granted Tenant Consent.
/// </para>
/// <para>
/// No database: nothing on this path reaches one. A callback that fails never redeems a code,
/// never writes a membership row and never touches the token cache, which is what makes this the
/// one sign-in suite that boots in milliseconds.
/// </para>
/// </summary>
public sealed class SignInFailureTests
{
    /// <summary>The AADSTS code Entra ID sends when a consent screen was not approved.</summary>
    private const string UserDeclined =
        "AADSTS65004: User declined to consent to access the app. Send an interactive authorization "
        + "request for this user and resource.";

    private static readonly WebApplicationFactoryClientOptions ClientOptions = new()
    {
        AllowAutoRedirect = false,
        HandleCookies = false,
        BaseAddress = new Uri("https://localhost"),
    };

    /// <summary>
    /// A declined consent screen, answered with somewhere to go rather than with a stack trace's
    /// public face.
    /// </summary>
    [Fact]
    public async Task ADeclinedConsentScreen_SendsThePersonToACardRatherThanAProblemDocument()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        using var factory = Host();
        using var client = factory.CreateClient(ClientOptions);

        var challenge = await TestSignIn.ChallengeAsync(client, "/", cancellationToken);

        using var response = await TestSignIn.CallbackErrorAsync(
            client,
            challenge.State,
            "access_denied",
            UserDeclined,
            challenge.Cookies,
            cancellationToken);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(
            $"/?{SignInFailure.ReasonParameter}={SignInFailure.NotApproved}",
            response.Headers.Location?.ToString());

        // The two things the old answer got wrong, pinned as facts rather than as an absence of
        // the new one: it was a 500, and it was JSON.
        Assert.NotEqual(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.DoesNotContain(
            "json",
            response.Content.Headers.ContentType?.MediaType ?? string.Empty,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A refusal and a breakage are different cards, because they are different sentences: one
    /// says nobody approved this, the other says nothing was refused and the round trip simply did
    /// not finish.
    /// </summary>
    [Fact]
    public async Task ARoundTripThatBrokeRatherThanBeingRefused_AsksForAnotherAttempt()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        using var factory = Host();
        using var client = factory.CreateClient(ClientOptions);

        var challenge = await TestSignIn.ChallengeAsync(client, "/", cancellationToken);

        using var response = await TestSignIn.CallbackErrorAsync(
            client,
            challenge.State,
            "server_error",
            "AADSTS90099: Something at Microsoft's end.",
            challenge.Cookies,
            cancellationToken);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(
            $"/?{SignInFailure.ReasonParameter}={SignInFailure.Failed}",
            response.Headers.Location?.ToString());
    }

    /// <summary>
    /// The other way a callback fails, and the one nobody chose: the correlation cookie did not
    /// come back. Asserting only that no session came out of it would not be enough: a thrown
    /// exception satisfies that as well as an answer does.
    /// </summary>
    [Fact]
    public async Task ACallbackWithoutItsCorrelationCookie_LandsOnTheCardRatherThanThrowing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        using var factory = Host();
        using var client = factory.CreateClient(ClientOptions);

        var challenge = await TestSignIn.ChallengeAsync(client, "/", cancellationToken);

        using var response = await TestSignIn.CallbackResponseAsync(
            client,
            challenge.State,
            FakeEntraAndGraphHandler.AuthorizationCodeFor(challenge.Nonce),
            cookies: string.Empty,
            cancellationToken);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(
            $"/?{SignInFailure.ReasonParameter}={SignInFailure.Failed}",
            response.Headers.Location?.ToString());

        // Still refused, which is the older promise this must not have traded away: a callback
        // nobody solicited signs nobody in, however politely it now says so.
        Assert.DoesNotContain(
            TestSession.SessionCookieName,
            string.Join(';', response.Headers.TryGetValues("Set-Cookie", out var set) ? set : []),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The Teams consent popup runs this same flow, and its return URL is the tab's auth-end
    /// document. A failure that landed anywhere else would leave the popup with nothing to report
    /// and the tab waiting for a window that never speaks.
    /// </summary>
    [Fact]
    public async Task ADeclineInsideTheTeamsPopup_ComesBackToTheDocumentTheTabIsListeningTo()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        using var factory = Host();
        using var client = factory.CreateClient(ClientOptions);

        var challenge = await TestSignIn.ChallengeAsync(client, "/teams/auth-end", cancellationToken);

        using var response = await TestSignIn.CallbackErrorAsync(
            client,
            challenge.State,
            "access_denied",
            UserDeclined,
            challenge.Cookies,
            cancellationToken);

        Assert.Equal(
            $"/teams/auth-end?{SignInFailure.ReasonParameter}={SignInFailure.NotApproved}",
            response.Headers.Location?.ToString());
    }

    /// <summary>
    /// The AADSTS code is the only evidence anywhere of which refusal this was — TodoWerk cannot
    /// tell a decline from a tenant that reserves the decision — so the log line carries it.
    /// </summary>
    [Fact]
    public async Task TheRefusalIsLogged_WithTheCodeEntraIdSent()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var logs = new RecordedLogs();
        using var factory = Host(logs);
        using var client = factory.CreateClient(ClientOptions);

        var challenge = await TestSignIn.ChallengeAsync(client, "/", cancellationToken);

        using var response = await TestSignIn.CallbackErrorAsync(
            client,
            challenge.State,
            "access_denied",
            UserDeclined,
            challenge.Cookies,
            cancellationToken);

        var line = Assert.Single(logs.Records, record =>
            record.Message.Contains("ended without approval", StringComparison.Ordinal));

        Assert.Equal(LogLevel.Information, line.Level);
        Assert.Contains("AADSTS65004", line.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Everything in that line came off the wire, so a crafted description must not be able to
    /// write a second line that reads like this application's own — which applies wherever an
    /// untrusted value is logged.
    /// </summary>
    [Fact]
    public async Task ACraftedErrorDescription_CannotForgeALineOfItsOwn()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var logs = new RecordedLogs();
        using var factory = Host(logs);
        using var client = factory.CreateClient(ClientOptions);

        var challenge = await TestSignIn.ChallengeAsync(client, "/", cancellationToken);

        using var response = await TestSignIn.CallbackErrorAsync(
            client,
            challenge.State,
            "access_denied",
            "AADSTS65004\r\nfail: TodoWerk[0] Everything is fine and the licence is valid.",
            challenge.Cookies,
            cancellationToken);

        var line = Assert.Single(logs.Records, record =>
            record.Message.Contains("ended without approval", StringComparison.Ordinal));

        Assert.DoesNotContain('\n', line.Message);
        Assert.DoesNotContain('\r', line.Message);

        // Bounded rather than dropped: the code is still readable, which is what the line is for.
        Assert.Contains("AADSTS65004", line.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A host with no database and no workers. The failure path reaches neither, and a suite that
    /// asked for a database would spend a minute proving something about SQL Server.
    /// </summary>
    private static TodoWerkWebApplicationFactory Host(RecordedLogs? logs = null)
    {
        var factory = new TodoWerkWebApplicationFactory().WithoutBackgroundWorkers();

        return logs is null ? factory : factory.WithLoggerProvider(logs);
    }
}
