using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace TodoWerk.Infrastructure.Graph;

/// <summary>
/// The one typed client every Graph call rides on, registered once for every module that makes
/// one — the index reads through it, a Change writes through it.
/// </summary>
public static class GraphServiceCollectionExtensions
{
    public static IServiceCollection AddTodoWerkGraph(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<GraphOptions>()
            .Bind(configuration.GetSection(GraphOptions.SectionName))
            .Validate(
                options => options.MaxRetryDelay >= TimeSpan.Zero,
                $"{GraphOptions.SectionName}:MaxRetryDelay must not be negative.")
            .Validate(
                options => options.MaxThrottleRetries is >= 0 and <= 32,
                $"{GraphOptions.SectionName}:MaxThrottleRetries must be between 0 and 32.")
            .ValidateOnStart();

        services.AddHttpClient<GraphGateway>(client =>
        {
            client.BaseAddress = new Uri("https://graph.microsoft.com/");
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        return services;
    }
}
