using TodoWerk.Domain.Indexing;

namespace TodoWerk.Infrastructure.Indexing;

/// <summary>
/// How the index keeps itself current. Configuration rather than constants: a tenant with large
/// mailboxes and a tenant with three lists want different numbers here, and ADR-0003's "index
/// freshness is a first-class concern" is only meaningful if freshness is tunable.
/// </summary>
public sealed class IndexingOptions
{
    public const string SectionName = "Indexing";

    /// <summary>
    /// How often the worker looks for queued scans. Short enough that a manual re-scan feels
    /// immediate, long enough that an idle deployment is not polling SQL Server in a tight loop.
    /// </summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// How stale a list may get before the worker syncs it without being asked. This is the real
    /// bound on how wrong the inventory can be.
    /// </summary>
    public TimeSpan SyncInterval { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// How long somebody may go without an interactive sign-in before the worker stops keeping
    /// their index fresh on its own. Somebody inside the window is synced every
    /// <see cref="SyncInterval"/>; somebody outside it is Idle, and their index waits for them —
    /// the sign-in that brings them back is stamped before their first page renders, so the next
    /// poll tick queues the overdue sync. Bounds the cost of an index nobody is reading: without
    /// it, every person who ever signed in is synced, and under the Hosted Service has their
    /// Licence re-checked, every half hour for the life of the deployment.
    /// <para>
    /// Fourteen days rather than seven, which two other settings already use — a number shared
    /// with nothing else in configuration is what lets a test tell a crossed wire from a
    /// coincidence. Must exceed <see cref="SyncInterval"/>; a smaller value would switch scheduled
    /// syncs off for everybody, silently.
    /// </para>
    /// </summary>
    public TimeSpan IdleAfter { get; set; } = TimeSpan.FromDays(14);

    /// <summary>
    /// How long a scan may stay <c>Running</c> before the worker assumes the process that claimed
    /// it is gone and picks it up again. Longer than any real scan, because reclaiming one that is
    /// still running would index the same tasks twice.
    /// </summary>
    public TimeSpan ScanTimeout { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// How long every task carrying a Hashtag has to go unmodified before the inventory calls it
    /// stale. Configuration rather than a number in the code, because "old" means something
    /// different to a team that closes tasks weekly and one that plans by quarter. Six months is
    /// a default, not a claim.
    /// </summary>
    public TimeSpan StaleAfter { get; set; } = TimeSpan.FromDays(180);

    /// <summary>
    /// The shortest Hashtag the near-duplicate flag will consider. Below it, one character is most
    /// of the name and <c>#q1</c> looks like a typo of <c>#q2</c>.
    /// </summary>
    public int NearDuplicateMinimumLength { get; set; } = NearDuplicateDetector.DefaultMinimumLength;

    /// <summary>
    /// The longest Hashtag the near-duplicate flag will consider. The detector's work grows with
    /// the square of the name length, and the names are written by whoever writes task titles —
    /// without a ceiling, one account full of pathological tags decides how much CPU every
    /// inventory read costs. A real Hashtag is a word or three; sixty-four characters is past
    /// every legitimate one.
    /// </summary>
    public int NearDuplicateMaximumLength { get; set; } = 64;

    /// <summary>
    /// How long finished scan rows are kept before the worker deletes them. They are a work
    /// queue, not an audit log — but the scheduler reads the recent ones to know a user was
    /// tried lately, so the window must comfortably exceed <see cref="SyncInterval"/>.
    /// </summary>
    public TimeSpan FinishedScanRetention { get; set; } = TimeSpan.FromDays(7);

    /// <summary>
    /// How long the near-duplicate result for one user is reused before being recomputed. The
    /// pairing reads every distinct key the user has, which is too much work to redo for every
    /// page of a grid somebody is scrolling — and a flag that is up to a minute behind the index
    /// is still honest, because the index itself is allowed to be half an hour behind the tasks.
    /// </summary>
    public TimeSpan NearDuplicateCacheLifetime { get; set; } = TimeSpan.FromMinutes(1);
}
