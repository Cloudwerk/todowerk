using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TodoWerk.Domain.Indexing;
using TodoWerk.Infrastructure.Persistence;

namespace TodoWerk.Infrastructure.Indexing.Persistence;

internal sealed class IndexScanConfiguration : IEntityTypeConfiguration<IndexScan>
{
    public void Configure(EntityTypeBuilder<IndexScan> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("IndexScans");

        builder.HasKey(scan => scan.Id);

        builder.Property(scan => scan.TenantId).HasMaxLength(StorageConventions.IdentifierLength).IsRequired();
        builder.Property(scan => scan.UserId).HasMaxLength(StorageConventions.IdentifierLength).IsRequired();
        // Binary-collated like every other Graph id. Nothing is unique here, but a
        // single-list scan is matched against this column — under a case-insensitive collation a
        // request for one list would be answered by, and swallowed by, a queued scan of another.
        builder.Property(scan => scan.TaskListId)
            .HasMaxLength(StorageConventions.GraphIdLength)
            .UseCollation(StorageConventions.GraphIdCollation);

        builder.Property(scan => scan.Mode).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(scan => scan.State).HasConversion<string>().HasMaxLength(32).IsRequired();

        builder.Property(scan => scan.FailureReason)
            .HasMaxLength(StorageConventions.FailureReasonLength);

        // Stored beside the sentence, not instead of it: the sentence is what a reader
        // sees, and this is what decides what the client offers about it.
        builder.Property(scan => scan.FailureCode)
            .HasConversion<string>()
            .HasMaxLength(StorageConventions.EnumNameLength)
            .IsRequired();

        // What the worker asks for on every poll: the oldest scan still waiting. Filtered, so the
        // index stays the size of the queue rather than of its history.
        builder.HasIndex(scan => new { scan.State, scan.RequestedAt })
            .HasDatabaseName("IX_IndexScans_State_RequestedAt")
            .HasFilter("[State] IN ('Pending', 'Running')");

        // And what a re-scan request asks: has this user got one queued already?
        builder.HasIndex(scan => new { scan.TenantId, scan.UserId, scan.State })
            .HasDatabaseName("IX_IndexScans_Tenant_User_State");
    }
}
