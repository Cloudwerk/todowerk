using Microsoft.Extensions.Caching.SqlServer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Web.TokenCacheProviders.Distributed;
using TodoWerk.Infrastructure.Persistence;

namespace TodoWerk.Infrastructure.Authentication;

public static class TokenCacheServiceCollectionExtensions
{
    /// <summary>
    /// In the one database of ADR-0003, created by the <c>DurableTokenCache</c> migration.
    /// Deliberately not an EF entity: the table belongs to the SQL distributed cache, whose
    /// queries dictate its exact shape, and rows are opaque ciphertext — there is no
    /// tenant/user column to hold to the persistence conventions, and nothing should ever
    /// query it through the <see cref="TodoWerkDbContext"/>.
    /// </summary>
    internal const string TableName = "TokenCache";

    internal const string SchemaName = "dbo";

    /// <summary>
    /// How long an entry outlives its last use. Sliding, because every token acquisition —
    /// a visit or a background job alike — proves the entry alive and rotates the refresh
    /// token inside it; 90 days, because that is Entra ID's default refresh-token inactivity
    /// window, so an entry idle longer holds a token that no longer works and is pure
    /// liability. Never left unset: the SQL cache would then apply its own 20-minute default,
    /// and the first background scan starting later than that would find no token.
    /// </summary>
    internal static readonly TimeSpan AbandonedEntryLifetime = TimeSpan.FromDays(90);

    /// <summary>
    /// The durable server-side token cache of ADR-0002: MSAL's cache serialized to SQL Server,
    /// so a deploy does not sign everyone out and a queued job still holds a refresh token
    /// after the browser is long closed. Entries are encrypted at rest through Data
    /// Protection — the same key ring the session cookies already require — and the account's
    /// entry is evicted when Microsoft.Identity.Web handles sign-out.
    /// </summary>
    public static IServiceCollection AddTodoWerkTokenCache(this IServiceCollection services)
    {
        services.AddDistributedSqlServerCache(options =>
        {
            options.SchemaName = SchemaName;
            options.TableName = TableName;
        });

        // The connection string is bound where DatabaseOptions is and validated at startup
        // there; binding it again here would just be a second place to misconfigure.
        services.AddOptions<SqlServerCacheOptions>()
            .Configure<IOptions<DatabaseOptions>>((cache, database) =>
                cache.ConnectionString = database.Value.ConnectionString);

        services.Configure<MsalDistributedTokenCacheAdapterOptions>(options =>
        {
            options.Encrypt = true;
            options.SlidingExpiration = AbandonedEntryLifetime;

            // DisableL1Cache stays false: the in-process L1 spares SQL a read on every token
            // acquisition. Note for the day TodoWerk runs more than one instance: sign-out
            // evicts the SQL row and the evicting instance's L1, but every other instance
            // keeps serving the signed-out user from its own L1 — scaling out requires
            // DisableL1Cache = true.
        });

        return services;
    }
}
