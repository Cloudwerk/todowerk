using Microsoft.EntityFrameworkCore;
using TodoWerk.Application.Indexing;
using TodoWerk.Domain.Indexing;
using TodoWerk.Infrastructure.Persistence;
using TodoWerk.SharedKernel;
using TodoWerk.Application.Abstractions.Authentication;
using TodoWerk.Domain.Failures;

namespace TodoWerk.Infrastructure.Indexing.Persistence;

/// <summary>
/// Reads the freshness the Workbench shows in its chrome: what the index is doing, how old it is,
/// and how far each list has got.
/// </summary>
internal sealed class IndexStatusReader(TodoWerkDbContext context) : IIndexStatusReader
{
    public async Task<Result<IndexStatusDto>> GetStatusAsync(IndexUser user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);

        // Counted from the indexed tasks themselves rather than read off the list state's
        // progress field: a delta pass records only how many tasks it touched, and showing that
        // as the size of the index would turn "500 tasks" into "2" half an hour after the first
        // scan. During a full scan the two agree anyway — pages are committed as they arrive, so
        // this count is the live progress number.
        var taskCounts = await context.Set<IndexedTask>()
            .AsNoTracking()
            .Where(task => task.TenantId == user.TenantId && task.UserId == user.UserId)
            .GroupBy(task => task.TaskListId)
            .Select(group => new { TaskListId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(group => group.TaskListId, group => group.Count, StringComparer.Ordinal, cancellationToken);

        var lists = await context.Set<TaskListIndexState>()
            .AsNoTracking()
            .Where(state => state.TenantId == user.TenantId && state.UserId == user.UserId)
            .OrderBy(state => state.DisplayName)
            .Select(state => new
            {
                state.TaskListId,
                state.DisplayName,
                state.State,
                state.LastSuccessfulSyncAt,
                state.LastCompletedScanAt,
                state.FailureReason,
                state.FailureCode,
            })
            .ToListAsync(cancellationToken);

        var scanState = await context.Set<IndexScan>()
            .AsNoTracking()
            .Where(scan => scan.TenantId == user.TenantId
                && scan.UserId == user.UserId
                && (scan.State == IndexScanState.Pending || scan.State == IndexScanState.Running))
            .Select(scan => (IndexScanState?)scan.State)
            .ToListAsync(cancellationToken);

        var activity = scanState switch
        {
            _ when scanState.Contains(IndexScanState.Running) => IndexActivity.Scanning,
            _ when scanState.Count > 0 => IndexActivity.Queued,
            _ => IndexActivity.Idle,
        };

        // The most recent scan that finished, whichever way it finished. If that was a failure it
        // is still the truth about this index; a success after it means the failure is history.
        var lastFinished = await context.Set<IndexScan>()
            .AsNoTracking()
            .Where(scan => scan.TenantId == user.TenantId
                && scan.UserId == user.UserId
                && (scan.State == IndexScanState.Completed || scan.State == IndexScanState.Failed))
            .OrderByDescending(scan => scan.RequestedAt)
            .Select(scan => new { scan.State, scan.FailureReason, scan.FailureCode })
            .FirstOrDefaultAsync(cancellationToken);

        IReadOnlyList<TaskListStatusDto> listStatuses =
        [
            .. lists.Select(state => new TaskListStatusDto(
                state.TaskListId,
                state.DisplayName,
                Displayed(state.State, activity, state.LastCompletedScanAt),
                taskCounts.TryGetValue(state.TaskListId, out var count) ? count : 0,
                state.LastSuccessfulSyncAt,
                state.LastCompletedScanAt,
                state.FailureReason,
                state.FailureCode)),
        ];

        // The oldest of the per-list timestamps, not the newest: one list synced a second ago says
        // nothing about the nine that have not been touched for an hour, and the number on screen
        // has to be one the user can act on.
        var currentAsOf = listStatuses.Count > 0 && listStatuses.All(list => list.LastSuccessfulSyncAt is not null)
            ? listStatuses.Min(list => list.LastSuccessfulSyncAt)
            : null;

        return Result.Success(new IndexStatusDto(
            activity,
            currentAsOf,
            HasCompletedFirstScan: listStatuses.Count > 0 && listStatuses.All(list => list.LastCompletedScanAt is not null),
            ListCount: listStatuses.Count,
            ListsIndexed: listStatuses.Count(list => list.LastCompletedScanAt is not null),
            TasksIndexed: listStatuses.Sum(list => list.TasksIndexed),
            Lists: listStatuses,
            LastScanFailure: lastFinished is { State: IndexScanState.Failed }
                ? lastFinished.FailureReason
                : null,
            LastScanFailureCode: lastFinished is { State: IndexScanState.Failed }
                ? lastFinished.FailureCode
                : FailureCode.None));
    }

    /// <summary>
    /// What a list's state means to a reader. A pass writes <c>Scanning</c> before it starts and
    /// the state that replaces it when it ends — so a pass that never ended, because the process
    /// running it did not, leaves the row saying <c>Scanning</c> with nothing left to say
    /// otherwise. With no scan in flight anywhere, the honest answer is what the list was before
    /// that pass: indexed if it has ever been read end to end, and never scanned if it has not.
    /// The next scan sets it properly; until then the UI must not show a spinner nobody is behind.
    /// </summary>
    private static ListScanState Displayed(
        ListScanState stored,
        IndexActivity activity,
        DateTimeOffset? lastCompletedScanAt) =>
        stored is ListScanState.Scanning && activity is IndexActivity.Idle
            ? lastCompletedScanAt is not null ? ListScanState.Indexed : ListScanState.NeverScanned
            : stored;
}
