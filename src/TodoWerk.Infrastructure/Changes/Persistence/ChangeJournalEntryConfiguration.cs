using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TodoWerk.Domain.Changes;
using TodoWerk.Infrastructure.Persistence;

namespace TodoWerk.Infrastructure.Changes.Persistence;

internal sealed class ChangeJournalEntryConfiguration : IEntityTypeConfiguration<ChangeJournalEntry>
{
    public void Configure(EntityTypeBuilder<ChangeJournalEntry> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("ChangeJournalEntries");

        builder.HasKey(entry => entry.Id);

        builder.Property(entry => entry.TenantId).HasMaxLength(StorageConventions.IdentifierLength).IsRequired();
        builder.Property(entry => entry.UserId).HasMaxLength(StorageConventions.IdentifierLength).IsRequired();

        builder.Property(entry => entry.TaskListId)
            .HasMaxLength(StorageConventions.GraphIdLength)
            .UseCollation(StorageConventions.GraphIdCollation)
            .IsRequired();
        builder.Property(entry => entry.GraphTaskId)
            .HasMaxLength(StorageConventions.GraphIdLength)
            .UseCollation(StorageConventions.GraphIdCollation)
            .IsRequired();

        // Compared byte for byte by undo — "restore only if the task still says exactly what we
        // wrote" is a claim about characters, and a case-insensitive collation would let a title
        // somebody re-capitalised pass for untouched.
        builder.Property(entry => entry.TitleBefore)
            .HasMaxLength(StorageConventions.DisplayTextLength)
            .UseCollation(StorageConventions.KeyCollation)
            .IsRequired();
        builder.Property(entry => entry.TitleAfter)
            .HasMaxLength(StorageConventions.DisplayTextLength)
            .UseCollation(StorageConventions.KeyCollation)
            .IsRequired();

        builder.HasOne<Change>()
            .WithMany()
            .HasForeignKey(entry => entry.ChangeId)
            .OnDelete(DeleteBehavior.Cascade);

        // What undo reads: everything one Change wrote, in the order it wrote it.
        builder.HasIndex(entry => new { entry.ChangeId, entry.WrittenAt })
            .HasDatabaseName("IX_ChangeJournalEntries_Change_WrittenAt");
    }
}
