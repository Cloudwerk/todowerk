using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TodoWerk.Infrastructure.Persistence;

/// <summary>
/// How <c>dotnet ef</c> builds a context at design time, so <c>migrations add</c> and
/// <c>database update</c> work without booting the web application — which would demand an
/// Entra ID client secret to scaffold a table.
/// </summary>
public sealed class TodoWerkDbContextFactory : IDesignTimeDbContextFactory<TodoWerkDbContext>
{
    public TodoWerkDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable(DatabaseOptions.EnvironmentVariableName);

        var options = new DbContextOptionsBuilder<TodoWerkDbContext>()
            .UseSqlServer(string.IsNullOrWhiteSpace(connectionString)
                ? DatabaseOptions.LocalDbFallbackConnectionString
                : connectionString)
            .Options;

        return new TodoWerkDbContext(options);
    }
}
