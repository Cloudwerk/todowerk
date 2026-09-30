namespace TodoWerk.Infrastructure.Persistence;

/// <summary>
/// Where the database is. Configuration, never code: every installation points the same binary
/// at its own SQL Server (ADR-0003).
/// </summary>
public sealed class DatabaseOptions
{
    /// <summary>Named connection string — <c>ConnectionStrings:TodoWerk</c>.</summary>
    public const string ConnectionStringName = "TodoWerk";

    /// <summary>
    /// The same setting as an environment variable, which is how anything running outside the
    /// web application's configuration finds the database: <c>dotnet ef</c> at design time, and
    /// the integration tests.
    /// </summary>
    public const string EnvironmentVariableName = "ConnectionStrings__" + ConnectionStringName;

    /// <summary>
    /// The database those two use when nothing said otherwise. Design-time and test convenience
    /// on a Windows workstation, and nothing more — the running application has no fallback and
    /// refuses to start without a configured connection string.
    /// </summary>
    public const string LocalDbFallbackConnectionString =
        @"Server=(localdb)\MSSQLLocalDB;Database=TodoWerk;Trusted_Connection=True;TrustServerCertificate=True";

    public string ConnectionString { get; set; } = string.Empty;
}
