using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TodoWerk.Domain.Changes;
using TodoWerk.Infrastructure.Persistence;

namespace TodoWerk.Infrastructure.Changes.Persistence;

internal sealed class ChangeConfiguration : IEntityTypeConfiguration<Change>
{
    /// <summary>
    /// Room for the folded keys of one Merge. Ten sources at the longest a key can be is about
    /// 2,600 characters; the column is bounded rather than <c>nvarchar(max)</c> so exceeding it is
    /// an error on write and not a Change that quietly lost a source.
    /// </summary>
    internal const int SourceKeysLength = 4000;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public void Configure(EntityTypeBuilder<Change> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Changes");

        builder.HasKey(change => change.Id);

        builder.Property(change => change.TenantId).HasMaxLength(StorageConventions.IdentifierLength).IsRequired();
        builder.Property(change => change.UserId).HasMaxLength(StorageConventions.IdentifierLength).IsRequired();

        // One column rather than a child table: nothing queries by a source key, the set is read
        // whole every time it is read, and a table would add a join to every row of the queue view
        // for a list that is usually one element long.
        builder.Property(change => change.SourceKeys)
            .HasConversion(
                keys => JsonSerializer.Serialize(keys, Json),
                json => JsonSerializer.Deserialize<List<string>>(json, Json) ?? new List<string>(),
                new ValueComparer<IReadOnlyList<string>>(
                    (left, right) => left != null && right != null && left.SequenceEqual(right, StringComparer.Ordinal),
                    keys => keys.Aggregate(0, (hash, key) => HashCode.Combine(hash, StringComparer.Ordinal.GetHashCode(key))),
                    keys => keys.ToList()))
            .HasMaxLength(SourceKeysLength)
            .IsRequired();

        // Not bounded, unlike the sources above, and the reason is the other half of the same
        // argument: the sources are capped at fifteen keys by configuration, while the applied
        // Markers are capped only by how many rules a person may hold — fifty, each carrying a key,
        // a Spelling and up to two emoji. A bounded column would turn "you have a lot of marker
        // rules" into a Change that cannot be confirmed, which is a worse failure than a column
        // nothing indexes or queries by.
        builder.Property(change => change.AppliedMarkers)
            .HasConversion(
                markers => JsonSerializer.Serialize(markers, Json),
                json => JsonSerializer.Deserialize<List<AppliedMarker>>(json, Json) ?? new List<AppliedMarker>(),
                new ValueComparer<IReadOnlyList<AppliedMarker>>(
                    (left, right) => left != null && right != null && left.SequenceEqual(right),
                    markers => markers.Aggregate(0, (hash, marker) => HashCode.Combine(hash, marker.GetHashCode())),
                    markers => markers.ToList()))
            .IsRequired();

        // Binary-collated for the reason ADR-0005 gives: the Spelling is what gets written into
        // somebody's task title, and the database must hold no opinion about which casings are
        // the same string.
        builder.Property(change => change.TargetSpelling)
            .HasMaxLength(Domain.Hashtags.HashtagKey.MaxLength)
            .UseCollation(StorageConventions.KeyCollation)
            .IsRequired();

        // Stored as their names. A numeric discriminator would silently change meaning the day a
        // member is inserted rather than appended, and these columns outlive any one deploy.
        builder.Property(change => change.Kind)
            .HasConversion<string>()
            .HasMaxLength(StorageConventions.EnumNameLength)
            .IsRequired();
        builder.Property(change => change.State)
            .HasConversion<string>()
            .HasMaxLength(StorageConventions.EnumNameLength)
            .IsRequired();
        builder.Property(change => change.FailureCode)
            .HasConversion<string>()
            .HasMaxLength(StorageConventions.EnumNameLength)
            .IsRequired();

        builder.Property(change => change.FailureReason).HasMaxLength(StorageConventions.FailureReasonLength);

        // What the worker asks for on every poll: the oldest Change still waiting. Filtered, so
        // the index stays the size of the queue rather than of thirty days of history.
        builder.HasIndex(change => new { change.State, change.RequestedAt })
            .HasDatabaseName("IX_Changes_State_RequestedAt")
            .HasFilter("[State] IN ('Pending', 'Running')");

        // And what the request path asks: has this user got one in flight, and what does their
        // history look like?
        builder.HasIndex(change => new { change.TenantId, change.UserId, change.RequestedAt })
            .HasDatabaseName("IX_Changes_Tenant_User_RequestedAt");

        // How "has this Change already been undone?" is answered without scanning the table.
        builder.HasIndex(change => change.UndoOfChangeId)
            .HasDatabaseName("IX_Changes_UndoOfChange")
            .HasFilter("[UndoOfChangeId] IS NOT NULL");
    }
}
