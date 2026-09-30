using Microsoft.Extensions.DependencyInjection;
using TodoWerk.Application.Abstractions.Behaviors;
using TodoWerk.Application.Abstractions.Messaging;
using TodoWerk.Application.Changes;
using TodoWerk.Application.Markers;
using TodoWerk.Application.Onboarding;

namespace TodoWerk.Application;

public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.Scan(scan => scan
            .FromAssemblies(typeof(ApplicationServiceCollectionExtensions).Assembly)
            .AddClasses(classes => classes.AssignableTo(typeof(IQueryHandler<,>)), publicOnly: false)
                .AsImplementedInterfaces()
                .WithScopedLifetime()
            .AddClasses(classes => classes.AssignableTo(typeof(ICommandHandler<>)), publicOnly: false)
                .AsImplementedInterfaces()
                .WithScopedLifetime()
            .AddClasses(classes => classes.AssignableTo(typeof(ICommandHandler<,>)), publicOnly: false)
                .AsImplementedInterfaces()
                .WithScopedLifetime());

        // Shared by the preview and the confirmation, which must not be able to disagree about
        // what a Change covers. Not a handler, so the scan above does not find it.
        services.AddScoped<ChangePlanner>();

        // Shared by the person asking to be forgotten and the sweep that forgets somebody dormant,
        // which must not be able to mean different things by it. Not a handler either.
        services.AddScoped<PersonalDataEraser>();

        // How far each Marker Rule has been applied. Not a handler either, and not part of the
        // Markers store: what counts as marked is the block grammar, which the store has no
        // business knowing and the index cannot be asked.
        services.AddScoped<MarkerCoverage>();

        // Cross-cutting concerns are Scrutor decorators over the handler interfaces. Only queries
        // are decorated: the logging decorator exists to time reads, and a command that writes to
        // somebody's tasks already logs where it matters, in the worker that performs the write.
        services.Decorate(typeof(IQueryHandler<,>), typeof(LoggingQueryHandlerDecorator<,>));

        return services;
    }
}
