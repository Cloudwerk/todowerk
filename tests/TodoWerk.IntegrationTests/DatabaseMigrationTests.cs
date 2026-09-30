using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using TodoWerk.Domain.Indexing;
using TodoWerk.Infrastructure.Persistence;
using Xunit;

namespace TodoWerk.IntegrationTests;

/// <summary>
/// The checked-in migrations, applied to the real SQL Server through the application's own
/// registration (ADR-0003). Nothing here calls <c>EnsureCreated</c>: a database built from
/// today's model rather than from the migrations has no migration history and is a database no
/// deployment will ever have.
/// </summary>
public sealed class DatabaseMigrationTests(SqlServerDatabaseFixture database)
    : IClassFixture<SqlServerDatabaseFixture>
{
    /// <summary>
    /// The migration the index was built by before the Graph ids became case-sensitive. Named
    /// rather than derived, because the point of the test below is this exact starting schema.
    /// </summary>
    private const string MigrationBeforeTheFix = "20260810122820_HashtagIndexTuning";

    /// <summary>
    /// One test rather than two, because the interesting part is the sequence: the migrations
    /// have to reach a server where this database does not exist yet, and the second run has to
    /// be a no-op — every deploy applies them again, and a procedure that first has to work out
    /// whether the last deploy already migrated is a procedure that will get it wrong.
    /// </summary>
    [Fact]
    public async Task Migrations_AppliedToAServerWithoutTheDatabase_CreateItAndAreIdempotent()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new TodoWerkWebApplicationFactory()
            .WithConfigurationOverride("ConnectionStrings:TodoWerk", database.ConnectionString);

        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TodoWerkDbContext>();

        await context.Database.MigrateAsync(cancellationToken);

        Assert.True(await context.Database.CanConnectAsync(cancellationToken));
        Assert.NotEmpty(await context.Database.GetAppliedMigrationsAsync(cancellationToken));
        Assert.Empty(await context.Database.GetPendingMigrationsAsync(cancellationToken));

        await context.Database.MigrateAsync(cancellationToken);

        Assert.Empty(await context.Database.GetPendingMigrationsAsync(cancellationToken));
    }

    /// <summary>
    /// The collation migration, against a database that already has rows: the columns
    /// are altered with both unique indexes over them, and every list is put back in line for a
    /// full read. That second part is the repair — a database indexed before the migration may be
    /// missing tasks its case-insensitive index swallowed, and no delta pass would ever mention
    /// them again, because Graph already reported them once.
    /// </summary>
    [Fact]
    public async Task TheCollationMigration_ClearsTheDeltaLinksOfAnIndexBuiltBeforeIt()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new TodoWerkWebApplicationFactory()
            .WithConfigurationOverride("ConnectionStrings:TodoWerk", database.ConnectionString);

        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TodoWerkDbContext>();
        var migrator = context.GetService<IMigrator>();

        // The state of a deployment that indexed before the fix — reached by migrating down if
        // the other test in this class got here first, which also exercises Down.
        await migrator.MigrateAsync(MigrationBeforeTheFix, cancellationToken);

        // Written as SQL rather than through the model: the model is the one at HEAD, and the
        // point of the row is that it predates the schema this test is about to apply.
        await context.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO TaskListIndexStates
                (Id, TenantId, UserId, TaskListId, DisplayName, State, DeltaLink, TasksIndexed,
                 LastSuccessfulSyncAt, LastCompletedScanAt, LastAttemptAt)
            VALUES
                ({Guid.CreateVersion7()}, {TodoWerkWebApplicationFactory.TenantId},
                 {FakeEntraAndGraphHandler.UserObjectId}, 'AAMkAGxpc3RlAAWPkADl', 'Arbeit',
                 'Indexed', 'https://graph.microsoft.com/v1.0/me/todo/lists/x/tasks/delta?$deltatoken=42',
                 614, {DateTimeOffset.UtcNow}, {DateTimeOffset.UtcNow}, {DateTimeOffset.UtcNow});
            """,
            cancellationToken);

        await migrator.MigrateAsync(cancellationToken: cancellationToken);

        var state = await context.Set<TaskListIndexState>().AsNoTracking().SingleAsync(cancellationToken);

        Assert.Null(state.DeltaLink);
        Assert.False(state.CanSyncIncrementally);

        // Cleared so the scheduler finds this user due at the next poll rather than a sync
        // interval later.
        Assert.Null(state.LastSuccessfulSyncAt);
        Assert.Null(state.LastAttemptAt);

        // And nothing else was thrown away: the rows the index does hold are still worth having,
        // and the re-read replaces them in place.
        Assert.Equal(614, state.TasksIndexed);
        Assert.NotNull(state.LastCompletedScanAt);
    }
}
