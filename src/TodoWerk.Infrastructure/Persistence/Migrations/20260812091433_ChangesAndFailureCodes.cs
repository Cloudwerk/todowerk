using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TodoWerk.Infrastructure.Persistence.Migrations;

/// <summary>
/// M2's schema: the Change queue, its plan rows and its journal — plus the failure code that
/// retires the client's sentence-matching, on the two tables that already carried a failure
/// sentence. Safe against a populated database: the new columns arrive with a valid enum name
/// rather than the empty string EF would otherwise backfill, and the rows that already failed are
/// corrected to <c>Unknown</c> in the same migration.
/// </summary>
public partial class ChangesAndFailureCodes : Migration
{
    private static readonly string[] JournalByChange = ["ChangeId", "WrittenAt"];

    private static readonly string[] PlanBySequence = ["ChangeId", "Sequence"];

    private static readonly string[] PlanByStatus = ["ChangeId", "Status"];

    private static readonly string[] QueueByState = ["State", "RequestedAt"];

    private static readonly string[] HistoryByUser = ["TenantId", "UserId", "RequestedAt"];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // "None", not the generated empty string: the column stores the enum by name, and a row
        // holding '' would throw on the first read rather than the first write. Rows that already
        // carry a failure sentence are corrected below.
        migrationBuilder.AddColumn<string>(
            name: "FailureCode",
            table: "TaskListIndexStates",
            type: "nvarchar(32)",
            maxLength: 32,
            nullable: false,
            defaultValue: "None");

        migrationBuilder.AddColumn<string>(
            name: "FailureCode",
            table: "IndexScans",
            type: "nvarchar(32)",
            maxLength: 32,
            nullable: false,
            defaultValue: "None");

        // Everything that failed before this migration failed for a reason nobody wrote a code
        // for, and "Unknown" is the honest answer — not "Unavailable", which would claim Microsoft
        // To Do was down. The next attempt overwrites it with the truth either way.
        //
        // Wrapped in EXEC, and not for style. `dotnet ef database update` sends each operation as
        // its own command, so the column added above exists by the time these run. A generated
        // script does not: every statement of one migration lands in a single batch, SQL Server
        // compiles the batch before executing any of it, and resolving a column on a table that
        // already exists happens at compile time — so it fails with "Invalid column name
        // 'FailureCode'" on a database where the ALTER two statements up would have created it.
        // EXEC defers the name resolution to execution time, which is the only difference.
        //
        // Editing an applied migration is safe here because EF tracks migrations by id: an
        // existing database has already had this data change and will not see it again. What
        // changes is what a *new* database gets.
        migrationBuilder.Sql(
            "EXEC(N'UPDATE [TaskListIndexStates] SET [FailureCode] = ''Unknown'' "
            + "WHERE [FailureReason] IS NOT NULL;');");

        migrationBuilder.Sql(
            "EXEC(N'UPDATE [IndexScans] SET [FailureCode] = ''Unknown'' "
            + "WHERE [FailureReason] IS NOT NULL;');");

        migrationBuilder.AddColumn<bool>(
            name: "WasPreempted",
            table: "IndexScans",
            type: "bit",
            nullable: false,
            defaultValue: false);

        migrationBuilder.CreateTable(
            name: "Changes",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                TenantId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                UserId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                SourceKeys = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                TargetSpelling = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false, collation: "Latin1_General_100_BIN2"),
                Kind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                State = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                PlannedTaskCount = table.Column<int>(type: "int", nullable: false),
                CancelRequested = table.Column<bool>(type: "bit", nullable: false),
                UndoOfChangeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                RequestedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                FailureCode = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                FailureReason = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Changes", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "ChangeJournalEntries",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                TenantId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                UserId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                ChangeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                TaskListId = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false, collation: "Latin1_General_100_BIN2"),
                GraphTaskId = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false, collation: "Latin1_General_100_BIN2"),
                TitleBefore = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false, collation: "Latin1_General_100_BIN2"),
                TitleAfter = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false, collation: "Latin1_General_100_BIN2"),
                WrittenAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ChangeJournalEntries", x => x.Id);
                table.ForeignKey(
                    name: "FK_ChangeJournalEntries_Changes_ChangeId",
                    column: x => x.ChangeId,
                    principalTable: "Changes",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "ChangePlanItems",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                TenantId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                UserId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                ChangeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Sequence = table.Column<int>(type: "int", nullable: false),
                TaskListId = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false, collation: "Latin1_General_100_BIN2"),
                GraphTaskId = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false, collation: "Latin1_General_100_BIN2"),
                PreviewedTitle = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                PreviewedNewTitle = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                Outcome = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                AttemptedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ChangePlanItems", x => x.Id);
                table.ForeignKey(
                    name: "FK_ChangePlanItems_Changes_ChangeId",
                    column: x => x.ChangeId,
                    principalTable: "Changes",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ChangeJournalEntries_Change_WrittenAt",
            table: "ChangeJournalEntries",
            columns: JournalByChange);

        migrationBuilder.CreateIndex(
            name: "IX_ChangePlanItems_Change_Status",
            table: "ChangePlanItems",
            columns: PlanByStatus);

        migrationBuilder.CreateIndex(
            name: "UX_ChangePlanItems_Change_Sequence",
            table: "ChangePlanItems",
            columns: PlanBySequence,
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Changes_State_RequestedAt",
            table: "Changes",
            columns: QueueByState,
            filter: "[State] IN ('Pending', 'Running')");

        migrationBuilder.CreateIndex(
            name: "IX_Changes_Tenant_User_RequestedAt",
            table: "Changes",
            columns: HistoryByUser);

        migrationBuilder.CreateIndex(
            name: "IX_Changes_UndoOfChange",
            table: "Changes",
            column: "UndoOfChangeId",
            filter: "[UndoOfChangeId] IS NOT NULL");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "ChangeJournalEntries");

        migrationBuilder.DropTable(
            name: "ChangePlanItems");

        migrationBuilder.DropTable(
            name: "Changes");

        migrationBuilder.DropColumn(
            name: "FailureCode",
            table: "TaskListIndexStates");

        migrationBuilder.DropColumn(
            name: "FailureCode",
            table: "IndexScans");

        migrationBuilder.DropColumn(
            name: "WasPreempted",
            table: "IndexScans");
    }
}
