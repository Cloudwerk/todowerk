using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TodoWerk.Infrastructure.Persistence.Migrations;

/// <summary>
/// The table behind the durable MSAL token cache: the schema
/// <c>Microsoft.Extensions.Caching.SqlServer</c> expects, exactly as its
/// <c>dotnet sql-cache create</c> tool would emit it, carried as a hand-written migration so
/// the one database of ADR-0003 stays entirely under migration control. It is raw SQL rather
/// than model-driven on purpose — the table is the cache implementation's, not the domain's,
/// and must not appear in the EF model (see <c>TokenCacheServiceCollectionExtensions</c>).
/// </summary>
public partial class DurableTokenCache : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Id is nvarchar(449): the longest key that still fits SQL Server's 900-byte limit for
        // a clustered index. The case-sensitive collation is load-bearing — cache keys are
        // case-sensitive strings, and a case-insensitive collation would let two distinct keys
        // collide on the primary key.
        migrationBuilder.Sql("""
            CREATE TABLE [dbo].[TokenCache] (
                [Id] nvarchar(449) COLLATE SQL_Latin1_General_CP1_CS_AS NOT NULL,
                [Value] varbinary(max) NOT NULL,
                [ExpiresAtTime] datetimeoffset NOT NULL,
                [SlidingExpirationInSeconds] bigint NULL,
                [AbsoluteExpiration] datetimeoffset NULL,
                CONSTRAINT [PK_TokenCache] PRIMARY KEY CLUSTERED ([Id])
            );
            """);

        // The cache's periodic clean-up deletes by expiry, which is a table scan without this.
        migrationBuilder.Sql("""
            CREATE NONCLUSTERED INDEX [Index_ExpiresAtTime]
                ON [dbo].[TokenCache] ([ExpiresAtTime]);
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Reversal costs every user a sign-in, never a wrong answer: the cache is rebuilt
        // from fresh sign-ins.
        migrationBuilder.Sql("DROP TABLE [dbo].[TokenCache];");
    }
}
