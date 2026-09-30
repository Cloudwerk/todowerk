using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TodoWerk.Application.Abstractions.Authentication;
using TodoWerk.Application.Licensing;
using TodoWerk.Domain.Licensing;

namespace TodoWerk.Infrastructure.Licensing;

/// <summary>
/// The only thing in TodoWerk that talks to ManagementPortal. Two calls — resolve one person,
/// report one seat — over the First-Party Path, authenticated with the application key.
/// <para>
/// It answers in TodoWerk's own vocabulary and never throws for a portal that is having a bad day:
/// a timeout, a 5xx, a 401 from a key rotation, a 429 and a body it cannot read all come back as
/// the same thing, <see cref="PortalAnswer.Unreachable"/>, because there is exactly one thing the
/// caller can do about any of them.
/// </para>
/// <para>
/// One field crosses the edge in the portal's own words rather than TodoWerk's: the seat status on
/// a <see cref="SeatReport"/>. Nothing decides on it — it is quoted, into the one warning about a
/// seat nobody counted — and putting TodoWerk's words on a vocabulary somebody else owns would
/// only mean a line that no longer matches the portal's own trace of the same call.
/// </para>
/// </summary>
internal sealed class FirstPartyPortalClient(
    IHttpClientFactory httpClientFactory,
    IOptions<LicensingOptions> options,
    ILogger<FirstPartyPortalClient> logger)
{
    /// <summary>
    /// A named client asked for per call rather than a typed one held for the life of the process.
    /// The resolver above this is a singleton — it owns the cache, and the seat report runs after
    /// the request that triggered it has been answered — and a singleton that captures one
    /// <see cref="HttpClient"/> forever is the handler-lifetime bug the factory exists to prevent:
    /// its connections never rotate, so a portal that moves hosts is unreachable until a deploy.
    /// </summary>
    internal const string HttpClientName = "ManagementPortal.FirstParty";

    internal const string ApplicationKeyHeader = "X-Application-Key";

    internal const string ResolvePath = "/api/v1/first-party/resolve";

    internal const string UsagePath = "/api/v1/first-party/usage";

    /// <summary>
    /// The shortest an answer may be trusted for, whatever the portal asks. A recheck of nought
    /// seconds — a portal bug, or a field this build failed to read — would otherwise mean one
    /// resolution per request, which is exactly what the cache exists to stop.
    /// </summary>
    private static readonly TimeSpan MinimumRecheck = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The seat state a solution metered as Unlimited answers with. It arrives beside
    /// <c>recorded: false</c> — such a solution never meters at all, so nothing is stored and
    /// nothing is wrong — which makes it the one <c>recorded: false</c> that is not news.
    /// </summary>
    private const string UnlimitedSeatStatus = "unlimited";

    /// <summary>
    /// The seat state of a licence carrying more people than it was sold. The portal stores the
    /// seat anyway and calls this soft — warn, never block — so it arrives beside
    /// <c>recorded: true</c> and is the one landed report worth a word.
    /// </summary>
    private const string OverSeatStatus = "over";

    /// <summary>What one call to <c>/resolve</c> ended in, before any caching or fail-open.</summary>
    internal sealed record PortalAnswer(LicenceResolution? Resolution, TimeSpan Recheck)
    {
        /// <summary>The portal did not answer, or answered something this build cannot read.</summary>
        public static readonly PortalAnswer Unreachable = new(null, TimeSpan.Zero);
    }

    /// <summary>
    /// What one call to <c>/usage</c> ended in: whether the portal stored a seat, and the word it
    /// used for the licence's seat state.
    /// </summary>
    /// <param name="SeatsUsed">Seats counted against the licence after this report, where the portal said.</param>
    /// <param name="SeatLimit">The licence's seat limit, absent where it has none.</param>
    /// <param name="Answered">
    /// Whether the portal answered this call at all. It separates an outage from a refusal in the
    /// one place either is visible, which is the warning the reporter writes.
    /// </param>
    internal sealed record SeatReport(
        bool Recorded,
        string? SeatStatus,
        int? SeatsUsed = null,
        int? SeatLimit = null,
        bool Answered = true)
    {
        /// <summary>The portal did not answer, or answered something this build cannot read.</summary>
        public static readonly SeatReport Unanswered = new(false, null, Answered: false);

        /// <summary>
        /// Whether this report may be treated as counted, which is not the same question as
        /// whether a row was written: a solution metered as Unlimited stores nothing and is
        /// working exactly as intended, so warning about it on every sign-in of every person would
        /// bury the refusals that mean something.
        /// <para>
        /// Everything else that did not record is worth the line, rather than <c>invalid</c>
        /// alone. <c>invalid</c> is the licence being refused — five reasons, all of them the
        /// licence — but the sixth way a seat goes uncounted is the per-licence seat-token ceiling,
        /// and the portal answers that one with <c>recorded: false</c> and a seat status of
        /// <c>within</c> or <c>over</c>. Complaining only about <c>invalid</c> would miss exactly
        /// the case a tenant large enough to reach a ceiling arrives as.
        /// </para>
        /// <para>
        /// A report that landed can still be worth a word: see <see cref="OverSeatLimit"/>.
        /// </para>
        /// </summary>
        public bool Landed => Recorded
            || string.Equals(SeatStatus, UnlimitedSeatStatus, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Whether the portal counted this seat above the licence's seat limit. Soft by the
        /// portal's own design — it stores the row and answers <c>recorded: true</c> with
        /// <c>over</c> — and the contract asks callers to warn and never to block.
        /// <para>
        /// Only a seat that was recorded: over the per-licence seat-token ceiling the same word
        /// arrives beside <c>recorded: false</c>, and that is a seat nobody counted, which
        /// <see cref="Landed"/> already has the line for. One report is worth one line.
        /// </para>
        /// </summary>
        public bool OverSeatLimit => Recorded
            && string.Equals(SeatStatus, OverSeatStatus, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Asks what one person holds.
    /// </summary>
    /// <param name="checkKind">
    /// <c>install</c> for a person this process has never resolved, <c>periodic</c> afterwards.
    /// It reaches the portal's call trace and changes no answer.
    /// </param>
    public async Task<PortalAnswer> ResolveAsync(
        IndexUser user,
        string checkKind,
        string? packageVersion,
        CancellationToken cancellationToken)
    {
        var settings = options.Value;

        try
        {
            using var http = Authenticated();
            using var response = await http.PostAsJsonAsync(
                ResolvePath,
                new FirstPartyResolveRequest(
                    user.TenantId,
                    user.UserId,
                    settings.SolutionSlug,
                    packageVersion,
                    checkKind),
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                // Including 401. A wrong or rotated key is a configuration fault that retrying
                // unchanged will never fix — but denying every customer over it would be worse
                // than serving them while somebody is paged, and the fail-open window is sized
                // for exactly this. Logged loudly so that somebody is.
                logger.LogError(
                    "ManagementPortal answered {StatusCode} to a licence resolution. Nobody is "
                    + "denied by this on its own; the fail-open window is now running.",
                    (int)response.StatusCode);

                return PortalAnswer.Unreachable;
            }

            var body = await response.Content.ReadFromJsonAsync<FirstPartyResolveResponse>(cancellationToken);

            if (body is null)
            {
                logger.LogError("ManagementPortal answered a licence resolution with an empty body.");

                return PortalAnswer.Unreachable;
            }

            return new PortalAnswer(Translate(body), RecheckFor(body, settings));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The caller went away, not the portal. Rethrown so a cancelled request is not
            // recorded as an outage that starts somebody's fail-open window running.
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException
            or TaskCanceledException
            or JsonException
            or NotSupportedException
            or FormatException)
        {
            // Four unrelated shapes, one meaning: no answer came back. A timeout arrives as
            // TaskCanceledException with nobody having cancelled anything; a proxy or a
            // maintenance page answering 200 with HTML arrives as NotSupportedException from the
            // JSON reader rather than as a parse error; and an application key that picked up a
            // trailing newline (from a secret file, say) arrives as FormatException from the header,
            // before a request goes out at all. Letting any of them escape would turn one portal
            // hiccup into a 500 on every authenticated request, with no fail-open and no retry
            // floor, because the failure would never be recorded.
            logger.LogError(
                exception,
                "Could not reach ManagementPortal to resolve a licence. Serving on the last "
                + "positive answer while the fail-open window lasts.");

            return PortalAnswer.Unreachable;
        }
    }

    /// <summary>
    /// Files one seat report. Answers what became of it, for the log line and nothing else: usage
    /// is a figure and a reconnect signal, and no decision anywhere depends on it.
    /// <para>
    /// The answer is in the body rather than in the status code. A seat goes uncounted six ways —
    /// a licence out of term by the moment the report lands, a suspended one, one that is not this
    /// tenant's, one that is not this solution's, one the portal has never heard of, and the
    /// per-licence seat-token ceiling reached — and every one of them arrives as
    /// <c>recorded: false</c> over HTTP 200, so a client reading <c>IsSuccessStatusCode</c> would
    /// call all six a seat that landed.
    /// </para>
    /// </summary>
    public async Task<SeatReport> ReportUsageAsync(
        string tenantId,
        string licenceId,
        string usageToken,
        CancellationToken cancellationToken)
    {
        try
        {
            using var http = Authenticated();
            using var response = await http.PostAsJsonAsync(
                UsagePath,
                new FirstPartyUsageRequest(
                    tenantId,
                    options.Value.SolutionSlug,
                    licenceId,
                    usageToken),
                cancellationToken);

            // A non-2xx is a portal that never decided, which the reporter's line calls what it
            // is. Not logged again here: unlike a resolution, one seat report is one log line, and
            // a portal answering 5xx to this is answering 5xx to /resolve, which does say so.
            if (!response.IsSuccessStatusCode)
            {
                return SeatReport.Unanswered;
            }

            var body = await response.Content.ReadFromJsonAsync<FirstPartyUsageResponse>(cancellationToken);

            return body is null
                ? SeatReport.Unanswered
                : new SeatReport(body.Recorded, body.SeatStatus, body.SeatsUsed, body.SeatLimit);
        }
        catch (Exception exception) when (exception is HttpRequestException
            or TaskCanceledException
            or OperationCanceledException
            or JsonException
            or NotSupportedException
            or FormatException)
        {
            // JsonException joins the rest now that there is a body to read: an answer that is not
            // the contract — a maintenance page, a truncated body — is not a seat that landed, and
            // it is not worth an exception on a path nothing is waiting for either.
            logger.LogWarning(exception, "A seat usage report to ManagementPortal did not go through.");

            return SeatReport.Unanswered;
        }
    }

    /// <summary>
    /// A client for one call, carrying the one credential.
    /// <para>
    /// The key goes on here rather than into the named client's registration, and the difference
    /// is what can be tested: on the registration it is a fact about the container, and a suite
    /// standing in for the portal would assert it against its own fixture rather than against this
    /// code. On the request it is a fact about the call — including the half that matters most,
    /// that the key travels as a header and never in a body.
    /// </para>
    /// </summary>
    private HttpClient Authenticated()
    {
        var http = httpClientFactory.CreateClient(HttpClientName);

        http.DefaultRequestHeaders.Remove(ApplicationKeyHeader);
        http.DefaultRequestHeaders.Add(ApplicationKeyHeader, options.Value.ApplicationKey);

        return http;
    }

    /// <summary>
    /// The portal's answer in TodoWerk's words. A confirmed negative is a <c>valid: false</c> over
    /// HTTP 200 and is the only thing that ends anybody's access.
    /// </summary>
    private LicenceResolution Translate(FirstPartyResolveResponse body)
    {
        // Read on both branches, because the portal sends it on both. A refusal is the answer that
        // most wants one: it is the moment a person is told their Trial ended.
        var purchaseUrl = PurchaseUrlFrom(body.PurchaseUrl);

        if (!body.Valid)
        {
            return LicenceResolution.Ended(body.Message, purchaseUrl);
        }

        return LicenceResolution.Allowed(
            new Licence(
                KindFrom(body.Kind),
                body.ExpiresUtc,
                body.LicenseId ?? string.Empty,
                body.UsageSecret ?? string.Empty),
            purchaseUrl);
    }

    /// <summary>
    /// The portal's lowercase <c>kind</c>, and what to do with one this build has never heard of.
    /// <para>
    /// An unknown kind reads as <see cref="LicenceKind.Personal"/>, which is the conservative
    /// answer rather than an arbitrary one: it shows no banner and offers no Tenant Consent. The
    /// two mistakes available are not symmetrical — withholding the invitation costs an
    /// administrator a route they still have in the Entra portal, while offering it wrongly is
    /// precisely what ADR-0012 decided a Personal Licence must never do.
    /// </para>
    /// </summary>
    private LicenceKind KindFrom(string? kind)
    {
        // Compared rather than lowercased: the portal sends these lowercase, and folding the case
        // of a value from the wire is one more way to be wrong about a vocabulary somebody else owns.
        switch (kind)
        {
            case not null when kind.Equals("tenant", StringComparison.OrdinalIgnoreCase):
                return LicenceKind.Tenant;
            case not null when kind.Equals("personal", StringComparison.OrdinalIgnoreCase):
                return LicenceKind.Personal;
            case not null when kind.Equals("trial", StringComparison.OrdinalIgnoreCase):
                return LicenceKind.Trial;
            default:
                logger.LogWarning(
                    "ManagementPortal answered with a licence kind this build does not know "
                    + "({Kind}). Treating it as a Personal Licence: no banner and no Tenant "
                    + "Consent invitation.",
                    kind ?? "absent");

                return LicenceKind.Personal;
        }
    }

    /// <summary>
    /// The portal's purchase URL, when it sent a usable one. Absent is the ordinary state — an
    /// empty catalog sells nothing and a deactivated organisation is not invited to pay — and it
    /// is not a fault: no link is shown and nothing is logged about it.
    /// <para>
    /// Absolute and http(s) or nothing. The portal composes and validates this itself, so this is
    /// a second gate rather than the only one — kept
    /// because what a bad value becomes on screen is a link somewhere nobody chose, and because a
    /// client that trusts an address it was handed is how the first one gets through.
    /// </para>
    /// </summary>
    private Uri? PurchaseUrlFrom(JsonElement? delivered)
    {
        // Absent, or an explicit null. The ordinary state, and silent: the portal sends no address
        // where it sells nothing for TodoWerk, which is not a fault and not news.
        if (delivered is null or { ValueKind: JsonValueKind.Undefined or JsonValueKind.Null })
        {
            return null;
        }

        var value = delivered.Value.ValueKind is JsonValueKind.String
            ? delivered.Value.GetString()
            : null;

        if (Uri.TryCreate(value, UriKind.Absolute, out var url)
            && (url.Scheme == Uri.UriSchemeHttps || url.Scheme == Uri.UriSchemeHttp))
        {
            return url;
        }

        // Everything else in one line: a relative path, a javascript: scheme, an empty string, and
        // a value that is not a string at all. All of them mean the same thing here — there is no
        // address to send anybody to — and none of them is worth failing the answer over.
        logger.LogWarning(
            "ManagementPortal answered with a purchase URL that is not an absolute http(s) URL, "
            + "so no purchase link is shown.");

        return null;
    }

    /// <summary>
    /// How long the answer is trusted: the portal's own <c>recheckSeconds</c>, bounded below so a
    /// missing or nonsensical value cannot mean "resolve on every request", and above by this
    /// deployment's ceiling so an operator cutting an organisation off always lands in minutes.
    /// </summary>
    private static TimeSpan RecheckFor(FirstPartyResolveResponse body, LicensingOptions settings)
    {
        var ceiling = settings.MaximumCacheLifetime;

        if (body.RecheckSeconds is not { } seconds || seconds <= 0)
        {
            return ceiling;
        }

        var asked = TimeSpan.FromSeconds(seconds);

        return asked < MinimumRecheck ? MinimumRecheck : asked > ceiling ? ceiling : asked;
    }
}
