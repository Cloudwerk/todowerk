using System.Buffers.Text;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace TodoWerk.IntegrationTests;

/// <summary>
/// Stands in for both clouds the backend talks to: Entra ID's token endpoint (plus the
/// discovery documents MSAL fetches on the way there) and Microsoft Graph. Everything is
/// served from canned responses keyed on the request URL, and every token grant and Graph
/// authorization header lands in the shared <see cref="EntraAndGraphRecorder"/> so a test can
/// assert on which flow actually ran — no test contacts a live tenant, ever
/// (CONTRIBUTING § Testing).
/// </summary>
internal sealed class FakeEntraAndGraphHandler(
    EntraAndGraphRecorder recorder,
    FakeTodoTenant? tenant = null,
    string? refuseOnBehalfOfWith = null) : HttpMessageHandler
{
    /// <summary>The grant type an on-behalf-of exchange arrives as.</summary>
    internal const string OnBehalfOfGrantType = "urn:ietf:params:oauth:grant-type:jwt-bearer";

    /// <summary>
    /// What Entra ID answers a tenant that has not consented to the Graph scopes: the shape MSAL
    /// turns into an <c>MsalUiRequiredException</c> classified as consent-required. Real, not
    /// invented — <c>invalid_grant</c> with <c>consent_required</c> as the sub-error and AADSTS65001
    /// in the description is what the documented failure looks like.
    /// </summary>
    internal const string ConsentRequiredError = """
        {"error":"invalid_grant","suberror":"consent_required","error_description":"AADSTS65001: The user or administrator has not consented to use the application with ID '11111111-1111-1111-1111-111111111111'.","error_codes":[65001]}
        """;

    /// <summary>
    /// What Entra ID answers when the client credential itself is wrong. Nothing a consent screen
    /// fixes, and the tab must not offer one — which is the whole point of testing it.
    /// </summary>
    internal const string InvalidClientError = """
        {"error":"invalid_client","error_description":"AADSTS7000215: Invalid client secret provided.","error_codes":[7000215]}
        """;

    /// <summary>
    /// The mailbox behind Graph. A test that only cares about tokens gets the default one — a
    /// single empty list — and never has to know this exists.
    /// </summary>
    private readonly FakeTodoTenant _tenant = tenant ?? DefaultTenant();

    /// <summary>Issued when a code is redeemed. Expires almost immediately (see <c>expires_in</c>).</summary>
    public const string SeededAccessToken = "seeded-access-token";

    /// <summary>Issued only for <c>grant_type=refresh_token</c> — seeing this at Graph proves the refresh flow ran.</summary>
    public const string RefreshedAccessToken = "refreshed-access-token";

    public const string RefreshTokenValue = "fake-refresh-token-payload";

    public const string TaskListDisplayName = "Fake To Do List";

    /// <summary>
    /// The object id of the one user this fake ever signs in. MSAL keys the cache entry on
    /// <c>uid.utid</c> from <c>client_info</c>, so a test that wants the entry found must put
    /// this (as <c>uid</c>) and the tenant (as <c>utid</c>) into its session cookie's claims.
    /// </summary>
    public const string UserObjectId = "22222222-2222-2222-2222-222222222222";

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var uri = request.RequestUri
            ?? throw new InvalidOperationException("Outbound request without a URI.");

        if (string.Equals(uri.Host, "login.microsoftonline.com", StringComparison.OrdinalIgnoreCase))
        {
            return await HandleEntraIdAsync(request, uri, cancellationToken);
        }

        if (string.Equals(uri.Host, "graph.microsoft.com", StringComparison.OrdinalIgnoreCase))
        {
            return HandleGraph(request, uri);
        }

        throw new InvalidOperationException(
            $"Unexpected outbound request to {uri} — the fake cloud only answers for Entra ID and Graph.");
    }

    private async Task<HttpResponseMessage> HandleEntraIdAsync(
        HttpRequestMessage request,
        Uri uri,
        CancellationToken cancellationToken)
    {
        if (uri.AbsolutePath.Contains("/discovery/instance", StringComparison.OrdinalIgnoreCase))
        {
            return Json(InstanceDiscoveryDocument());
        }

        if (uri.AbsolutePath.EndsWith("/.well-known/openid-configuration", StringComparison.OrdinalIgnoreCase))
        {
            return Json(OpenIdConfigurationDocument());
        }

        if (uri.AbsolutePath.Contains("/discovery/v2.0/keys", StringComparison.OrdinalIgnoreCase))
        {
            return Json("""{"keys":[]}""");
        }

        if (uri.AbsolutePath.EndsWith("/oauth2/v2.0/token", StringComparison.OrdinalIgnoreCase))
        {
            var form = await (request.Content?.ReadAsStringAsync(cancellationToken)
                ?? Task.FromResult(string.Empty));
            var grantType = ReadFormValue(form, "grant_type");

            recorder.RecordTokenGrant(grantType);

            // The on-behalf-of exchange, which a test may make fail: the tab's whole first-run
            // path is what Entra ID says when the tenant has not consented, and nothing but a
            // refusal here exercises it.
            if (refuseOnBehalfOfWith is not null
                && string.Equals(grantType, OnBehalfOfGrantType, StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.BadRequest)
                {
                    Content = new StringContent(refuseOnBehalfOfWith, Encoding.UTF8, "application/json"),
                };
            }

            // The nonce rides in on the authorization code rather than through a field on this
            // handler. Real Entra ID remembers which nonce it put in the authorization request;
            // this fake has no memory to remember it in, and a mutable property would be shared
            // state between hosts that come and go — so the caller that redeems a code says which
            // nonce the id token has to carry, and the fake stays a function of its input.
            return Json(TokenResponse(grantType, NonceIn(ReadFormValue(form, "code"))));
        }

        throw new InvalidOperationException($"Unexpected Entra ID request to {uri}.");
    }

    private HttpResponseMessage HandleGraph(HttpRequestMessage request, Uri uri)
    {
        recorder.RecordGraphAuthorization(request.Headers.Authorization?.ToString());

        if (uri.AbsolutePath.EndsWith("/v1.0/me/todo/lists", StringComparison.OrdinalIgnoreCase))
        {
            return Json(_tenant.ListsJson());
        }

        if (uri.AbsolutePath.EndsWith("/tasks/delta", StringComparison.OrdinalIgnoreCase))
        {
            return HandleDelta(uri);
        }

        // .../lists/{listId}/tasks/{taskId} — the write path's read, and the write itself.
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length >= 2 && string.Equals(segments[^2], "tasks", StringComparison.OrdinalIgnoreCase))
        {
            return HandleSingleTask(request, segments);
        }

        throw new InvalidOperationException($"Unexpected Graph request to {uri}.");
    }

    /// <summary>
    /// One task by id: read it, or write its title. The write is a <c>PATCH</c> carrying nothing
    /// but the title, which is what the gateway is meant to send — anything else here would let a
    /// test pass against a request Microsoft To Do would not accept.
    /// </summary>
    private HttpResponseMessage HandleSingleTask(HttpRequestMessage request, string[] segments)
    {
        var taskId = Uri.UnescapeDataString(segments[^1]);
        var listId = Uri.UnescapeDataString(segments[^3]);

        if (_tenant.ShouldThrottleTask(taskId))
        {
            var throttled = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json"),
            };
            throttled.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.Zero);

            return throttled;
        }

        if (request.Method == HttpMethod.Patch)
        {
            var payload = request.Content?.ReadAsStringAsync(CancellationToken.None).GetAwaiter().GetResult() ?? "{}";
            var title = JsonDocument.Parse(payload).RootElement.GetProperty("title").GetString() ?? string.Empty;

            return _tenant.WriteTitle(listId, taskId, title)
                ? Json(TaskJson(taskId, title))
                : NotFound();
        }

        var current = _tenant.TitleOf(listId, taskId);

        return current is null ? NotFound() : Json(TaskJson(taskId, current));
    }

    private static string TaskJson(string taskId, string title) =>
        JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["id"] = taskId,
            ["title"] = title,
            ["status"] = "notStarted",
        });

    private static HttpResponseMessage NotFound() => new(HttpStatusCode.NotFound)
    {
        Content = new StringContent(
            """{"error":{"code":"itemNotFound","message":"The task no longer exists."}}""",
            Encoding.UTF8,
            "application/json"),
    };

    /// <summary>
    /// The delta endpoint, including the two things a scan has to survive: being told to slow
    /// down, and being told its token is too old.
    /// </summary>
    private HttpResponseMessage HandleDelta(Uri uri)
    {
        // .../lists/{listId}/tasks/delta — the id is three segments from the end.
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var listId = Uri.UnescapeDataString(segments[^3]);

        if (!_tenant.HasList(listId))
        {
            return new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent(
                    """{"error":{"code":"itemNotFound","message":"The list no longer exists."}}""",
                    Encoding.UTF8,
                    "application/json"),
            };
        }

        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        var deltaToken = query["$deltatoken"];
        var skip = int.TryParse(query["$skiptoken"], CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;

        if (_tenant.ShouldThrottle(listId))
        {
            var throttled = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json"),
            };
            throttled.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.Zero);

            return throttled;
        }

        if (_tenant.ShouldExpireDeltaToken(listId, deltaToken))
        {
            return new HttpResponseMessage(HttpStatusCode.Gone)
            {
                Content = new StringContent(
                    """{"error":{"code":"resyncRequired","message":"Resync required."}}""",
                    Encoding.UTF8,
                    "application/json"),
            };
        }

        return Json(_tenant.DeltaJson(listId, deltaToken, skip));
    }

    /// <summary>What the token-cache tests see: one list, named, with nothing in it.</summary>
    private static FakeTodoTenant DefaultTenant()
    {
        var tenant = new FakeTodoTenant();
        tenant.AddList("fake-list-id", TaskListDisplayName, "defaultList");

        return tenant;
    }

    /// <summary>
    /// An authorization code carrying the nonce the id token redeemed with it must repeat.
    /// <see cref="NonceIn"/> is the other half; a code without the marker redeems fine and yields
    /// a token with no nonce, which is what a test that never issued one wants.
    /// </summary>
    internal static string AuthorizationCodeFor(string nonce) => $"fake-authorization-code{NonceMarker}{nonce}";

    private const string NonceMarker = "|nonce=";

    private static string? NonceIn(string authorizationCode)
    {
        var marker = authorizationCode.IndexOf(NonceMarker, StringComparison.Ordinal);

        return marker < 0 ? null : authorizationCode[(marker + NonceMarker.Length)..];
    }

    /// <summary>
    /// The response to <c>grant_type=authorization_code</c> carries an access token that is
    /// already inside MSAL's five-minute expiry buffer, so the next acquisition for this
    /// account cannot ride it and must redeem the refresh token — which is the path a host
    /// restart has to prove.
    /// </summary>
    private static string TokenResponse(string grantType, string? nonce)
    {
        var refreshing = string.Equals(grantType, "refresh_token", StringComparison.Ordinal);
        var accessToken = refreshing ? RefreshedAccessToken : SeededAccessToken;
        var expiresIn = refreshing ? 3600 : 60;

        return JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["token_type"] = "Bearer",
            // One grant, and it is the write scope (ADR-0007). The fake says what Entra ID would
            // say, so a silent acquisition here matches the scopes the gateway asks for.
            ["scope"] = "https://graph.microsoft.com/Tasks.ReadWrite openid profile offline_access",
            ["expires_in"] = expiresIn,
            ["ext_expires_in"] = expiresIn,
            ["access_token"] = accessToken,
            ["refresh_token"] = RefreshTokenValue,
            ["id_token"] = IdToken(nonce),
            ["client_info"] = ClientInfo(),
        });
    }

    /// <summary>
    /// Read twice over, by two readers with different standards. MSAL parses it for its claims and
    /// never checks the signature — a confidential client trusts the TLS channel to the token
    /// endpoint. The OpenID Connect middleware, which sees this token when Microsoft.Identity.Web
    /// hands it the redemption result, validates the signature, the issuer, the audience and the
    /// nonce. So it is really signed, with the key the host is told to trust
    /// (<see cref="FakeEntraSigning"/>).
    /// </summary>
    private static string IdToken(string? nonce)
    {
        var now = DateTimeOffset.UtcNow;

        var claims = new Dictionary<string, object>
        {
            ["oid"] = UserObjectId,
            ["tid"] = TodoWerkWebApplicationFactory.TenantId,
            ["sub"] = "fake-pairwise-subject",
            ["preferred_username"] = "signed-in@todowerk.test",
            ["name"] = "Signed In",
            ["ver"] = "2.0",
        };

        // Absent rather than empty when nobody asked for one: a refresh-token grant carries no
        // nonce, and an empty claim would be a different thing from a missing one.
        if (!string.IsNullOrEmpty(nonce))
        {
            claims["nonce"] = nonce;
        }

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = $"https://login.microsoftonline.com/{TodoWerkWebApplicationFactory.TenantId}/v2.0",
            Audience = TodoWerkWebApplicationFactory.ClientId,
            IssuedAt = now.AddMinutes(-1).UtcDateTime,
            NotBefore = now.AddMinutes(-1).UtcDateTime,
            Expires = now.AddHours(1).UtcDateTime,
            Claims = claims,
            SigningCredentials = FakeEntraSigning.Credentials,
        });
    }

    /// <summary>
    /// <c>uid.utid</c> is what MSAL keys the account — and therefore the cache entry — on, so
    /// these must match the claims a test puts in its session cookie.
    /// </summary>
    private static string ClientInfo() => EncodeBase64Url(
        $$"""{"uid":"{{UserObjectId}}","utid":"{{TodoWerkWebApplicationFactory.TenantId}}"}""");

    private static string InstanceDiscoveryDocument() => $$"""
        {
          "tenant_discovery_endpoint": "https://login.microsoftonline.com/{{TodoWerkWebApplicationFactory.TenantId}}/v2.0/.well-known/openid-configuration",
          "api-version": "1.1",
          "metadata": [
            {
              "preferred_network": "login.microsoftonline.com",
              "preferred_cache": "login.windows.net",
              "aliases": ["login.microsoftonline.com", "login.windows.net", "login.microsoft.com", "sts.windows.net"]
            }
          ]
        }
        """;

    private static string OpenIdConfigurationDocument()
    {
        var authority = $"https://login.microsoftonline.com/{TodoWerkWebApplicationFactory.TenantId}";

        return $$"""
            {
              "issuer": "{{authority}}/v2.0",
              "authorization_endpoint": "{{authority}}/oauth2/v2.0/authorize",
              "token_endpoint": "{{authority}}/oauth2/v2.0/token",
              "jwks_uri": "{{authority}}/discovery/v2.0/keys",
              "response_modes_supported": ["query", "fragment", "form_post"],
              "response_types_supported": ["code", "id_token", "code id_token", "id_token token"],
              "scopes_supported": ["openid", "profile", "email", "offline_access"],
              "subject_types_supported": ["pairwise"],
              "id_token_signing_alg_values_supported": ["RS256"],
              "token_endpoint_auth_methods_supported": ["client_secret_post", "client_secret_basic"]
            }
            """;
    }

    private static string ReadFormValue(string form, string key)
    {
        foreach (var pair in form.Split('&'))
        {
            var separator = pair.IndexOf('=', StringComparison.Ordinal);

            if (separator > 0 && Uri.UnescapeDataString(pair[..separator]) == key)
            {
                return Uri.UnescapeDataString(pair[(separator + 1)..]);
            }
        }

        return string.Empty;
    }

    private static string EncodeBase64Url(string value) => Base64Url.EncodeToString(Encoding.UTF8.GetBytes(value));

    private static HttpResponseMessage Json(string payload) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(payload, Encoding.UTF8, "application/json"),
    };
}
