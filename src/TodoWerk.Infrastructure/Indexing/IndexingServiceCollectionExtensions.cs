using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TodoWerk.Application.Indexing;
using TodoWerk.Infrastructure.Indexing.Persistence;
using TodoWerk.Infrastructure.Graph;
using TodoWerk.Application.Abstractions.Indexing;

namespace TodoWerk.Infrastructure.Indexing;

public static class IndexingServiceCollectionExtensions
{
    public static IServiceCollection AddTodoWerkIndexing(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<IndexingOptions>()
            .Bind(configuration.GetSection(IndexingOptions.SectionName))
            .Validate(
                options => options.PollInterval > TimeSpan.Zero && options.SyncInterval > TimeSpan.Zero,
                $"{IndexingOptions.SectionName}:PollInterval and :SyncInterval must be positive.")
            .Validate(
                options => options.ScanTimeout > options.SyncInterval,
                $"{IndexingOptions.SectionName}:ScanTimeout must exceed :SyncInterval — a timeout "
                + "at or below the sync cadence declares every ordinary scan abandoned while it runs.")
            .Validate(
                options => options.IdleAfter > options.SyncInterval,
                $"{IndexingOptions.SectionName}:IdleAfter must exceed :SyncInterval — a window at or "
                + "below the sync cadence calls everybody idle before their next sync is due, which "
                + "switches scheduled syncs off for the whole deployment without saying so.")
            .Validate(
                options => options.FinishedScanRetention >= options.SyncInterval,
                $"{IndexingOptions.SectionName}:FinishedScanRetention must be at least :SyncInterval — "
                + "the scheduler reads recent scan rows to know a user was tried lately.")
            .Validate(
                options => options.NearDuplicateMinimumLength >= 1
                    && options.NearDuplicateMaximumLength >= options.NearDuplicateMinimumLength,
                $"{IndexingOptions.SectionName}:NearDuplicate lengths must be positive, maximum >= minimum.")
            .ValidateOnStart();

        services.AddMemoryCache();

        services.AddScoped<ITaskListsReader, GraphTaskListsReader>();
        services.AddScoped<ITodoTaskReader, GraphTodoTaskReader>();
        // Registered by its own type as well as behind the port: the background worker asks it to
        // schedule due syncs, which is not something a request-path caller can do.
        services.AddScoped<IndexScanScheduler>();
        services.AddScoped<IIndexScanScheduler>(provider => provider.GetRequiredService<IndexScanScheduler>());
        services.AddScoped<IIndexStatusReader, IndexStatusReader>();
        // The index, read for somebody else's sake: a Change is planned from these rows and must
        // not reach into this module for them (ADR-0006).
        services.AddScoped<ITaggedTaskReader, TaggedTaskReader>();
        services.AddScoped<ITaggedTitleReader, TaggedTitleReader>();
        services.AddScoped<IHashtagInventoryReader, HashtagInventoryReader>();
        // The Occurrence total the Tenant Overview reports. Behind a port for the same reason the
        // scheduler is: the screen belongs to another module, and what crosses the boundary is a
        // count rather than anything about which Hashtags exist.
        services.AddScoped<IOccurrenceCounter, OccurrenceCounter>();
        services.AddScoped<IndexScanRunner>();

        services.AddHostedService<IndexScanBackgroundService>();

        return services;
    }
}
