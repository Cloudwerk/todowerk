using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TodoWerk.Domain.Indexing;
using TodoWerk.Infrastructure.Persistence;

namespace TodoWerk.Infrastructure.Indexing.Persistence;

internal sealed class TaskListIndexStateConfiguration : IEntityTypeConfiguration<TaskListIndexState>
{
    /// <summary>
    /// Graph's delta links carry an opaque token in the query string and are the longest thing
    /// this schema stores. Bounded rather than <c>nvarchar(max)</c> so a truncation shows up as an
    /// error on write instead of as a delta pass that silently starts over.
    /// </summary>
    internal const int DeltaLinkLength = 2048;

    public void Configure(EntityTypeBuilder<TaskListIndexState> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("TaskListIndexStates");

        builder.HasKey(state => state.Id);

        builder.Property(state => state.TenantId).HasMaxLength(StorageConventions.IdentifierLength).IsRequired();
        builder.Property(state => state.UserId).HasMaxLength(StorageConventions.IdentifierLength).IsRequired();
        builder.Property(state => state.TaskListId)
            .HasMaxLength(StorageConventions.GraphIdLength)
            .UseCollation(StorageConventions.GraphIdCollation)
            .IsRequired();
        builder.Property(state => state.DisplayName)
            .HasMaxLength(StorageConventions.DisplayTextLength)
            .IsRequired();

        // Stored as its name. A numeric discriminator would silently change meaning the day a
        // member is inserted rather than appended, and this column outlives any one deploy.
        builder.Property(state => state.State)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(state => state.DeltaLink).HasMaxLength(DeltaLinkLength);
        builder.Property(state => state.FailureReason).HasMaxLength(StorageConventions.FailureReasonLength);

        // Stored beside the sentence, not instead of it: the sentence is what a reader sees, and
        // this is what decides whether to offer a sign-in button, so nothing looks for a phrase.
        builder.Property(state => state.FailureCode)
            .HasConversion<string>()
            .HasMaxLength(StorageConventions.EnumNameLength)
            .IsRequired();

        // Binary-collated like every other Graph id: two of a user's lists whose ids
        // differ only in case are two lists, and this index has to agree — a collision here does
        // not fail one list, it fails the scan before a single list has been read.
        builder.HasIndex(state => new { state.TenantId, state.UserId, state.TaskListId })
            .IsUnique()
            .HasDatabaseName("UX_TaskListIndexStates_Tenant_User_TaskList");
    }
}
