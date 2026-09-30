using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TodoWerk.Infrastructure.Persistence.Migrations;

/// <summary>
/// Re-collates every column holding an id Microsoft Graph issued to <c>Latin1_General_100_BIN2</c>.
/// <para>
/// Graph writes task and list ids as case-sensitive base64. Under the database's default
/// <c>SQL_Latin1_General_CP1_CI_AS</c> two ids differing in one letter's case are one value, so
/// <c>UX_IndexedTasks_Tenant_User_GraphTask</c> rejected the second of two legitimately different
/// tasks as a duplicate key — and because a delta page is deterministic, every retry of that list
/// failed identically and the list never finished indexing. Such a pair is structural rather than
/// unlucky: Exchange item ids share long prefixes and vary at the tail.
/// </para>
/// <para>
/// Both unique indexes span a re-collated column, so SQL Server cannot alter the columns in place
/// — the generated script drops each index, alters, and recreates it. Widening a comparison is
/// safe against rows already there: binary equality is strictly narrower than case-insensitive
/// equality, so nothing that fits the old index can collide under the new one. <c>Down</c> is the
/// direction that can fail, and it fails loudly, on exactly the rows this migration exists to
/// allow.
/// </para>
/// </summary>
public partial class GraphIdBinaryCollation : Migration
{
    private const string GraphIdCollation = "Latin1_General_100_BIN2";

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<string>(
            name: "TaskListId",
            table: "TaskListIndexStates",
            type: "nvarchar(512)",
            maxLength: 512,
            nullable: false,
            collation: GraphIdCollation,
            oldClrType: typeof(string),
            oldType: "nvarchar(512)",
            oldMaxLength: 512);

        migrationBuilder.AlterColumn<string>(
            name: "TaskListId",
            table: "IndexScans",
            type: "nvarchar(512)",
            maxLength: 512,
            nullable: true,
            collation: GraphIdCollation,
            oldClrType: typeof(string),
            oldType: "nvarchar(512)",
            oldMaxLength: 512,
            oldNullable: true);

        migrationBuilder.AlterColumn<string>(
            name: "TaskListId",
            table: "IndexedTasks",
            type: "nvarchar(512)",
            maxLength: 512,
            nullable: false,
            collation: GraphIdCollation,
            oldClrType: typeof(string),
            oldType: "nvarchar(512)",
            oldMaxLength: 512);

        migrationBuilder.AlterColumn<string>(
            name: "GraphTaskId",
            table: "IndexedTasks",
            type: "nvarchar(512)",
            maxLength: 512,
            nullable: false,
            collation: GraphIdCollation,
            oldClrType: typeof(string),
            oldType: "nvarchar(512)",
            oldMaxLength: 512);

        // Every index built before this point is incomplete, and the schema change does not repair
        // it: the tasks a collision swallowed were never written, and a delta pass will not mention
        // them again — Graph already reported them once. Dropping the delta link is what turns the
        // next pass into a full read; clearing the two timestamps is what makes it happen at the
        // next poll rather than up to a sync interval later. Nothing here is destructive: the
        // indexed rows stay, and the Workbench shows freshness as unknown for the minutes it takes
        // to be true again.
        migrationBuilder.Sql("""
            UPDATE TaskListIndexStates
            SET DeltaLink = NULL, LastSuccessfulSyncAt = NULL, LastAttemptAt = NULL;
            """);
    }

    /// <summary>
    /// Reverses the collation only. The delta links this migration cleared are gone — Graph issued
    /// them and nothing kept a copy — so a reverted database re-reads every list once, which is
    /// what it would have to do anyway. If two ids that differ only in case are already stored, the
    /// unique indexes cannot be recreated under the old collation and this fails rather than
    /// quietly dropping one of the two tasks.
    /// </summary>
    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<string>(
            name: "TaskListId",
            table: "TaskListIndexStates",
            type: "nvarchar(512)",
            maxLength: 512,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "nvarchar(512)",
            oldMaxLength: 512,
            oldCollation: GraphIdCollation);

        migrationBuilder.AlterColumn<string>(
            name: "TaskListId",
            table: "IndexScans",
            type: "nvarchar(512)",
            maxLength: 512,
            nullable: true,
            oldClrType: typeof(string),
            oldType: "nvarchar(512)",
            oldMaxLength: 512,
            oldNullable: true,
            oldCollation: GraphIdCollation);

        migrationBuilder.AlterColumn<string>(
            name: "TaskListId",
            table: "IndexedTasks",
            type: "nvarchar(512)",
            maxLength: 512,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "nvarchar(512)",
            oldMaxLength: 512,
            oldCollation: GraphIdCollation);

        migrationBuilder.AlterColumn<string>(
            name: "GraphTaskId",
            table: "IndexedTasks",
            type: "nvarchar(512)",
            maxLength: 512,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "nvarchar(512)",
            oldMaxLength: 512,
            oldCollation: GraphIdCollation);
    }
}
