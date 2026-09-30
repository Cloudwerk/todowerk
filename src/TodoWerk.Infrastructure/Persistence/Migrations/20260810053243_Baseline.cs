using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TodoWerk.Infrastructure.Persistence.Migrations;

/// <summary>
/// Deliberately empty. It creates the database and the migration history, and nothing else:
/// the persistence foundation landed before the first entity did, and a database whose history
/// starts at the first table has no record of ever having been empty. The index tables arrive
/// in the next migration.
/// </summary>
public partial class Baseline : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
    }
}
