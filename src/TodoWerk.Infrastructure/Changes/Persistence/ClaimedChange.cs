namespace TodoWerk.Infrastructure.Changes.Persistence;

/// <summary>
/// A Change the worker has won: the row's identity, whose tasks it writes, and the lease under
/// which this process may write it.
/// </summary>
/// <remarks>
/// The lease is the <c>StartedAt</c> value the claim wrote. Every later write to the row — a
/// renewal, a terminal state — is conditional on it still being there, which is what lets a
/// process discover that it lost the row to the abandoned-Change recovery instead of silently
/// PATCHing somebody's tasks on behalf of a run that now belongs to another worker. Renewal moves
/// it, so it is the one mutable thing here.
/// </remarks>
internal sealed class ClaimedChange(Guid id, string tenantId, string userId, DateTimeOffset lease)
{
    public Guid Id { get; } = id;

    public string TenantId { get; } = tenantId;

    public string UserId { get; } = userId;

    /// <summary>The <c>StartedAt</c> this process last wrote. Updated by each successful renewal.</summary>
    public DateTimeOffset Lease { get; set; } = lease;
}
