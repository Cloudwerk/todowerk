using Microsoft.EntityFrameworkCore;
using TodoWerk.Application.Abstractions.Indexing;
using TodoWerk.Domain.Indexing;
using TodoWerk.Infrastructure.Persistence;

namespace TodoWerk.Infrastructure.Indexing.Persistence;

/// <summary>
/// How many Occurrences a tenant holds, counted at request time. A filtered aggregate over one
/// tenant's rows, which is what the leading column of
/// <c>IX_HashtagOccurrences_Tenant_User_Key</c> already answers — ADR-0003 says plain SQL covers
/// the access patterns and a count over one tenant does not change that.
/// </summary>
internal sealed class OccurrenceCounter(TodoWerkDbContext context) : IOccurrenceCounter
{
    public Task<long> CountForTenantAsync(string tenantId, CancellationToken cancellationToken) =>
        context.Set<HashtagOccurrence>()
            .AsNoTracking()
            .LongCountAsync(occurrence => occurrence.TenantId == tenantId, cancellationToken);
}
