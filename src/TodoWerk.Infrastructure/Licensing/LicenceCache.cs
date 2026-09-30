using System.Collections.Concurrent;
using TodoWerk.Application.Abstractions.Authentication;
using TodoWerk.Domain.Licensing;

namespace TodoWerk.Infrastructure.Licensing;

/// <summary>
/// The last answer for each person, and when it may next be asked about again. In the process and
/// not in the database, deliberately: it is not a record of anything — a restart costs one extra
/// resolution per person, and a Licence in a table would be the second entitlement brain ADR-0001
/// exists to prevent.
/// <para>
/// Keyed per person rather than per tenant, because the whole of ADR-0012 is that a colleague's
/// answer says nothing about yours.
/// </para>
/// </summary>
internal sealed class LicenceCache
{
    private readonly ConcurrentDictionary<string, LicenceCacheEntry> _entries = new(StringComparer.Ordinal);

    /// <summary>
    /// How many entries may accumulate before a write sweeps the ones nothing can use any more.
    /// <para>
    /// Without it the dictionary holds one entry per person who has ever signed in to this process,
    /// for the life of the process — which is a slow leak in a busy tenant and, less forgivably,
    /// leaves an Entra object id in memory long after the answer beside it stopped meaning
    /// anything. Well above any plausible number of people signed in at once, so a healthy
    /// deployment never reaches it.
    /// </para>
    /// </summary>
    private const int SweepAbove = 10_000;

    /// <summary>
    /// What is known about one person.
    /// </summary>
    /// <param name="Answer">
    /// The last answer the portal actually gave, positive or a confirmed negative. Null for
    /// somebody every attempt for has failed — which is what a person arriving for the first time
    /// during an outage looks like, and why they are denied rather than served.
    /// </param>
    /// <param name="AnsweredAt">When that answer arrived. The instant the fail-open window counts from.</param>
    /// <param name="RetryNotBefore">
    /// When the portal may be asked again: the recheck interval after a success, a short floor
    /// after a failure. One field for both, so there is one rule about when a call goes out and
    /// not two that can disagree.
    /// </param>
    internal sealed record LicenceCacheEntry(
        LicenceResolution? Answer,
        DateTimeOffset AnsweredAt,
        DateTimeOffset RetryNotBefore);

    public static string KeyFor(IndexUser user)
    {
        ArgumentNullException.ThrowIfNull(user);

        return $"{user.TenantId}:{user.UserId}";
    }

    public LicenceCacheEntry? Read(IndexUser user) =>
        _entries.TryGetValue(KeyFor(user), out var entry) ? entry : null;

    /// <summary>The portal answered. Trusted until the recheck interval runs out.</summary>
    public void RecordAnswer(
        IndexUser user,
        LicenceResolution answer,
        DateTimeOffset now,
        TimeSpan recheck,
        TimeSpan failOpenWindow)
    {
        _entries[KeyFor(user)] = new LicenceCacheEntry(answer, now, now + recheck);

        SweepIfCrowded(now, failOpenWindow);
    }

    /// <summary>
    /// The portal did not answer. Whatever it last said is kept — that is what the fail-open
    /// window serves on — and only the moment of the next attempt moves.
    /// </summary>
    public void RecordAttemptFailed(IndexUser user, DateTimeOffset now, TimeSpan retryAfter) =>
        _entries.AddOrUpdate(
            KeyFor(user),
            _ => new LicenceCacheEntry(null, now, now + retryAfter),
            (_, existing) => existing with { RetryNotBefore = now + retryAfter });

    /// <summary>
    /// Drops every entry too old to be served on and not currently holding anybody's retry floor.
    /// <para>
    /// Opportunistic rather than a timer: an entry past the fail-open window is one the resolver
    /// would answer "could not be verified" from anyway, so forgetting it costs a portal call the
    /// next request was going to make in any case. Nothing here changes an answer — it only stops
    /// the process holding identifiers whose answers have stopped meaning anything.
    /// </para>
    /// </summary>
    private void SweepIfCrowded(DateTimeOffset now, TimeSpan failOpenWindow)
    {
        if (_entries.Count <= SweepAbove)
        {
            return;
        }

        foreach (var (key, entry) in _entries)
        {
            if (now - entry.AnsweredAt > failOpenWindow && now >= entry.RetryNotBefore)
            {
                _entries.TryRemove(key, out _);
            }
        }
    }

    /// <summary>
    /// Forgets one person, so that erasure leaves nothing of them in this process either. Answers
    /// whether there was anything to forget, for the log line the sweep writes.
    /// </summary>
    public bool Forget(IndexUser user) => _entries.TryRemove(KeyFor(user), out _);
}
