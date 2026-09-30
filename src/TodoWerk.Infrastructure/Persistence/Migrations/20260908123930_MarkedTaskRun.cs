using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TodoWerk.Infrastructure.Persistence.Migrations;

/// <summary>
/// The run of emoji a task's title opens with, on the task row itself.
/// <para>
/// Deciding whether a task carries somebody's Marker is the block grammar, which the database
/// cannot run, so the coverage figure would otherwise have to read the stored title of every task
/// any of the person's emoji appeared in — unbounded by anything but the size of the mailbox, once
/// per load of the rules, with one query parameter per emoji they had ever used. The column turns the
/// half of that question that does not depend on whose rules are being asked about into something
/// the database answers on its own.
/// </para>
/// <para>
/// Binary-collated, like every other emoji comparison in this schema. Under the database's default
/// collation a supplementary character has no weight, so two different emoji compare equal and the
/// empty string equals all of them — which would make the filter below match every row rather than
/// the marked ones.
/// </para>
/// <para>
/// The index is filtered on the same predicate the one reader uses, and carries the run on the key,
/// so that read never touches the table. Most tasks do not open with an emoji, so the index holds a
/// small fraction of the rows.
/// </para>
/// <para>
/// Adding a <c>NOT NULL</c> column with a constant default is a metadata-only change on every
/// edition of SQL Server this product runs on. What it cannot do is backfill: the value is one
/// grapheme walk per title and there is no SQL that computes it, so every existing row starts
/// empty and would stay empty — a delta sync only revisits tasks Graph reports as changed, so a
/// task nobody edits again would never get one.
/// </para>
/// <para>
/// Hence the third statement. Clearing every delta link makes the next sync of each list read it
/// end to end, which rewrites the run on every task through the ordinary write path rather than
/// through a migration that would have to reimplement the grammar in T-SQL.
/// <c>LastCompletedScanAt</c> is deliberately left alone: the lists stay fully indexed, so nothing
/// is excluded from the inventory or from a Change in the meantime (ADR-0003), and the only thing
/// that reads low until the sync lands is the marker coverage figure — which is documented as
/// being as fresh as the last scan in any case.
/// </para>
/// <para>
/// Rolling back drops the column and the index. It does not put the delta links back, because they
/// are Graph's tokens and are not recoverable; the cost of that is one full read per list on the
/// next sync, which is the cold path the product takes anyway.
/// </para>
/// </summary>
public partial class MarkedTaskRun : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.AddColumn<string>(
            name: "LeadingEmoji",
            table: "IndexedTasks",
            type: "nvarchar(256)",
            maxLength: 256,
            nullable: false,
            defaultValue: "",
            collation: "Latin1_General_100_BIN2");

        migrationBuilder.CreateIndex(
            name: "IX_IndexedTasks_Tenant_User_LeadingEmoji",
            table: "IndexedTasks",
            columns: ["TenantId", "UserId", "LeadingEmoji"],
            filter: "[LeadingEmoji] <> N''");

        // The backfill, done by the next sync rather than here. A cleared delta link is what
        // TaskListIndexState.RequireFullResync writes for the same purpose.
        migrationBuilder.Sql("UPDATE [TaskListIndexStates] SET [DeltaLink] = NULL;");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.DropIndex(
            name: "IX_IndexedTasks_Tenant_User_LeadingEmoji",
            table: "IndexedTasks");

        migrationBuilder.DropColumn(
            name: "LeadingEmoji",
            table: "IndexedTasks");
    }
}
