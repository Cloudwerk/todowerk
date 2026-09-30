using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Abstractions;
using Microsoft.Identity.Client;
using Microsoft.Identity.Web;
using TodoWerk.Application.Abstractions.Authentication;
using TodoWerk.Infrastructure.Authentication;
using TodoWerk.SharedKernel;

// Microsoft.Identity.Client publishes a LogLevel of its own, and it is not the one this file logs
// through.
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace TodoWerk.Infrastructure.Graph;

/// <summary>
/// Every call TodoWerk makes to Microsoft Graph goes through here: one place that turns an
/// <see cref="IndexUser"/> into a delegated token, sends the request, waits out throttling, and
/// turns whatever comes back into a <see cref="Result{TValue}"/>.
/// <para>
/// The user is explicit rather than ambient, which is what lets the same code serve a request, a
/// background scan and a Change. The token comes from the durable cache keyed on the
/// account identifier these claims produce, so work that outlives the request that triggered it —
/// or the process that started it — still has one.
/// </para>
/// <para>
/// Outside any vertical module: the index reads through it and a Change writes through it, and a
/// second transport for writes would mean a second copy of the <c>Retry-After</c> handling, the
/// one-shot 401 refresh, the host allowlist and the error mapping (ADR-0006).
/// </para>
/// </summary>
internal sealed class GraphGateway(
    HttpClient httpClient,
    IAuthorizationHeaderProvider authorizationHeaderProvider,
    ITokenAcquisition tokenAcquisition,
    IOptions<GraphOptions> options,
    TimeProvider timeProvider,
    ILogger<GraphGateway> logger)
{
    /// <summary>
    /// Graph's code for an expired delta token. It arrives as <c>410 Gone</c>, and means the
    /// list has to be read in full again — not that anything is broken.
    /// </summary>
    private const string ResyncRequiredCode = "resyncRequired";

    public Task<Result<TResponse>> GetAsync<TResponse>(
        IndexUser user,
        string requestUri,
        CancellationToken cancellationToken) =>
        SendAsync<TResponse>(
            user,
            HttpMethod.Get,
            requestUri,
            content: null,
            async (response, token) =>
            {
                var payload = await response.Content.ReadFromJsonAsync<TResponse>(token);

                return payload is null
                    ? Result.Failure<TResponse>(GraphErrors.Unavailable)
                    : Result.Success(payload);
            },
            cancellationToken);

    /// <summary>
    /// Sends one PATCH and reads nothing back. Used for <c>Update todoTask</c>, which takes no
    /// <c>If-Match</c> because <c>todoTask</c> carries no ETag — the caller's re-read immediately
    /// before this call is all the concurrency control the API offers (ADR-0006).
    /// </summary>
    /// <param name="payload">Serialised fresh per attempt: content cannot be sent twice.</param>
    public async Task<Result> PatchAsync(
        IndexUser user,
        string requestUri,
        object payload,
        CancellationToken cancellationToken)
    {
        var sent = await SendAsync(
            user,
            HttpMethod.Patch,
            requestUri,
            () => JsonContent.Create(payload, payload.GetType()),
            // The body of a successful PATCH is the updated task, and nothing here wants it: the
            // caller already knows what it wrote, and reading it would only be a second chance to
            // fail on deserialisation.
            (_, _) => Task.FromResult(Result.Success(true)),
            cancellationToken);

        return sent.IsSuccess ? Result.Success() : Result.Failure(sent.Error);
    }

    /// <summary>
    /// The one request loop: authorise, send, wait out throttling, map the failure. Both verbs go
    /// through it so a write cannot quietly acquire different manners than a read.
    /// </summary>
    private async Task<Result<TResult>> SendAsync<TResult>(
        IndexUser user,
        HttpMethod method,
        string requestUri,
        Func<HttpContent>? content,
        Func<HttpResponseMessage, CancellationToken, Task<Result<TResult>>> readSuccess,
        CancellationToken cancellationToken)
    {
        // Continuation links are stored in the database between passes, and this header is a
        // bearer token: whatever host the link names receives it. Resolved against the base
        // address first and checked afterwards, because that is the address the request will
        // actually go to — "//evil.example/x" is not an absolute URI, so checking the string as
        // given would wave it through and let HttpClient resolve it into another origin.
        var target = Uri.TryCreate(httpClient.BaseAddress, requestUri, out var resolved) ? resolved : null;

        if (target is null || !IsGraphHost(target))
        {
            logger.LogError(
                "Refusing a Graph request to {Host}: only {AllowedHost} may see the delegated token.",
                target?.Host ?? "an address that could not be parsed",
                httpClient.BaseAddress?.Host);

            return Result.Failure<TResult>(GraphErrors.Unavailable);
        }

        var header = await AuthorizeAsync(user, cancellationToken);

        if (header.IsFailure)
        {
            return Result.Failure<TResult>(header.Error);
        }

        var settings = options.Value;
        var reauthorized = false;

        for (var attempt = 0; ; attempt++)
        {
            using var request = new HttpRequestMessage(method, target);
            request.Headers.Authorization = AuthenticationHeaderValue.Parse(header.Value);
            request.Content = content?.Invoke();

            HttpResponseMessage response;

            try
            {
                response = await httpClient.SendAsync(request, cancellationToken);
            }
            catch (Exception exception) when (exception is HttpRequestException
                || (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested))
            {
                // A refused connection, a DNS failure, or the client's own timeout — Graph did
                // not answer. Turned into a result rather than allowed to escape, because a
                // timeout arrives as a cancellation and would otherwise sail straight through
                // the caller's "this list failed on its own" handling and fail the whole scan.
                logger.LogWarning(exception, "Graph did not answer {Path}; treating it as unavailable.", target.AbsolutePath);

                return Result.Failure<TResult>(GraphErrors.Unavailable);
            }

            TimeSpan delay;

            using (response)
            {
                if (response.IsSuccessStatusCode)
                {
                    return await readSuccess(response, cancellationToken);
                }

                // One fresh header before believing a 401. The one in hand can be minutes old — a
                // throttled page waits out several Retry-After pauses — and an expired token
                // answered with "reconnect required" would send the user back through sign-in for
                // something a silent refresh fixes.
                if (response.StatusCode is HttpStatusCode.Unauthorized && !reauthorized)
                {
                    reauthorized = true;
                    header = await AuthorizeAsync(user, cancellationToken);

                    if (header.IsFailure)
                    {
                        return Result.Failure<TResult>(header.Error);
                    }

                    // Not one of the throttle attempts: this round sent no request Graph counted.
                    attempt--;
                    continue;
                }

                if (!IsThrottling(response.StatusCode))
                {
                    return Result.Failure<TResult>(await DescribeFailureAsync(response, cancellationToken));
                }

                if (attempt >= settings.MaxThrottleRetries)
                {
                    if (logger.IsEnabled(LogLevel.Warning))
                    {
                        logger.LogWarning(
                            "Graph still answered {StatusCode} for {Path} after {Attempts} attempts; giving up on it for now.",
                            (int)response.StatusCode,
                            target.AbsolutePath,
                            attempt + 1);
                    }

                    // Both statuses are retried, but they do not mean the same thing to a reader:
                    // 429 is this account being asked to slow down, 503 is Graph having a bad day.
                    return Result.Failure<TResult>(response.StatusCode is HttpStatusCode.TooManyRequests
                        ? GraphErrors.Throttled
                        : GraphErrors.Unavailable);
                }

                delay = RetryDelay(response, attempt, settings);

                if (logger.IsEnabled(LogLevel.Information))
                {
                    logger.LogInformation(
                        "Graph asked for {DelaySeconds}s before retrying {Path} (attempt {Attempt}).",
                        delay.TotalSeconds,
                        target.AbsolutePath,
                        attempt + 1);
                }
            }

            await Task.Delay(delay, timeProvider, cancellationToken);
        }
    }

    /// <summary>
    /// Builds the delegated authorization header for one user, from a principal carrying just
    /// enough to find their entry in the token cache and refresh it silently.
    /// </summary>
    private async Task<Result<string>> AuthorizeAsync(IndexUser user, CancellationToken cancellationToken)
    {
        try
        {
            var header = await authorizationHeaderProvider.CreateAuthorizationHeaderForUserAsync(
                [.. GraphScopes.SignIn],
                claimsPrincipal: AccountPrincipal(user),
                cancellationToken: cancellationToken);

            return Result.Success(header);
        }
        catch (Exception exception) when (exception is MicrosoftIdentityWebChallengeUserException or MsalUiRequiredException)
        {
            // Before believing it: this person may have signed in through the Teams tab, whose
            // tokens MSAL files in a partition of its own rather than under the account id looked
            // up above (TeamsSsoDefaults.SessionKeyFor). Nothing on the queue row says which way
            // somebody signed in, and adding a column to say so would be storing a fact TodoWerk
            // can simply ask about — so the account is tried first, because it is the common case,
            // and this is what stops a Teams-only user from meeting "reconnect" on every request.
            if (await TeamsSessionHeaderAsync(user, cancellationToken) is { } fromTeamsSession)
            {
                return Result.Success(fromTeamsSession);
            }

            // Nothing a retry fixes: the cached refresh token is gone, consent was withdrawn, or
            // the entry was issued under a narrower scope than TodoWerk now asks for (ADR-0007).
            // Only the user standing in front of a browser can put any of those back.
            logger.LogInformation(
                exception,
                "No usable token for the signed-in user; asking them to reconnect.");

            return Result.Failure<string>(GraphErrors.ReconnectRequired);
        }
        catch (MsalException exception)
        {
            // Entra ID having a bad moment — an outage, a throttle, a transport fault inside
            // MSAL. Unlike the cases above this is not the user's to fix, so it must not surface
            // as "reconnect": the caller records a plain failure and tries again later.
            logger.LogWarning(exception, "Token acquisition failed transiently; the caller will retry later.");

            return Result.Failure<string>(GraphErrors.Unavailable);
        }
    }

    /// <summary>
    /// The same person's token from the partition a Teams sign-in leaves it in, or null when there
    /// is nothing there — which is the ordinary answer for somebody who has only ever used a
    /// browser, and is why this returns rather than throws.
    /// </summary>
    private async Task<string?> TeamsSessionHeaderAsync(IndexUser user, CancellationToken cancellationToken)
    {
        try
        {
            var token = await tokenAcquisition.GetAccessTokenForUserAsync(
                GraphScopes.SignIn,
                authenticationScheme: TeamsSsoDefaults.AuthenticationScheme,
                tokenAcquisitionOptions: new TokenAcquisitionOptions
                {
                    LongRunningWebApiSessionKey = TeamsSsoDefaults.SessionKeyFor(user.TenantId, user.UserId),
                });

            return $"Bearer {token}";
        }
        catch (Exception exception) when (exception is MsalException or MicrosoftIdentityWebChallengeUserException)
        {
            logger.LogDebug(
                exception,
                "No Teams-session token for this user either; the caller will ask them to reconnect.");

            return null;
        }
    }

    /// <summary>
    /// Whether an absolute request URI points at the one host the delegated token belongs to,
    /// which is the host the typed client is configured with.
    /// </summary>
    private bool IsGraphHost(Uri absolute) =>
        string.Equals(absolute.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
        && httpClient.BaseAddress is { Host: var allowed }
        && string.Equals(absolute.Host, allowed, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The principal MSAL resolves an account from. It has to carry <c>uid</c> and <c>utid</c> —
    /// the <em>home</em> account identifiers MSAL keys its cache on — and not <c>oid</c> and
    /// <c>tid</c>: a principal carrying only the directory claims makes token
    /// acquisition fail with "no account or login hint", which reads like a missing token rather
    /// than the wrong lookup key it is. <c>ClaimsPrincipalFactory.FromTenantIdAndObjectId</c>
    /// builds the directory-claims shape and is therefore not usable here.
    /// <para>
    /// For a work or school user in their own tenant the home identifiers and the directory ones are
    /// the same values, which is what makes deriving them like this correct. They diverge for a guest
    /// signing in to a tenant that is not their home, and TodoWerk does not index guests until that
    /// has been tried against a real tenant.
    /// </para>
    /// </summary>
    private static ClaimsPrincipal AccountPrincipal(IndexUser user) => new(new ClaimsIdentity(
        [
            new Claim(ClaimConstants.UniqueObjectIdentifier, user.UserId),
            new Claim(ClaimConstants.UniqueTenantIdentifier, user.TenantId),
            new Claim(ClaimConstants.Oid, user.UserId),
            new Claim(ClaimConstants.Tid, user.TenantId),
        ],
        "TodoWerkGraphCall"));

    /// <summary>429 is the documented one; 503 carries the same header when Graph sheds load.</summary>
    private static bool IsThrottling(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable;

    /// <summary>
    /// Graph's <c>Retry-After</c> if it sent one, capped; otherwise an exponential back-off, so a
    /// 503 without the header does not turn into a tight loop. The date form is measured against
    /// the injected clock — the only clock the rest of this class believes in.
    /// </summary>
    private TimeSpan RetryDelay(HttpResponseMessage response, int attempt, GraphOptions settings)
    {
        var requested = response.Headers.RetryAfter switch
        {
            { Delta: { } delta } => delta,
            { Date: { } date } => date - timeProvider.GetUtcNow(),
            _ => TimeSpan.FromSeconds(Math.Pow(2, attempt)),
        };

        return requested < TimeSpan.Zero
            ? TimeSpan.Zero
            : requested > settings.MaxRetryDelay ? settings.MaxRetryDelay : requested;
    }

    private async Task<Error> DescribeFailureAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation(
                    "Graph rejected the delegated token with {StatusCode}.",
                    (int)response.StatusCode);
            }

            return GraphErrors.ReconnectRequired;
        }

        if (response.StatusCode is HttpStatusCode.Gone && await IsResyncRequiredAsync(response, cancellationToken))
        {
            return GraphErrors.ResyncRequired;
        }

        // A 404 is the write path's ordinary outcome for a task somebody deleted between the plan
        // and the write, so it says that rather than borrowing the outage wording.
        if (response.StatusCode is HttpStatusCode.NotFound)
        {
            return GraphErrors.NotFound;
        }

        if (logger.IsEnabled(LogLevel.Warning))
        {
            logger.LogWarning(
                "Graph returned {StatusCode} for {RequestUri}.",
                (int)response.StatusCode,
                response.RequestMessage?.RequestUri);
        }

        return GraphErrors.Unavailable;
    }

    private static async Task<bool> IsResyncRequiredAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            var payload = await response.Content.ReadFromJsonAsync<GraphErrorResponse>(cancellationToken);

            return string.Equals(payload?.Error?.Code, ResyncRequiredCode, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is System.Text.Json.JsonException or NotSupportedException)
        {
            // A 410 whose body is not the error shape is not the resync signal; treat it as an
            // ordinary failure rather than dropping a delta token on a guess.
            return false;
        }
    }

    private sealed record GraphErrorResponse(GraphErrorDetail? Error);

    private sealed record GraphErrorDetail(string? Code, string? Message);
}
