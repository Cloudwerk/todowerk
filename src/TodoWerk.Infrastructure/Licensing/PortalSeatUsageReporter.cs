using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using TodoWerk.Application.Abstractions.Authentication;
using TodoWerk.Application.Abstractions.Licensing;
using TodoWerk.Application.Licensing;
using TodoWerk.Domain.Licensing;

namespace TodoWerk.Infrastructure.Licensing;

/// <summary>
/// Reports one seat on an interactive sign-in, so the portal's figures mean something and its
/// last-seen refresh doubles as "this person is back". A figure and a signal, never a gate: a
/// Tenant Licence is unlimited and a Personal Licence is one person (ADR-0012).
/// <para>
/// What is sent is <c>hex(HMAC-SHA256(key = usageSecret, message = objectId))</c> and the licence
/// id — never the object id itself and never a licence key, neither of which this application
/// holds or forwards. Keying on the <em>per-licence</em> secret is the load-bearing part: it is
/// what stops one person's token being the same under two Licences.
/// </para>
/// <para>
/// Only a human ever reaches here. The two call sites are the OpenID Connect callback and the
/// Teams tab's token exchange; a scan on a timer has no route to either, which is the same way the
/// Tenant Member record enforces the same rule.
/// </para>
/// </summary>
internal sealed class PortalSeatUsageReporter(
    ILicenceResolver resolver,
    FirstPartyPortalClient portal,
    TimeProvider timeProvider,
    ILogger<PortalSeatUsageReporter> logger) : ISeatUsageReporter
{
    /// <summary>
    /// Who has already been reported today. In the process and not in the database on purpose: a
    /// missed report after a restart is one more report, not data lost, and the alternative is a
    /// table of who signed in and when — which is ADR-0009's whole subject.
    /// </summary>
    private readonly ConcurrentDictionary<string, DateOnly> _reportedOn = new(StringComparer.Ordinal);

    /// <summary>
    /// How long the detached report may take before it gives up. Nothing waits on it, so the only
    /// thing this bounds is how long a socket is held for a figure nobody reads synchronously.
    /// </summary>
    private static readonly TimeSpan ReportTimeout = TimeSpan.FromSeconds(15);

    /// <summary>
    /// How many claims may accumulate before a new one sweeps out the days that have passed. Well
    /// above any plausible number of people signed in on one day, so a healthy deployment never
    /// reaches it.
    /// </summary>
    private const int SweepAbove = 10_000;

    public Task ReportSignInAsync(IndexUser user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);

        if (!ClaimToday(user))
        {
            return Task.CompletedTask;
        }

        // Detached, with a token of its own. The sign-in has already succeeded by the time this
        // runs and must not wait on it or fail with it — the resolution alone can be a round trip
        // to the portal, and nobody should stand at a sign-in screen for it.
        _ = Task.Run(() => ReportNowAsync(user, CancellationToken.None), CancellationToken.None);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Drops one person's claim, so that erasure leaves nothing of them here either. The cache
    /// beside this one is cleared by the same purge; this dictionary held an Entra object id just
    /// as plainly, and forgetting one but not the other would make the purge's promise half true.
    /// </summary>
    internal bool Forget(IndexUser user)
    {
        ArgumentNullException.ThrowIfNull(user);

        return _reportedOn.TryRemove(LicenceCache.KeyFor(user), out _);
    }

    /// <summary>
    /// Whether this sign-in is the one that reports today, and takes today for this person if so.
    /// <para>
    /// Claimed before the work starts rather than after it succeeds. A failed report is
    /// deliberately not retried until tomorrow: the figure is a count of people, and a portal
    /// having a bad minute is not worth a retry loop against a number nothing enforces.
    /// </para>
    /// <para>
    /// Its own method so that the once-a-day rule can be checked on its own: the report itself is
    /// detached by design and nothing can await it.
    /// </para>
    /// </summary>
    internal bool ClaimToday(IndexUser user)
    {
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var key = LicenceCache.KeyFor(user);

        DropYesterdays(today);

        // Compare-and-swap rather than read-then-write: two sign-ins of the same person can land
        // on two threads, and "at most once per person per day" has to survive that in a process
        // serving a whole tenant.
        while (true)
        {
            if (!_reportedOn.TryGetValue(key, out var last))
            {
                if (_reportedOn.TryAdd(key, today))
                {
                    return true;
                }

                continue;
            }

            if (last == today)
            {
                return false;
            }

            if (_reportedOn.TryUpdate(key, today, last))
            {
                return true;
            }
        }
    }

    /// <summary>
    /// The report itself, awaited only by the tests. Never throws: every failure here is a log
    /// line, because there is nothing upstream that could act on one.
    /// </summary>
    internal async Task ReportNowAsync(IndexUser user, CancellationToken cancellationToken)
    {
        using var deadline = new CancellationTokenSource(ReportTimeout, timeProvider);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);

        try
        {
            var resolution = await resolver.ResolveAsync(user, timeout.Token);

            if (resolution is not { Outcome: LicenceOutcome.Licensed, Licence: { } licence })
            {
                // Nothing to report against. A denied person has no seat, and a Self-Host never
                // gets here at all — it is registered with a reporter that does nothing.
                return;
            }

            if (licence.LicenceId.Length == 0 || licence.UsageSecret.Length == 0)
            {
                logger.LogWarning(
                    "A valid licence arrived without a licence id or usage secret, so this "
                    + "person's seat cannot be reported. The portal's figures will be short by one.");

                return;
            }

            var report = await portal.ReportUsageAsync(
                user.TenantId,
                licence.LicenceId,
                UsageToken(licence.UsageSecret, user.UserId),
                timeout.Token);

            if (!report.Landed)
            {
                // The portal's own word for why, because the six ways a seat goes uncounted are
                // not one thing: a licence that lapsed between the resolution a moment ago and
                // this call asks for a different person than a seat-token ceiling a large tenant
                // has reached, and a portal that never answered asks for a third.
                logger.LogWarning(
                    "ManagementPortal did not record a seat for one person in tenant {TenantId} "
                    + "({SeatStatus}). Their sign-in was unaffected.",
                    user.TenantId,
                    report.SeatStatus ?? (report.Answered ? "no seat status" : "no answer"));
            }
            else if (report.OverSeatLimit)
            {
                // The soft one, and deliberately not phrased like the line above it: this seat was
                // counted, nobody is blocked, and what is worth somebody's attention is a licence
                // carrying more people than it was sold.
                //
                // Written once per person per day, which is what the claim above already bounds it
                // to, rather than once per licence: a licence-keyed note of when it last complained
                // is a residue erasure cannot reach — under a Personal Licence that key is one
                // person — and this class deletes what it holds about somebody when they ask.
                logger.LogWarning(
                    "ManagementPortal counted a seat in tenant {TenantId} above the licence's seat "
                    + "limit: {SeatsUsed} of {SeatLimit}. Nothing is blocked by it — the portal "
                    + "calls this soft — and the licence is carrying more people than it was sold.",
                    user.TenantId,
                    report.SeatsUsed,
                    report.SeatLimit);
            }
        }
        catch (OperationCanceledException)
        {
            // The deadline, or a host shutting down. Neither is worth a stack trace: nothing is
            // waiting on this and there is nothing to retry until tomorrow.
            logger.LogWarning("Reporting a seat to ManagementPortal did not finish in time. The sign-in was unaffected.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Deliberately everything else. This runs on a task nobody awaits, so an escape would
            // be an unobserved exception and, in the worst case, a torn-down process over a figure.
            logger.LogWarning(exception, "Reporting a seat to ManagementPortal failed. The sign-in was unaffected.");
        }
    }

    /// <summary>
    /// Forgets claims from any day but this one, once there are more of them than a deployment
    /// plausibly has people signed in at once.
    /// <para>
    /// A claim from yesterday decides nothing — today is all that is compared — so it is an object
    /// id being held for no reason, and one per person who has ever signed in to this process. The
    /// sweep is opportunistic rather than a timer for the same reason the licence cache's is: this
    /// is bookkeeping, not a schedule anybody depends on.
    /// </para>
    /// </summary>
    private void DropYesterdays(DateOnly today)
    {
        if (_reportedOn.Count <= SweepAbove)
        {
            return;
        }

        foreach (var (key, day) in _reportedOn)
        {
            if (day != today)
            {
                _reportedOn.TryRemove(new KeyValuePair<string, DateOnly>(key, day));
            }
        }
    }

    /// <summary>
    /// <c>hex(HMAC-SHA256(key = utf8(usageSecret), message = utf8(objectId)))</c>, sixty-four
    /// lowercase hex characters. Computed here and nowhere else: the raw object id never leaves
    /// this server on a usage call.
    /// </summary>
    internal static string UsageToken(string usageSecret, string objectId) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(usageSecret),
            Encoding.UTF8.GetBytes(objectId)));
}
