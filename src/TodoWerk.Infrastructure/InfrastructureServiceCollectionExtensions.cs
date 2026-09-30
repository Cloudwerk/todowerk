using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using TodoWerk.Application.Abstractions.Erasure;
using TodoWerk.Infrastructure.Authentication;
using TodoWerk.Infrastructure.Changes;
using TodoWerk.Infrastructure.Changes.Persistence;
using TodoWerk.Infrastructure.Graph;
using TodoWerk.Infrastructure.Indexing;
using TodoWerk.Infrastructure.Indexing.Persistence;
using TodoWerk.Infrastructure.Licensing;
using TodoWerk.Infrastructure.Markers;
using TodoWerk.Infrastructure.Markers.Persistence;
using TodoWerk.Infrastructure.Onboarding;
using TodoWerk.Infrastructure.Persistence;

namespace TodoWerk.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        // The clock, injected everywhere rather than read from DateTimeOffset.UtcNow: the scan
        // schedules itself against it, and a test that cannot move time cannot prove that.
        services.TryAddSingleton(TimeProvider.System);

        services.AddTodoWerkPersistence(configuration);
        services.AddTodoWerkDataProtection(configuration, environment);
        services.AddTodoWerkAuthentication(configuration, environment);
        services.AddTodoWerkTokenCache();
        services.AddTodoWerkGraph(configuration);
        services.AddTodoWerkIndexing(configuration);
        services.AddTodoWerkChanges(configuration);
        services.AddTodoWerkMarkers();
        // Before Onboarding, so that the sign-in recorders Onboarding chains onto the OpenID
        // Connect handler can ask for a seat reporter that is already registered. A Self-Host
        // registers one that does nothing, which is what makes "no outbound call" true there.
        services.AddTodoWerkLicensing(configuration);
        // After authentication, deliberately: Onboarding chains the Tenant Member write onto the
        // OpenID Connect handler's token-validated event, and chaining only preserves what
        // Microsoft.Identity.Web put there when it is configured afterwards.
        services.AddTodoWerkOnboarding(configuration);

        // Erasure is orchestrated in the Application layer and performed here, once per place
        // personal data accumulates. Registered in this order because it is the order they run in,
        // and the first one is the one that matters: evicting the token cache is what stops new
        // background work from starting as somebody who is being forgotten.
        services.AddScoped<IPersonalDataPurge, TokenCachePersonalDataPurge>();
        services.AddScoped<IPersonalDataPurge, IndexPersonalDataPurge>();
        services.AddScoped<IPersonalDataPurge, ChangePersonalDataPurge>();
        services.AddScoped<IPersonalDataPurge, MarkerPersonalDataPurge>();

        return services;
    }
}
