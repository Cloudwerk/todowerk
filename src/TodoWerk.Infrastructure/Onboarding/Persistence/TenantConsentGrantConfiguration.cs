using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TodoWerk.Domain.Onboarding;
using TodoWerk.Infrastructure.Persistence;

namespace TodoWerk.Infrastructure.Onboarding.Persistence;

internal sealed class TenantConsentGrantConfiguration : IEntityTypeConfiguration<TenantConsentGrant>
{
    public void Configure(EntityTypeBuilder<TenantConsentGrant> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("TenantConsentGrants");

        builder.HasKey(grant => grant.Id);

        builder.Property(grant => grant.TenantId).HasMaxLength(StorageConventions.IdentifierLength).IsRequired();

        // One grant per tenant, enforced rather than assumed: two administrators approving within a
        // second of each other both reach the callback, and the second insert has to lose rather
        // than leave the tenant with two moments and no way to say which one counts.
        builder.HasIndex(grant => grant.TenantId)
            .IsUnique()
            .HasDatabaseName("UX_TenantConsentGrants_Tenant");
    }
}
