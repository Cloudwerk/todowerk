using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TodoWerk.Application.Changes;
using TodoWerk.Infrastructure.Changes.Persistence;

namespace TodoWerk.Infrastructure.Changes;

public static class ChangesServiceCollectionExtensions
{
    public static IServiceCollection AddTodoWerkChanges(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<ChangeOptions>()
            .Bind(configuration.GetSection(ChangeOptions.SectionName))
            .Validate(
                options => options.PollInterval > TimeSpan.Zero,
                $"{ChangeOptions.SectionName}:PollInterval must be positive.")
            .Validate(
                options => options.ChangeTimeout > options.PollInterval,
                $"{ChangeOptions.SectionName}:ChangeTimeout must exceed :PollInterval — a timeout at "
                + "or below the poll cadence declares every running change abandoned while it runs.")
            .Validate(
                options => options.MaxTasksPerChange is > 0 and <= 100_000,
                $"{ChangeOptions.SectionName}:MaxTasksPerChange must be between 1 and 100,000.")
            // Bounded by the column the sources are stored in, not by taste: fifteen keys at the
            // longest a Hashtag name can be is about 3,900 characters of JSON against a 4,000
            // character column, and a setting past that would turn a legitimate Merge into a
            // failed insert.
            .Validate(
                options => options.MaxSourcesPerChange is > 0 and <= 15,
                $"{ChangeOptions.SectionName}:MaxSourcesPerChange must be between 1 and 15 — the "
                + "sources are stored as one bounded column, and more would not fit in it.")
            .Validate(
                options => options.ChangeRetention > TimeSpan.Zero,
                $"{ChangeOptions.SectionName}:ChangeRetention must be positive — it is also how long "
                + "undo is offered, because the journal is what undo reads.")
            .ValidateOnStart();

        services.AddScoped<IChangeStore, ChangeStore>();
        services.AddScoped<ITodoTaskWriter, GraphTodoTaskWriter>();
        // Registered by its own type: the worker claims and purges, which is not something a
        // request-path caller may do.
        services.AddScoped<ChangeQueue>();
        services.AddScoped<ChangeRunner>();

        services.AddHostedService<ChangeBackgroundService>();

        return services;
    }
}
