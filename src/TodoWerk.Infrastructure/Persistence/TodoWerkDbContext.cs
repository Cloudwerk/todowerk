using Microsoft.EntityFrameworkCore;

namespace TodoWerk.Infrastructure.Persistence;

/// <summary>
/// The one database ADR-0003 calls for: the hashtag index, the sync state, and the job queue,
/// in a single SQL Server reached through EF Core. Self-Host operates one backing service and
/// nothing else.
/// </summary>
public sealed class TodoWerkDbContext(DbContextOptions<TodoWerkDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Entity configurations live with their vertical module — Infrastructure/<Module>/Persistence
        // — and are discovered here rather than listed. Collecting every mapping into this file
        // would make the database the one place every module meets, which is the coupling the
        // module boundaries exist to prevent.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TodoWerkDbContext).Assembly);
    }
}
