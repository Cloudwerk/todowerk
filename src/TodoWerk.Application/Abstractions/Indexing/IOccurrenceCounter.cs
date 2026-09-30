namespace TodoWerk.Application.Abstractions.Indexing;

/// <summary>
/// How many Hashtag Occurrences one tenant's people hold between them.
/// <para>
/// A shared port for the same reason <see cref="IIndexScanScheduler"/> is one: the Tenant Overview
/// reports this number and lives in the Onboarding module, which may not reach into Indexing. What
/// crosses the boundary is a count and only a count — never which Hashtags exist, never a
/// per-person figure, and never anything that would let one colleague compare their tags with
/// another's. That is a domain decision about Hashtag identity across people, not a reporting
/// change, and it is out of scope.
/// </para>
/// </summary>
public interface IOccurrenceCounter
{
    Task<long> CountForTenantAsync(string tenantId, CancellationToken cancellationToken);
}
