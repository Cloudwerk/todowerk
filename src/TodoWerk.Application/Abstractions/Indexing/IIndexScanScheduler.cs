using TodoWerk.Application.Abstractions.Authentication;
using TodoWerk.Domain.Indexing;

namespace TodoWerk.Application.Abstractions.Indexing;

/// <summary>
/// Port for asking that a user's index be brought up to date. Implemented in Infrastructure.
/// <para>
/// A shared abstraction rather than part of the Indexing module, because a finished Change has to
/// queue a scan of the lists it touched and must not reach into another module to do it
/// (ADR-0006). The scan <em>mode</em> is still the domain's word for what a pass reads; naming it
/// is not reaching into how the Indexing module runs one.
/// </para>
/// </summary>
public interface IIndexScanScheduler
{
    /// <summary>
    /// Queues a scan unless one is already waiting or running for this user, which is what makes
    /// pressing re-scan twice harmless.
    /// </summary>
    /// <param name="taskListId">One list, or null for all of them.</param>
    /// <returns>True if this call queued the scan; false if it found one already in flight.</returns>
    Task<bool> RequestScanAsync(
        IndexUser user,
        IndexScanMode mode,
        string? taskListId,
        CancellationToken cancellationToken);
}
