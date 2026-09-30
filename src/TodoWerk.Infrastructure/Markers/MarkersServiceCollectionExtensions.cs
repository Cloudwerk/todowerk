using Microsoft.Extensions.DependencyInjection;
using TodoWerk.Application.Abstractions.Markers;
using TodoWerk.Application.Markers;
using TodoWerk.Infrastructure.Markers.Persistence;

namespace TodoWerk.Infrastructure.Markers;

public static class MarkersServiceCollectionExtensions
{
    public static IServiceCollection AddTodoWerkMarkers(this IServiceCollection services)
    {
        services.AddScoped<IMarkerRuleStore, MarkerRuleStore>();

        // The two shared ports, which is how the Changes module reaches rules without depending on
        // this module (ADR-0014).
        services.AddScoped<IMarkerRuleReader, MarkerRuleReader>();
        services.AddScoped<IMarkerRuleMover, MarkerRuleMover>();

        return services;
    }
}
