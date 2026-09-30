using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TodoWerk.Infrastructure.Persistence.Migrations;

/// <summary>
/// A deleted Marker Rule is kept and marked rather than removed.
/// <para>
/// Deleting a rule writes nothing (ADR-0014), so its Marker stays at the front of every title the
/// rule was applied to — and a block reader that no longer recognised that Marker would read the
/// block as ending before it, and the next Apply would put a second block in front of the first.
/// The row stays so the Marker stays known. Both uniqueness rules become rules about the rules that
/// stand, so a deleted rule neither blocks the Hashtag getting a new one nor holds the emoji.
/// </para>
/// <para>
/// Safe against a populated table: the column is nullable and arrives null, which reads as "not
/// deleted" for every existing row. Rebuilding the two unique indexes with a filter is the same
/// keys over the same rows on a table that holds at most fifty rows per person. Rolling back
/// removes deleted rules' rows with the column — which is what a delete used to do.
/// </para>
/// </summary>
public partial class MarkerRuleSoftDelete : Migration
{
    private const string Standing = "[DeletedAt] IS NULL";

    private static readonly string[] RulesByKey = ["TenantId", "UserId", "Key"];

    private static readonly string[] RulesByMarker = ["TenantId", "UserId", "Marker"];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "UX_MarkerRules_Tenant_User_Key",
            table: "MarkerRules");

        migrationBuilder.DropIndex(
            name: "UX_MarkerRules_Tenant_User_Marker",
            table: "MarkerRules");

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "DeletedAt",
            table: "MarkerRules",
            type: "datetimeoffset",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "UX_MarkerRules_Tenant_User_Key",
            table: "MarkerRules",
            columns: RulesByKey,
            unique: true,
            filter: Standing);

        migrationBuilder.CreateIndex(
            name: "UX_MarkerRules_Tenant_User_Marker",
            table: "MarkerRules",
            columns: RulesByMarker,
            unique: true,
            filter: Standing);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "UX_MarkerRules_Tenant_User_Key",
            table: "MarkerRules");

        migrationBuilder.DropIndex(
            name: "UX_MarkerRules_Tenant_User_Marker",
            table: "MarkerRules");

        // The rows a delete used to remove, removed — so the unfiltered indexes can be rebuilt over
        // rules that stand and nothing else.
        migrationBuilder.Sql("DELETE FROM [MarkerRules] WHERE [DeletedAt] IS NOT NULL;");

        migrationBuilder.DropColumn(
            name: "DeletedAt",
            table: "MarkerRules");

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
}
