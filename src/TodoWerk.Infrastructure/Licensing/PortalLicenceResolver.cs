using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TodoWerk.Application.Abstractions.Authentication;
using TodoWerk.Application.Abstractions.Licensing;
using TodoWerk.Application.Licensing;
using TodoWerk.Domain.Licensing;

namespace TodoWerk.Infrastructure.Licensing;

/// <summary>
/// Resolution, caching, fail-open and deny, for one person at a time. The single answer behind the
/// filter in front of every authenticated endpoint, the Licence endpoint and both background
/// claims — which is why it is one class and one cache rather than three call sites that each
/// decide for themselves.
/// <para>
/// The rules, in the order they apply:
/// </para>
/// <list type="number">
/// <item>
/// Inside the recheck interval, answer from cache. Never resolve per request — including at the
/// moment the interval runs out, when every request in flight for one person misses the cache
/// together: those share one call rather than each making their own.
/// </item>
/// <item>
/// Otherwise ask the portal. A confirmed negative — <c>valid: false</c> over HTTP 200 — denies
/// that person and nobody else, and is cached like any other answer so that somebody who has just
/// paid is not held out for longer than the recheck interval.
/// </item>
/// <item>
/// Unreachable is not a licence failure. Keep serving on the last answer for the configured
/// fail-open window, counted from when that answer arrived.
/// </item>
/// <item>
/// Past the window, or for somebody never resolved at all, answer "could not be verified" — never
/// "ended". The next successful resolution clears it with nobody pressing anything.
/// </item>
/// </list>
/// <para>
/// A singleton: its cache is the process's, and it holds nothing scoped to a request. That is also
/// what lets the seat reporter fire off a resolution after a sign-in has already been answered.
/// </para>
/// </summary>
internal sealed class PortalLicenceResolver(
    FirstPartyPortalClient portal,
    LicenceCache cache,
    TimeProvider timeProvider,
    IOptions<LicensingOptions> options,
    ILogger<PortalLicenceResolver> logger) : ILicenceResolver, ILicenceGate
{
    /// <summary>
    /// How long after a failed attempt the portal may be asked again. Not configuration: it is a
    /// stampede guard, not a policy. Without it a portal that has stopped answering would be
    /// called once per request per person, each call costing the request the full timeout, and the
    /// outage would take the Hosted Service down with it.
    /// <para>
    /// Counted from the moment the failed attempt ended rather than the moment it began.
    /// <see cref="ResolveAsync"/> says why, and what it cost while it was the other way round.
    /// </para>
    /// <para>
    /// Short enough that the "could not be verified" card clears within half a minute of the
    /// portal coming back, which is the thing a person waiting on it can see.
    /// </para>
    /// </summary>
    private static readonly TimeSpan RetryAfterFailure = TimeSpan.FromSeconds(30);

    /// <summary>
    /// This build, for the portal's call trace. Read once: the assembly cannot change under a
    /// running process.
    /// </summary>
    private static readonly string? PackageVersion = typeof(PortalLicenceResolver).Assembly
        .GetName().Version?.ToString();

    /// <summary>
    /// The one portal call in flight for each person who has one, keyed as the cache is.
    /// <para>
    /// The cache is written when an answer arrives, and nothing in it says "being asked right
    /// now". Opening the Workbench sends several requests within the same few milliseconds, each
    /// behind the Licence gate; the moment a cached answer ages out they all miss it together, and
    /// without this every one of them would ask the portal. This is what joins them.
    /// </para>
    /// <para>
    /// <see cref="Lazy{T}"/> rather than the task itself, because the dictionary may run a
    /// factory it then discards, and a discarded factory must not have sent a request.
    /// </para>
    /// </summary>
    private readonly ConcurrentDictionary<string, Lazy<Task<LicenceResolution>>> _inFlight =
        new(StringComparer.Ordinal);

    public async Task<LicenceResolution> ResolveAsync(IndexUser user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);

        var now = timeProvider.GetUtcNow();
        var entry = cache.Read(user);

        if (entry is not null && now < entry.RetryNotBefore)
        {
            return Decide(entry, now);
        }

        var key = LicenceCache.KeyFor(user);
        var flight = _inFlight.GetOrAdd(
            key,
            _ => new Lazy<Task<LicenceResolution>>(() => ResolveFromPortalAsync(user, key)));

        // Waited on with this caller's token, not run under it. The call belongs to everybody
        // waiting on it: the first request in a burst is the one that sent it, and if that browser
        // tab navigates away the others still need the answer — and the cache still needs it
        // written, or the next burst starts over.
        return await flight.Value.WaitAsync(cancellationToken);
    }

    /// <summary>
    /// The call itself, made once for everybody who found the same stale entry.
    /// </summary>
    private async Task<LicenceResolution> ResolveFromPortalAsync(IndexUser user, string key)
    {
        try
        {
            // Read again under the flight. Whoever is here read the cache before the flight they
            // are now running existed, and the previous flight for this person may have landed in
            // between — in which case its answer is fresh, and this is a second call for nothing.
            var now = timeProvider.GetUtcNow();
            var entry = cache.Read(user);

            if (entry is not null && now < entry.RetryNotBefore)
            {
                return Decide(entry, now);
            }

            return await AskAndRecordAsync(user, entry);
        }
        finally
        {
            // Before the waiters resume, and after the cache was written — so a request arriving
            // in between reads the answer rather than starting another flight.
            _inFlight.TryRemove(key, out _);
        }
    }

    private async Task<LicenceResolution> AskAndRecordAsync(IndexUser user, LicenceCache.LicenceCacheEntry? entry)
    {
        var answer = await portal.ResolveAsync(
            user,
            // Whether this process has ever had an answer for this person, not whether it has one
            // now: a person whose entry is a run of failures is not installing again.
            entry is null ? "install" : "periodic",
            PackageVersion,
            // Nobody's token: see ResolveAsync. The client's own request timeout bounds the call,
            // and a timeout is the one cancellation the client treats as an outage rather than
            // rethrowing, which is the right reading when no caller asked for it.
            CancellationToken.None);

        // The clock is read after the round trip, not before it. The round trip takes up to
        // `RequestTimeout`, and it takes the whole of it precisely when the portal has stopped
        // answering — which is the case every stamp below exists for. Counted from before the
        // call, a floor would already have spent that long by the time it was written, and at a
        // timeout of thirty seconds or more it would expire on arrival, so every request from
        // somebody on the fail-open window would pay a full timeout instead of one request in
        // thirty seconds paying it and the rest being answered from cache.
        //
        // The recheck interval and the fail-open window count from here for the same reason: both
        // are about how old an answer is, and an answer is as old as its arrival. One reading of
        // the clock for every stamp, so the record and the decision made on it cannot disagree
        // about when.
        var attemptEnded = timeProvider.GetUtcNow();

        if (answer.Resolution is null)
        {
            cache.RecordAttemptFailed(user, attemptEnded, RetryAfterFailure);

            var afterFailure = cache.Read(user);
            var decision = afterFailure is null
                ? LicenceResolution.Unverified
                : Decide(afterFailure, attemptEnded);

            if (decision.Outcome is LicenceOutcome.Unverified)
            {
                // The one thing in this file that is somebody's pager rather than a user's
                // problem: the fail-open window has run out, or there was never an answer to fall
                // back on, and a person who may well have paid is being turned away.
                logger.LogError(
                    "ManagementPortal is unreachable and there is no answer left inside the "
                    + "fail-open window for one person in tenant {TenantId}. They are being denied "
                    + "with \"could not be verified\".",
                    user.TenantId);
            }

            return decision;
        }

        cache.RecordAnswer(user, answer.Resolution, attemptEnded, answer.Recheck, options.Value.FailOpenWindow);

        return answer.Resolution;
    }

    public async Task<bool> IsLicensedAsync(IndexUser user, CancellationToken cancellationToken) =>
        (await ResolveAsync(user, cancellationToken)).IsLicensed;

    /// <summary>
    /// What a cached entry means right now. One function for the fresh case and the fail-open case
    /// alike, so "how old may this be" is answered in one place.
    /// <para>
    /// A cached confirmed negative is served through the window as itself rather than becoming
    /// "could not be verified": whoever it belongs to was told their access had ended, and turning
    /// that into a different card halfway through a portal outage would say the opposite of what
    /// TodoWerk actually knows.
    /// </para>
    /// </summary>
    private LicenceResolution Decide(LicenceCache.LicenceCacheEntry entry, DateTimeOffset now)
    {
        if (entry.Answer is null)
        {
            return LicenceResolution.Unverified;
        }

        return now - entry.AnsweredAt > options.Value.FailOpenWindow
            ? LicenceResolution.Unverified
            : entry.Answer;
    }
}
