using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using TodoWerk.Domain.Hashtags;
using TodoWerk.Domain.Markers;
using TodoWerk.Infrastructure.Persistence;

namespace TodoWerk.Infrastructure.Markers.Persistence;

internal sealed class MarkerRuleConfiguration : IEntityTypeConfiguration<MarkerRule>
{
    /// <summary>
    /// One emoji, stored as its own text. Declared once and used for both columns, because EF
    /// applies a non-nullable converter to a nullable property by leaving null alone — which is
    /// exactly what a rule with nothing retired means.
    /// </summary>
    private static readonly ValueConverter<Marker, string> MarkerText =
        new(marker => marker.Text, text => Marker.Restore(text));

    public void Configure(EntityTypeBuilder<MarkerRule> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("MarkerRules");

        builder.HasKey(rule => rule.Id);

        builder.Property(rule => rule.TenantId).HasMaxLength(StorageConventions.IdentifierLength).IsRequired();
        builder.Property(rule => rule.UserId).HasMaxLength(StorageConventions.IdentifierLength).IsRequired();

        // The same binary collation the Occurrence key carries, and for the same reason: the fold
        // is computed in C# and the database compares the bytes it produced. Without it a rule
        // about #Straße and one about #Strasse would collide on an index the application considers
        // two different Hashtags.
        builder.Property(rule => rule.Key)
            .HasMaxLength(HashtagKey.MaxLength)
            .UseCollation(StorageConventions.KeyCollation)
            .IsRequired();

        builder.Property(rule => rule.Spelling)
            .HasMaxLength(HashtagKey.MaxLength)
            .UseCollation(StorageConventions.KeyCollation)
            .IsRequired();

        // Stored as the emoji's own text. Binary-collated too: SQL Server's default collation has
        // opinions about which of these are equal that the application does not share, and the
        // uniqueness below is the thing that makes a block map one Marker to one Hashtag.
        builder.Property(rule => rule.Marker)
            .HasConversion(MarkerText)
            .HasMaxLength(Marker.MaxLength)
            .UseCollation(StorageConventions.KeyCollation)
            .IsRequired();

        builder.Property(rule => rule.RetiredMarker)
            .HasConversion(MarkerText)
            .HasMaxLength(Marker.MaxLength)
            .UseCollation(StorageConventions.KeyCollation);

        builder.Property(rule => rule.Position).IsRequired();
        builder.Property(rule => rule.CreatedAt).IsRequired();
        builder.Property(rule => rule.DeletedAt);

        // One rule per Hashtag per person, enforced here rather than only checked in code: two
        // tabs adding a rule for the same tag is a race, and the loser has to be told which one it
        // lost rather than quietly making a second rule. Among the rules that stand: a deleted one
        // is kept for its Marker's sake and must not stop the Hashtag getting a new rule.
        builder.HasIndex(rule => new { rule.TenantId, rule.UserId, rule.Key })
            .IsUnique()
            .HasFilter("[DeletedAt] IS NULL")
            .HasDatabaseName("UX_MarkerRules_Tenant_User_Key");

        // And one Hashtag per Marker. The retired Marker is deliberately not in it: a Marker a rule
        // has let go of is free for another rule to take, and refusing it would let a mistake block
        // an emoji until an Apply nobody wanted to run. Bytes, under the binary collation — so two
        // presentations of one emoji would both get in; the store's own check, made with the
        // Marker's equality, is what refuses the second, and this index is the backstop for a race.
        builder.HasIndex(rule => new { rule.TenantId, rule.UserId, rule.Marker })
            .IsUnique()
            .HasFilter("[DeletedAt] IS NULL")
            .HasDatabaseName("UX_MarkerRules_Tenant_User_Marker");

        // The list view, which is every read this module makes.
        builder.HasIndex(rule => new { rule.TenantId, rule.UserId, rule.Position })
            .HasDatabaseName("IX_MarkerRules_Tenant_User_Position");
    }
}
