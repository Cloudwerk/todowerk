using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TodoWerk.Application.Onboarding;
using TodoWerk.Infrastructure.Onboarding.Persistence;

namespace TodoWerk.Infrastructure.Onboarding;

public static class OnboardingServiceCollectionExtensions
{
    /// <summary>
    /// Registered after <c>AddTodoWerkAuthentication</c>, and the order is load-bearing: this module
    /// hangs the Tenant Member write onto the OpenID Connect handler's <c>OnTokenValidated</c>, and
    /// chaining onto an event only keeps what Microsoft.Identity.Web put there if this runs later.
    /// </summary>
    public static IServiceCollection AddTodoWerkOnboarding(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<OnboardingOptions>()
            .Bind(configuration.GetSection(OnboardingOptions.SectionName))
            .Validate(
                options => options.StatisticsFloor >= 1,
                $"{OnboardingOptions.SectionName}:StatisticsFloor must be at least 1 — the average "
                + "the overview reports divides by the member count, and a floor of zero would let "
                + "it divide by nothing.")
            .Validate(
                options => options.DormancyWindow > TimeSpan.Zero,
                $"{OnboardingOptions.SectionName}:DormancyWindow must be positive. It is how long "
                + "somebody may go without signing in before TodoWerk forgets them, and zero would "
                + "forget everybody on the next sweep.")
            // Bounded above by what a PeriodicTimer will accept, not by taste: it rejects a period
            // past about 49.7 days, so without this ceiling a setting that passed validation would
            // throw on the worker's first line and take the whole host down at boot. A day is well
            // inside that and is already far more often than a twelve-month deadline needs.
            .Validate(
                options => options.SweepInterval >= TimeSpan.FromMinutes(1)
                    && options.SweepInterval <= TimeSpan.FromDays(1)
                    && options.SweepInterval < options.DormancyWindow,
                $"{OnboardingOptions.SectionName}:SweepInterval must be between one minute and one "
                + "day, and shorter than :DormancyWindow — a sweep that runs less often than the "
                + "deadline it enforces cannot keep it, and a period past about 49 days is more than "
                + "a timer will accept at all.")
            .Validate(
                options => options.SweepBatchSize > 0,
                $"{OnboardingOptions.SectionName}:SweepBatchSize must be positive, or no tick would "
                + "forget anybody.")
            .ValidateOnStart();

        // One class, two ports: the sign-in write and the overview read are the same table, and the
        // question they have to agree on — what counts as somebody being here — is one question.
        services.AddScoped<TenantMemberStore>();
        services.AddScoped<ITenantMemberStore>(provider => provider.GetRequiredService<TenantMemberStore>());
        services.AddScoped<ITenantOverviewReader>(provider => provider.GetRequiredService<TenantMemberStore>());

        services.AddScoped<ITenantConsentStore, TenantConsentStore>();
        services.AddScoped<TenantConsentFlow>();

        // Registered by its own type: sweeping is the worker's to do, not a request-path caller's.
        services.AddScoped<RetentionSweep>();

        services.AddOptions<OpenIdConnectOptions>(OpenIdConnectDefaults.AuthenticationScheme)
            .PostConfigure(TenantMemberSignInRecorder.ChainOnto);

        services.AddHostedService<OnboardingBackgroundService>();

        return services;
    }
}
