using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TodoWerk.Domain.Hashtags;
using TodoWerk.Domain.Indexing;
using TodoWerk.Infrastructure.Persistence;

namespace TodoWerk.Infrastructure.Indexing.Persistence;

internal sealed class IndexedTaskConfiguration : IEntityTypeConfiguration<IndexedTask>
{
    public void Configure(EntityTypeBuilder<IndexedTask> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("IndexedTasks");

        builder.HasKey(task => task.Id);

        builder.Property(task => task.TenantId).HasMaxLength(StorageConventions.IdentifierLength).IsRequired();
        builder.Property(task => task.UserId).HasMaxLength(StorageConventions.IdentifierLength).IsRequired();
        builder.Property(task => task.TaskListId)
            .HasMaxLength(StorageConventions.GraphIdLength)
            .UseCollation(StorageConventions.GraphIdCollation)
            .IsRequired();
        builder.Property(task => task.GraphTaskId)
            .HasMaxLength(StorageConventions.GraphIdLength)
            .UseCollation(StorageConventions.GraphIdCollation)
            .IsRequired();
        builder.Property(task => task.Title).HasMaxLength(StorageConventions.DisplayTextLength).IsRequired();

        // Binary-collated, for the reason every other emoji comparison here is: under the
        // database's default collation a supplementary character has no weight, so two different
        // emoji compare equal and the empty string equals all of them — which would make the
        // filtered index below match every row (see TaggedTitleReader).
        builder.Property(task => task.LeadingEmoji)
            .HasMaxLength(MarkerBlock.MaxLeadingEmojiLength)
            .UseCollation(StorageConventions.KeyCollation)
            .IsRequired();
        builder.Property(task => task.LastModifiedAt).IsRequired();

        // A delta page identifies a task by its Graph id and nothing else, so this is the lookup
        // every incremental sync makes. Unique because one Graph task is one row per user; the
        // list is not part of it, because a task can be moved between lists and must not clone.
        // The invariant is only enforceable at all because the id column is binary-collated: under
        // the database's default collation this index reads two different tasks as one.
        builder.HasIndex(task => new { task.TenantId, task.UserId, task.GraphTaskId })
            .IsUnique()
            .HasDatabaseName("UX_IndexedTasks_Tenant_User_GraphTask");

        // The scan rewrites a whole list, and the Workbench counts per list.
        builder.HasIndex(task => new { task.TenantId, task.UserId, task.TaskListId })
            .HasDatabaseName("IX_IndexedTasks_Tenant_User_TaskList");

        // Marker coverage asks one question of this table — "which of my tasks open with an emoji,
        // and what is it" — and the answer is a small fraction of the rows. Filtered so the index
        // holds only those, and covering so the read never touches the table itself: the run is on
        // the key, and nothing else about the task is wanted.
        builder.HasIndex(task => new { task.TenantId, task.UserId, task.LeadingEmoji })
            .HasFilter("[LeadingEmoji] <> N''")
            .HasDatabaseName("IX_IndexedTasks_Tenant_User_LeadingEmoji");
    }
}
