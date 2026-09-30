using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace TodoWerk.Infrastructure.Persistence;

/// <summary>
/// Says once, shortly after startup, whether the database this instance is pointed at has the
/// schema this build expects.
/// <para>
/// It applies nothing. Migrations are an explicit step in every environment (ADR-0003), and an
/// application that quietly migrates the database it happens to connect to is how a deploy
/// rewrites a schema nobody agreed to. What it prevents is the failure this replaces: the app
/// announcing that it started, and then a background worker repeating "Invalid object name"
/// every poll interval — a stack trace whose actual meaning is one command nobody was told to
/// run.
/// </para>
/// <para>
/// Beside the startup path rather than on it. A database that is briefly unreachable must not
/// hold the application back from listening: the check would then wait out a connection timeout
/// and its retries while requests it could have answered — the health endpoint, the SPA — went
/// unserved.
/// </para>
/// </summary>
internal sealed class DatabaseReadinessCheck(
    IServiceScopeFactory scopeFactory,
    ILogger<DatabaseReadinessCheck> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Guarantees the rest of this runs after the host has finished starting, whatever the
        // provider does synchronously.
        await Task.Yield();

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();

            var pending = await scope.ServiceProvider.GetRequiredService<TodoWerkDbContext>()
                .Database
                .GetPendingMigrationsAsync(stoppingToken);

            var names = pending.ToArray();

            if (names.Length == 0)
            {
                return;
            }

            logger.LogError(
                "The database is missing {Count} migration(s) — {Migrations}. TodoWerk will run but "
                + "nothing that touches the index can work. Apply them with: dotnet ef database update "
                + "--project src/TodoWerk.Infrastructure",
                names.Length,
                string.Join(", ", names));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A database that cannot be reached at this moment is not a reason to refuse to
            // serve — it may well be up before the first request that needs it. Say so, once.
            logger.LogError(
                exception,
                "Could not check the database schema. If this persists, the connection string is "
                + "wrong or the database is unreachable.");
        }
    }
}
