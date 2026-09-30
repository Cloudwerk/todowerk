using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TodoWerk.Domain.Indexing;
using TodoWerk.Domain.Hashtags;
using TodoWerk.Infrastructure.Persistence;

namespace TodoWerk.Infrastructure.Indexing.Persistence;

internal sealed class HashtagOccurrenceConfiguration : IEntityTypeConfiguration<HashtagOccurrence>
{
    public void Configure(EntityTypeBuilder<HashtagOccurrence> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("HashtagOccurrences");

        builder.HasKey(occurrence => occurrence.Id);

        builder.Property(occurrence => occurrence.TenantId)
            .HasMaxLength(StorageConventions.IdentifierLength)
            .IsRequired();
        builder.Property(occurrence => occurrence.UserId)
            .HasMaxLength(StorageConventions.IdentifierLength)
            .IsRequired();

        builder.Property(occurrence => occurrence.Key)
            .HasMaxLength(HashtagKey.MaxLength)
            .UseCollation(StorageConventions.KeyCollation)
            .IsRequired();

        // Binary too, and for a sharper reason than the key: the inventory counts how many
        // distinct Spellings a Hashtag has, and that count is the casing flag. Under the
        // database's default collation "Work" and "work" are one distinct value, so the flag
        // whose entire purpose is to notice that difference would never once fire.
        builder.Property(occurrence => occurrence.Spelling)
            .HasMaxLength(HashtagKey.MaxLength)
            .UseCollation(StorageConventions.KeyCollation)
            .IsRequired();

        // Deleting an indexed task takes its Occurrences with it, which is what makes a delta page
        // reporting a deletion a single-row operation.
        builder.HasOne<IndexedTask>()
            .WithMany()
            .HasForeignKey(occurrence => occurrence.IndexedTaskId)
            .OnDelete(DeleteBehavior.Cascade);

        // The inventory groups by key within one user, and that is the query the Workbench makes
        // on every page load. The two included columns are what that query aggregates — without
        // them every occurrence row costs a lookup back into the table, which is the difference
        // between answering from the index and only finding the rows with it. Deliberately not
        // unique: a Hashtag has as many rows as it has Occurrences, and the one-row-per-Hashtag
        // table only becomes necessary in M2, when a user-chosen Canonical Spelling needs
        // somewhere to live (ADR-0005).
        builder.HasIndex(occurrence => new { occurrence.TenantId, occurrence.UserId, occurrence.Key })
            .IncludeProperties(occurrence => new { occurrence.IndexedTaskId, occurrence.Spelling })
            .HasDatabaseName("IX_HashtagOccurrences_Tenant_User_Key");

        builder.HasIndex(occurrence => occurrence.IndexedTaskId)
            .HasDatabaseName("IX_HashtagOccurrences_IndexedTask");
    }
}
