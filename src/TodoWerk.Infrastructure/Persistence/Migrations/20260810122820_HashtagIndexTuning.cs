using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TodoWerk.Infrastructure.Persistence.Migrations;

/// <summary>
/// Makes the occurrence index covering: the inventory aggregate reads <c>IndexedTaskId</c> and
/// <c>Spelling</c> for every row it groups, and without them in the leaf each row costs a lookup
/// back into the table.
/// </summary>
public partial class HashtagIndexTuning : Migration
{
    private static readonly string[] KeyColumns = ["TenantId", "UserId", "Key"];

    private static readonly string[] IncludedColumns = ["IndexedTaskId", "Spelling"];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_HashtagOccurrences_Tenant_User_Key",
            table: "HashtagOccurrences");

        migrationBuilder.CreateIndex(
            name: "IX_HashtagOccurrences_Tenant_User_Key",
            table: "HashtagOccurrences",
            columns: KeyColumns)
            .Annotation("SqlServer:Include", IncludedColumns);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_HashtagOccurrences_Tenant_User_Key",
            table: "HashtagOccurrences");

        migrationBuilder.CreateIndex(
            name: "IX_HashtagOccurrences_Tenant_User_Key",
            table: "HashtagOccurrences",
            columns: KeyColumns);
    }
}
