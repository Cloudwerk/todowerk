using TodoWerk.Domain.Failures;
using TodoWerk.Domain.Indexing;

namespace TodoWerk.Application.Indexing;

/// <summary>
/// How current the index is, and what it is doing. ADR-0003 makes this a first-class part of the
/// screen rather than a diagnostic: an index TodoWerk maintains itself is sometimes stale, and
/// hiding that would let someone act on numbers they have no reason to trust.
/// </summary>
/// <param name="Activity">What the index is doing right now, for the whole user.</param>
/// <param name="CurrentAsOf">
/// The oldest successful sync across the user's lists — the index as a whole is only as fresh as
/// its stalest list, so this is deliberately the minimum and not the maximum. Null until the
/// first list finishes.
/// </param>
/// <param name="HasCompletedFirstScan">
/// Every list has been read end to end at least once. Until then the inventory is a partial view
/// and the Workbench says so, because a first scan takes minutes.
/// </param>
/// <param name="LastScanFailure">
/// Why the last scan gave up, when it did and nothing has succeeded since. A scan can fail before
/// it reaches a single list — no token, no list of lists — and that failure has nowhere else to
/// surface: there are no per-list rows to carry it, so without this the screen would report an
/// account that has never been indexed and never say why.
/// </param>
/// <param name="LastScanFailureCode">
/// The same failure as a value the client switches on. What decides whether a sign-in button is
/// offered beside the sentence, so that no client has to look for a phrase in it.
/// </param>
public sealed record IndexStatusDto(
    IndexActivity Activity,
    DateTimeOffset? CurrentAsOf,
    bool HasCompletedFirstScan,
    int ListCount,
    int ListsIndexed,
    int TasksIndexed,
    IReadOnlyList<TaskListStatusDto> Lists,
    string? LastScanFailure = null,
    FailureCode LastScanFailureCode = FailureCode.None);

/// <summary>Per-list progress, which is what makes a long first scan legible rather than a spinner.</summary>
public sealed record TaskListStatusDto(
    string TaskListId,
    string DisplayName,
    ListScanState State,
    int TasksIndexed,
    DateTimeOffset? LastSuccessfulSyncAt,
    DateTimeOffset? LastCompletedScanAt,
    string? FailureReason,
    FailureCode FailureCode);

/// <summary>What the index is doing for one user, as one word the UI can switch on.</summary>
public enum IndexActivity
{
    /// <summary>Nothing queued, nothing running. The numbers on screen are as good as they get.</summary>
    Idle = 0,

    /// <summary>A scan is queued and the worker has not picked it up yet.</summary>
    Queued = 1,

    /// <summary>A scan is running now, and the per-list counts are moving.</summary>
    Scanning = 2,
}
