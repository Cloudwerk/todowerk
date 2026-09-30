using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TodoWerk.Infrastructure.Persistence.Migrations;

/// <summary>
/// The hashtag index itself: indexed tasks, the Hashtag Occurrences read out of their titles, the
/// per-list sync state the Workbench shows as freshness, and the queue the background scan drains.
/// <para>
/// Every table is new, so <c>Down</c> drops them and there is nothing to backfill. Both Hashtag
/// columns are binary-collated on purpose (ADR-0005): the key so the database compares the bytes
/// C# folded, and the Spelling so counting distinct spellings can see the difference between
/// <c>#Work</c> and <c>#work</c>, which is the whole casing flag.
/// </para>
/// <para>
/// The column arrays are hoisted into fields because the index columns repeat across tables and
/// the build treats CA1861 as an error (CONTRIBUTING § Code style: no per-file suppressions).
/// </para>
/// </summary>
public partial class HashtagIndex : Migration
{
    private static readonly string[] TenantUserKeyColumns = ["TenantId", "UserId", "Key"];

    private static readonly string[] TenantUserTaskListColumns = ["TenantId", "UserId", "TaskListId"];

    private static readonly string[] TenantUserGraphTaskColumns = ["TenantId", "UserId", "GraphTaskId"];

    private static readonly string[] TenantUserStateColumns = ["TenantId", "UserId", "State"];

    private static readonly string[] StateRequestedAtColumns = ["State", "RequestedAt"];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "IndexedTasks",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                TenantId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                UserId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                TaskListId = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                GraphTaskId = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                Title = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                LastModifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_IndexedTasks", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "IndexScans",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                TenantId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                UserId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                Mode = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                TaskListId = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                State = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                RequestedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                FailureReason = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_IndexScans", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "TaskListIndexStates",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                TenantId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                UserId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                TaskListId = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                DisplayName = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                State = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                DeltaLink = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                TasksIndexed = table.Column<int>(type: "int", nullable: false),
                LastSuccessfulSyncAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                LastCompletedScanAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                LastAttemptAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                FailureReason = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_TaskListIndexStates", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "HashtagOccurrences",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                TenantId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                UserId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                IndexedTaskId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Key = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false, collation: "Latin1_General_100_BIN2"),
                Spelling = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false, collation: "Latin1_General_100_BIN2")
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_HashtagOccurrences", x => x.Id);
                table.ForeignKey(
                    name: "FK_HashtagOccurrences_IndexedTasks_IndexedTaskId",
                    column: x => x.IndexedTaskId,
                    principalTable: "IndexedTasks",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_HashtagOccurrences_IndexedTask",
            table: "HashtagOccurrences",
            column: "IndexedTaskId");

        migrationBuilder.CreateIndex(
            name: "IX_HashtagOccurrences_Tenant_User_Key",
            table: "HashtagOccurrences",
            columns: TenantUserKeyColumns);

        migrationBuilder.CreateIndex(
            name: "IX_IndexedTasks_Tenant_User_TaskList",
            table: "IndexedTasks",
            columns: TenantUserTaskListColumns);

        migrationBuilder.CreateIndex(
            name: "UX_IndexedTasks_Tenant_User_GraphTask",
            table: "IndexedTasks",
            columns: TenantUserGraphTaskColumns,
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_IndexScans_State_RequestedAt",
            table: "IndexScans",
            columns: StateRequestedAtColumns,
            filter: "[State] IN ('Pending', 'Running')");

        migrationBuilder.CreateIndex(
            name: "IX_IndexScans_Tenant_User_State",
            table: "IndexScans",
            columns: TenantUserStateColumns);

        migrationBuilder.CreateIndex(
            name: "UX_TaskListIndexStates_Tenant_User_TaskList",
            table: "TaskListIndexStates",
            columns: TenantUserTaskListColumns,
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "HashtagOccurrences");

        migrationBuilder.DropTable(
            name: "IndexScans");

        migrationBuilder.DropTable(
            name: "TaskListIndexStates");

        migrationBuilder.DropTable(
            name: "IndexedTasks");
    }
}
