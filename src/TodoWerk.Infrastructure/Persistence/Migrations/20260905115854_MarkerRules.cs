using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TodoWerk.Infrastructure.Persistence.Migrations;

/// <summary>
/// M7's schema: the Marker Rule, which is the first thing TodoWerk stores that a person chose
/// rather than observed.
/// <para>
/// Purely additive and empty on arrival, so it is safe against a populated database and there is
/// nothing to backfill — a rule cannot be reconstructed from Graph, which is exactly why it does
/// not live beside the Occurrences the index rebuilds at will (ADR-0014).
/// </para>
/// <para>
/// Both emoji columns carry the binary collation the folded Hashtag key does, and for the same
/// reason: the application decides which values are equal and the database compares the bytes it
/// was given. The uniqueness on the Marker is what makes a block map one Marker to one Hashtag;
/// <c>RetiredMarker</c> is deliberately outside it, so a Marker one rule has let go of is free for
/// another to take.
/// </para>
/// </summary>
public partial class MarkerRules : Migration
{
    private static readonly string[] RulesByPosition = ["TenantId", "UserId", "Position"];

    private static readonly string[] RulesByKey = ["TenantId", "UserId", "Key"];

    private static readonly string[] RulesByMarker = ["TenantId", "UserId", "Marker"];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "MarkerRules",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                TenantId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                UserId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                Key = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false, collation: "Latin1_General_100_BIN2"),
                Spelling = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false, collation: "Latin1_General_100_BIN2"),
                Marker = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false, collation: "Latin1_General_100_BIN2"),
                RetiredMarker = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true, collation: "Latin1_General_100_BIN2"),
                Position = table.Column<int>(type: "int", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_MarkerRules", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_MarkerRules_Tenant_User_Position",
            table: "MarkerRules",
            columns: RulesByPosition);

        migrationBuilder.CreateIndex(
            name: "UX_MarkerRules_Tenant_User_Key",
            table: "MarkerRules",
            columns: RulesByKey,
            unique: true);

        migrationBuilder.CreateIndex(
            name: "UX_MarkerRules_Tenant_User_Marker",
            table: "MarkerRules",
            columns: RulesByMarker,
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "MarkerRules");
    }
}
