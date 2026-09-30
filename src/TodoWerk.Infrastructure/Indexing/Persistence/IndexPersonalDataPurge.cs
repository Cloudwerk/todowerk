using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using TodoWerk.Application.Abstractions.Authentication;
using TodoWerk.Application.Abstractions.Erasure;
using TodoWerk.Domain.Indexing;
using TodoWerk.Infrastructure.Persistence;

namespace TodoWerk.Infrastructure.Indexing.Persistence;

/// <summary>
/// Destroys one person's index: their queued and finished scans, their per-list sync state, their
/// indexed tasks and the Hashtag Occurrences hanging off them — and the near-duplicate result
/// memoised for them in this process.
/// <para>
/// The indexed titles are the sensitive part of everything TodoWerk holds, so this is the purge the
/// promise is really about.
/// </para>
/// </summary>
internal sealed class IndexPersonalDataPurge(TodoWerkDbContext context, IMemoryCache memoryCache)
    : IPersonalDataPurge
{
    /// <summary>
    /// How many times the deletes are repeated while they keep finding rows.
    /// <para>
    /// Two things can write here while an erasure runs. A scan running for this person is stopped by
    /// the first statement below — it renews its lease after every page and gives up when the row it
    /// claimed has gone — but it may write one more page first. And the scheduler that queues due
    /// syncs reads its candidates and inserts scan rows in separate statements, so one can land after
    /// the scans were cleared. Repeating catches both; the bound is here so that a defect somewhere
    /// else cannot turn this into a loop that never ends, and reaching it is reported rather than
    /// mistaken for finishing.
    /// </para>
    /// </summary>
    private const int MaximumPasses = 3;

    public string Describes => "the hashtag index";

    public async Task<PurgeOutcome> PurgeAsync(IndexUser user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);

        // First, and on its own: this is what stops a scan that is running for this person, so every
        // later statement is racing less. It is repeated in the loop below all the same, because
        // stopping the runner is not the same as stopping the scheduler.
        var removed = await Scans(user).ExecuteDeleteAsync(cancellationToken);

        var nothingLeft = false;

        for (var pass = 0; pass < MaximumPasses; pass++)
        {
            // Occurrences before the tasks they belong to. The foreign key cascades, so the second
            // delete would take them anyway — spelling it out means this holds whether or not a
            // future migration keeps the cascade.
            var occurrences = await context.Set<HashtagOccurrence>()
                .Where(occurrence => occurrence.TenantId == user.TenantId && occurrence.UserId == user.UserId)
                .ExecuteDeleteAsync(cancellationToken);

            var tasks = await context.Set<IndexedTask>()
                .Where(task => task.TenantId == user.TenantId && task.UserId == user.UserId)
                .ExecuteDeleteAsync(cancellationToken);

            var states = await context.Set<TaskListIndexState>()
                .Where(state => state.TenantId == user.TenantId && state.UserId == user.UserId)
                .ExecuteDeleteAsync(cancellationToken);

            var scans = await Scans(user).ExecuteDeleteAsync(cancellationToken);

            var found = occurrences + tasks + states + scans;
            removed += found;

            if (found == 0)
            {
                nothingLeft = true;
                break;
            }
        }

        // The inventory memoises each person's near-duplicate pairing for a minute, and that set is
        // their Hashtag names. Deleting the rows it was computed from does not remove it from memory,
        // so the cache entry is evicted by the same name the reader stores it under.
        //
        // In-process only, like the token cache's L1. On one instance that is the whole of it; the
        // day TodoWerk runs more than one, every other instance keeps its copy until
        // NearDuplicateCacheLifetime expires it — a minute by default, of Hashtag names and nothing
        // else. Recorded in status.md rather than solved here, alongside the other scale-out note.
        memoryCache.Remove(HashtagInventoryReader.NearDuplicateCacheKey(user));

        return nothingLeft ? PurgeOutcome.Complete(removed) : PurgeOutcome.Unfinished(removed);
    }

    private IQueryable<IndexScan> Scans(IndexUser user) =>
        context.Set<IndexScan>()
            .Where(scan => scan.TenantId == user.TenantId && scan.UserId == user.UserId);
}
