using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace TodoWerk.Infrastructure.Persistence;

public static class PersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddTodoWerkPersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<DatabaseOptions>()
            .Configure(options => options.ConnectionString =
                configuration.GetConnectionString(DatabaseOptions.ConnectionStringName) ?? string.Empty)
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.ConnectionString),
                $"ConnectionStrings:{DatabaseOptions.ConnectionStringName} must be configured. "
                + "TodoWerk keeps its own index, and there is nowhere to put it without a database "
                + "(ADR-0003).")
            // Present and usable are not the same thing, and the distance between them is the
            // distance between this line and a background worker repeating a stack trace fifteen
            // seconds after the app announced its ports. A mistyped setup step is enough to
            // open it. Parsing is as far as this goes — whether the database also has to answer before the app will serve is a
            // separate decision, and DatabaseReadinessCheck deliberately says no.
            .Validate(
                options => IsParseable(options.ConnectionString),
                $"ConnectionStrings:{DatabaseOptions.ConnectionStringName} is not a SQL Server "
                + "connection string. Expected keywords a driver understands, as in "
                + @"""Server=(localdb)\MSSQLLocalDB;Database=TodoWerk;Trusted_Connection=True"" "
                + "(CONTRIBUTING § Development setup).")
            // Same reason as the Entra ID options: without this, a deployment with no connection
            // string starts clean and fails at the first query instead of at boot.
            .ValidateOnStart();

        services.AddDbContext<TodoWerkDbContext>((provider, builder) =>
        {
            var options = provider.GetRequiredService<IOptions<DatabaseOptions>>().Value;

            builder.UseSqlServer(
                options.ConnectionString,
                // Managed SQL Server offerings drop connections as a matter of routine — failover,
                // scaling, idle reclaim — and the background scan runs long enough to meet one.
                // With retries on, a user-initiated transaction has to be wrapped in the
                // execution strategy, because a retry cannot replay a transaction it did not open.
                sqlServer => sqlServer.EnableRetryOnFailure());
        });

        // Registered ahead of anything that uses the database, so its verdict is the first thing
        // in the log rather than the fourth stack trace.
        services.AddHostedService<DatabaseReadinessCheck>();

        return services;
    }

    /// <summary>
    /// Whether the driver can read the value at all. Asked by building the same
    /// <see cref="SqlConnectionStringBuilder"/> the provider will build later, so this agrees with
    /// the thing that would otherwise throw — rather than with a guess about what a connection
    /// string looks like. An empty value parses happily, which is right: the validator above owns
    /// that case and words it for the operator who left the setting out.
    /// </summary>
    private static bool IsParseable(string connectionString)
    {
        try
        {
            _ = new SqlConnectionStringBuilder(connectionString);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException)
        {
            return false;
        }
    }
}
