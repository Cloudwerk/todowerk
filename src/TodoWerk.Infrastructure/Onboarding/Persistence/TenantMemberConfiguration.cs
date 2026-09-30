using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TodoWerk.Domain.Onboarding;
using TodoWerk.Infrastructure.Persistence;

namespace TodoWerk.Infrastructure.Onboarding.Persistence;

internal sealed class TenantMemberConfiguration : IEntityTypeConfiguration<TenantMember>
{
    public void Configure(EntityTypeBuilder<TenantMember> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("TenantMembers");

        builder.HasKey(member => member.Id);

        builder.Property(member => member.TenantId).HasMaxLength(StorageConventions.IdentifierLength).IsRequired();

        // Nullable, which is the whole design: erasure removes the object id and leaves the row, so
        // the tenant's cumulative count survives somebody exercising a right and nobody remains
        // identifiable in it.
        builder.Property(member => member.UserId).HasMaxLength(StorageConventions.IdentifierLength);

        // One row per person, and the filter is load-bearing. SQL Server treats NULLs as equal in a
        // unique index, so without it the second anonymised row in a tenant would be rejected as a
        // duplicate key — and erasure would start failing for everybody after the first person in a
        // tenant had used it.
        builder.HasIndex(member => new { member.TenantId, member.UserId })
            .IsUnique()
            .HasFilter("[UserId] IS NOT NULL")
            .HasDatabaseName("UX_TenantMembers_Tenant_User");

        // What the overview asks: the count, the earliest moment, and how many fall inside each
        // trailing window — all over one tenant. The included columns are what it aggregates, so
        // the answer comes out of the index rather than out of the rows.
        builder.HasIndex(member => member.TenantId)
            .IncludeProperties(member => new { member.FirstSignedInAt, member.LastSignedInAt })
            .HasDatabaseName("IX_TenantMembers_Tenant");

        // And what the dormancy sweep asks, across every tenant at once: who has not been back
        // since some moment. Filtered to the rows that still name somebody, because an anonymised
        // row has nobody left to forget and would otherwise be re-examined on every tick forever.
        builder.HasIndex(member => member.LastSignedInAt)
            .HasFilter("[UserId] IS NOT NULL")
            .HasDatabaseName("IX_TenantMembers_LastSignedIn");
    }
}
