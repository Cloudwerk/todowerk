using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TodoWerk.Infrastructure.Persistence.Migrations;

/// <summary>
/// The Markers an Apply carries, on the Change row itself.
/// <para>
/// Safe against a database with Changes in it, and the default value is why: every existing row is
/// one of the three Hashtag Changes and applies no Markers, so it gets an empty JSON array rather
/// than the empty string EF would otherwise backfill — which is not JSON, and which the column's
/// reader would throw on the first time anybody opened their change history.
/// </para>
/// <para>
/// <c>nvarchar(max)</c> rather than a bounded column, unlike the sources beside it: those are
/// capped at fifteen keys by configuration, while these are capped only by how many Marker Rules a
/// person may hold. Nothing queries by this column, so the index it cannot carry is one nobody
/// wanted.
/// </para>
/// <para>
/// Adding a <c>NOT NULL</c> column with a constant default is a metadata-only change on every
/// edition of SQL Server this product runs on (2016 SP1 and later), and the table it is added to
/// holds thirty days of Changes in any case. Rolling back drops the column while a Change of the
/// new kind may still be Pending; such a row would then be one the older runner cannot read, so a
/// rollback is a thing to do with the queue drained, not under it.
/// </para>
/// </summary>
public partial class ApplyMarkersChange : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "AppliedMarkers",
            table: "Changes",
            type: "nvarchar(max)",
            nullable: false,
            defaultValue: "[]");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "AppliedMarkers",
            table: "Changes");
    }
}
