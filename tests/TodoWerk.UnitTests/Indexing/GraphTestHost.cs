using System.Net;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Abstractions;
using Microsoft.Identity.Client;
using Microsoft.Identity.Web;
using TodoWerk.Application.Indexing;
using TodoWerk.Infrastructure.Indexing;
using TodoWerk.Infrastructure.Graph;
using TodoWerk.Application.Abstractions.Authentication;

namespace TodoWerk.UnitTests.Indexing;

/// <summary>
/// The stand-ins every Graph test here needs: a handler that answers from a script, an
/// authorization header provider that does not talk to Entra ID, and a gateway wired onto them.
/// Shared so the reader tests and the gateway tests cannot drift into testing two different fakes.
/// </summary>
internal sealed class GraphTestHost : IDisposable
{
    internal static readonly IndexUser User = new("tenant-id", "user-id");

    private readonly HttpClient _httpClient;

    /// <summary>
    /// Retries are configured to wait for nothing, so a test can prove the loop runs the right
    /// number of times without spending the seconds Graph would really ask for.
    /// </summary>
    internal GraphTestHost(
        ScriptedHandler handler,
        IAuthorizationHeaderProvider? headerProvider = null,
        int maxThrottleRetries = 0)
    {
        Handler = handler;
        Authorization = headerProvider ?? new StubAuthorizationHeaderProvider();

        _httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://graph.microsoft.com/") };

        Gateway = new GraphGateway(
            _httpClient,
            Authorization,
            // Nobody here signed in through Teams, so the partition a Teams session leaves its
            // tokens in is empty — which is what this says, and what makes a header provider that
            // asks for interaction still come out as "reconnect required" rather than silently
            // finding a token from the other path.
            new EmptyTeamsSessionTokenAcquisition(),
            Options.Create(new GraphOptions
            {
                MaxThrottleRetries = maxThrottleRetries,
                MaxRetryDelay = TimeSpan.Zero,
            }),
            TimeProvider.System,
            NullLogger<GraphGateway>.Instance);
    }

    internal ScriptedHandler Handler { get; }

    internal IAuthorizationHeaderProvider Authorization { get; }

    internal GraphGateway Gateway { get; }

    internal static GraphTestHost Answering(HttpStatusCode statusCode, string json) =>
        new(new ScriptedHandler(statusCode, json));

    public void Dispose() => _httpClient.Dispose();

    /// <summary>Answers each request from a queue of scripted responses, recording what it saw.</summary>
    internal sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Queue<(HttpStatusCode StatusCode, string Json)> _responses;

        internal ScriptedHandler(params (HttpStatusCode StatusCode, string Json)[] responses)
        {
            _responses = new Queue<(HttpStatusCode, string)>(responses);
        }

        internal ScriptedHandler(HttpStatusCode statusCode, string json)
            : this([(statusCode, json)])
        {
        }

        internal List<HttpRequestMessage> Requests { get; } = [];

        internal HttpRequestMessage? LastRequest => Requests.Count == 0 ? null : Requests[^1];

        /// <summary>Thrown instead of answering — how a transport fault or a timeout arrives.</summary>
        internal Exception? Throws { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);

            if (Throws is not null)
            {
                return Task.FromException<HttpResponseMessage>(Throws);
            }

            // The last scripted response repeats, so a retry test does not have to script one
            // entry per attempt.
            var (statusCode, json) = _responses.Count > 1 ? _responses.Dequeue() : _responses.Peek();

            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
        }
    }

    /// <summary>
    /// A token acquisition with nothing in it. The gateway asks it for a token from the Teams
    /// session partition before it gives up on somebody, and every test here is about a person who
    /// signed in through a browser — so the honest answer is the one MSAL gives for an empty
    /// partition.
    /// </summary>
    internal sealed class EmptyTeamsSessionTokenAcquisition : ITokenAcquisition
    {
        public Task<string> GetAccessTokenForUserAsync(
            IEnumerable<string> scopes,
            string? authenticationScheme,
            string? tenantId = null,
            string? userFlow = null,
            ClaimsPrincipal? user = null,
            TokenAcquisitionOptions? tokenAcquisitionOptions = null) =>
            throw new MsalUiRequiredException("no_tokens_found", "No token in this partition.");

        public Task<AuthenticationResult> GetAuthenticationResultForUserAsync(
            IEnumerable<string> scopes,
            string? authenticationScheme,
            string? tenantId = null,
            string? userFlow = null,
            ClaimsPrincipal? user = null,
            TokenAcquisitionOptions? tokenAcquisitionOptions = null) =>
            throw new MsalUiRequiredException("no_tokens_found", "No token in this partition.");

        public Task<string> GetAccessTokenForAppAsync(
            string scope,
            string? authenticationScheme,
            string? tenant = null,
            TokenAcquisitionOptions? tokenAcquisitionOptions = null) =>
            throw new NotSupportedException("TodoWerk holds no app-only credential (ADR-0008).");

        public Task<AuthenticationResult> GetAuthenticationResultForAppAsync(
            string scope,
            string? authenticationScheme,
            string? tenant = null,
            TokenAcquisitionOptions? tokenAcquisitionOptions = null) =>
            throw new NotSupportedException("TodoWerk holds no app-only credential (ADR-0008).");

        public string GetEffectiveAuthenticationScheme(string? authenticationScheme) =>
            authenticationScheme ?? string.Empty;

        public Task ReplyForbiddenWithWwwAuthenticateHeaderAsync(
            IEnumerable<string> scopes,
            MsalUiRequiredException msalServiceException,
            Microsoft.AspNetCore.Http.HttpResponse? httpResponse = null) =>
            Task.CompletedTask;

        public void ReplyForbiddenWithWwwAuthenticateHeader(
            IEnumerable<string> scopes,
            MsalUiRequiredException msalServiceException,
            string? authenticationScheme,
            Microsoft.AspNetCore.Http.HttpResponse? httpResponse = null)
        {
            // Nothing here answers a browser.
        }
    }

    internal class StubAuthorizationHeaderProvider : IAuthorizationHeaderProvider
    {
        /// <summary>The principal the gateway built for the user it was given.</summary>
        internal ClaimsPrincipal? SeenPrincipal { get; private set; }

        public virtual Task<string> CreateAuthorizationHeaderForUserAsync(
            IEnumerable<string> scopes,
            AuthorizationHeaderProviderOptions? downstreamApiOptions = null,
            ClaimsPrincipal? claimsPrincipal = null,
            CancellationToken cancellationToken = default)
        {
            SeenPrincipal = claimsPrincipal;

            return Task.FromResult("Bearer delegated-token");
        }

        public Task<string> CreateAuthorizationHeaderForAppAsync(
            string scopes,
            AuthorizationHeaderProviderOptions? downstreamApiOptions = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult("Bearer app-token");

        public Task<string> CreateAuthorizationHeaderAsync(
            IEnumerable<string> scopes,
            AuthorizationHeaderProviderOptions? options = null,
            ClaimsPrincipal? claimsPrincipal = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult("Bearer delegated-token");
    }

    internal sealed class ChallengingAuthorizationHeaderProvider : StubAuthorizationHeaderProvider
    {
        public override Task<string> CreateAuthorizationHeaderForUserAsync(
            IEnumerable<string> scopes,
            AuthorizationHeaderProviderOptions? downstreamApiOptions = null,
            ClaimsPrincipal? claimsPrincipal = null,
            CancellationToken cancellationToken = default) =>
            throw new MicrosoftIdentityWebChallengeUserException(
                new Microsoft.Identity.Client.MsalUiRequiredException("invalid_grant", "Reconnect needed."),
                [.. scopes]);
    }
}
