using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TodoWerk.Domain.Changes;
using TodoWerk.Infrastructure.Persistence;

namespace TodoWerk.Infrastructure.Changes.Persistence;

internal sealed class ChangePlanItemConfiguration : IEntityTypeConfiguration<ChangePlanItem>
{
    public void Configure(EntityTypeBuilder<ChangePlanItem> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("ChangePlanItems");

        builder.HasKey(item => item.Id);

        builder.Property(item => item.TenantId).HasMaxLength(StorageConventions.IdentifierLength).IsRequired();
        builder.Property(item => item.UserId).HasMaxLength(StorageConventions.IdentifierLength).IsRequired();

        builder.Property(item => item.TaskListId)
            .HasMaxLength(StorageConventions.GraphIdLength)
            .UseCollation(StorageConventions.GraphIdCollation)
            .IsRequired();
        builder.Property(item => item.GraphTaskId)
            .HasMaxLength(StorageConventions.GraphIdLength)
            .UseCollation(StorageConventions.GraphIdCollation)
            .IsRequired();

        builder.Property(item => item.PreviewedTitle).HasMaxLength(StorageConventions.DisplayTextLength).IsRequired();
        builder.Property(item => item.PreviewedNewTitle).HasMaxLength(StorageConventions.DisplayTextLength).IsRequired();

        builder.Property(item => item.Status)
            .HasConversion<string>()
            .HasMaxLength(StorageConventions.EnumNameLength)
            .IsRequired();

        builder.Property(item => item.Outcome).HasMaxLength(StorageConventions.FailureReasonLength);

        // The plan goes when its Change goes, which is what makes the thirty-day sweep one delete.
        builder.HasOne<Change>()
            .WithMany()
            .HasForeignKey(item => item.ChangeId)
            .OnDelete(DeleteBehavior.Cascade);

        // Exactly the query the runner makes on every task: the next row of this Change to attempt.
        builder.HasIndex(item => new { item.ChangeId, item.Sequence })
            .IsUnique()
            .HasDatabaseName("UX_ChangePlanItems_Change_Sequence");

        // And the one the Workbench makes every couple of seconds while a Change runs: how far
        // each of the user's recent Changes has got. Without it that poll aggregates every plan
        // row of up to fifty Changes off the clustered index — tens of thousands of rows to
        // produce a progress bar.
        builder.HasIndex(item => new { item.ChangeId, item.Status })
            .HasDatabaseName("IX_ChangePlanItems_Change_Status");
    }
}
