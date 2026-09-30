using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TodoWerk.Infrastructure.Persistence;
using Xunit;

namespace TodoWerk.IntegrationTests;

/// <summary>
/// A SQL Server database that belongs to one test class and is dropped with it.
/// <para>
/// Real SQL Server, never SQLite and never EF InMemory (ADR-0003): those providers accept
/// migrations that SQL Server would reject and answer queries SQL Server would answer
/// differently, so a green suite over them says nothing about the database TodoWerk actually
/// runs on. The connection string comes from <c>ConnectionStrings__TodoWerk</c> — which the CI
/// workflow points at its ephemeral SQL Server container — and falls back to LocalDB for a
/// Windows workstation. The database name gets a unique suffix so two classes, two runs, or a
/// run and a developer's own database, never share tables.
/// </para>
/// </summary>
public sealed class SqlServerDatabaseFixture : IAsyncLifetime
{
    public SqlServerDatabaseFixture()
    {
        var configured = Environment.GetEnvironmentVariable(DatabaseOptions.EnvironmentVariableName);

        var builder = new SqlConnectionStringBuilder(
            string.IsNullOrWhiteSpace(configured)
                ? DatabaseOptions.LocalDbFallbackConnectionString
                : configured);

        builder.InitialCatalog = $"{builder.InitialCatalog}_{Guid.NewGuid():N}";

        // Generous, because these are not the timeouts the product ships with. Each class creates
        // and migrates a database of its own, and on a busy workstation LocalDB can take longer
        // over that than the default thirty seconds — reported as a SQL timeout that looks like a
        // product fault and is not one. The classes themselves run one at a time; see the
        // project file.
        builder.CommandTimeout = 180;
        builder.ConnectTimeout = 60;

        ConnectionString = builder.ConnectionString;
    }

    public string ConnectionString { get; }

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    /// <summary>
    /// Drops the run's database. <c>EnsureDeleted</c> is the one EF-managed schema call this
    /// codebase allows: it is teardown, never a substitute for the migrations that create the
    /// schema in the first place.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        var options = new DbContextOptionsBuilder<TodoWerkDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;

        await using var context = new TodoWerkDbContext(options);
        await context.Database.EnsureDeletedAsync();
    }
}
