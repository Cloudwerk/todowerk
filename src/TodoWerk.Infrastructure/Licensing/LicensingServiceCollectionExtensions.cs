using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TodoWerk.Application.Abstractions.Erasure;
using TodoWerk.Application.Abstractions.Licensing;
using TodoWerk.Application.Licensing;

namespace TodoWerk.Infrastructure.Licensing;

public static class LicensingServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Licensing module — or, on a Self-Host, deliberately does not.
    /// <para>
    /// The <c>Licensing</c> section is complete or absent. Complete registers the portal client,
    /// the per-person cache and the resolver behind it. Absent registers a resolver that asks
    /// nobody and a reporter that tells nobody, and no <see cref="HttpClient"/> at all — so "makes
    /// no outbound call" is a fact about what exists in the container rather than a branch inside
    /// something that could be reached anyway.
    /// </para>
    /// <para>
    /// Half-filled is neither, and refuses to start. It is the shape a Hosted Service deployment
    /// takes when a secret failed to arrive, and the alternative to failing here is a production
    /// deployment that silently licenses everybody.
    /// </para>
    /// </summary>
    public static IServiceCollection AddTodoWerkLicensing(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var section = configuration.GetSection(LicensingOptions.SectionName);

        services.AddOptions<LicensingOptions>()
            .Bind(section)
            .Validate(
                options => options.IsConfigured || options.IsAbsent,
                $"{LicensingOptions.SectionName} must name a portal host, an application key and a "
                + "solution slug, or none of the three. A Hosted Service needs all of them; a "
                + "Self-Host has no Licence and asks nobody. Some of them is neither, and would "
                + "start as a Hosted Service that cannot reach its portal — which, after the "
                + "fail-open window, denies every one of its users.")
            .Validate(
                options => !options.IsConfigured
                    || Uri.TryCreate(options.PortalHost, UriKind.Absolute, out var host)
                        && (host.Scheme == Uri.UriSchemeHttps || host.Scheme == Uri.UriSchemeHttp),
                $"{LicensingOptions.SectionName}:PortalHost must be an absolute http(s) URL, "
                + "scheme included — the first-party paths are appended to it.")
            // A secret file or an environment variable picks up a trailing newline more often
            // than anybody expects, and the header it goes into refuses one. Caught here, where the
            // message names the setting, rather than as a FormatException per request — which the
            // client survives, but by treating a permanent configuration fault as an outage.
            .Validate(
                options => !options.IsConfigured
                    || options.ApplicationKey.All(character => character is >= ' ' and < (char)127),
                $"{LicensingOptions.SectionName}:ApplicationKey may hold only printable ASCII. A "
                + "trailing newline is the usual cause, and it cannot be sent as a header — every "
                + "resolution would fail and every user would be denied once the fail-open window "
                + "ran out.")
            .Validate(
                options => options.FailOpenWindow > TimeSpan.Zero,
                $"{LicensingOptions.SectionName}:FailOpenWindow must be positive. It is how long "
                + "TodoWerk keeps serving on somebody's last answer while ManagementPortal cannot "
                + "be reached, and zero would deny every user the moment the portal hiccupped.")
            .Validate(
                options => options.TrialEndingSoon > TimeSpan.Zero,
                $"{LicensingOptions.SectionName}:TrialEndingSoon must be positive. It is how close "
                + "to its end a Trial has to be before the banner warns, and zero would mean it "
                + "never did.")
            // The one minute here is also the client's number. The browser gives up on any request
            // after ninety seconds — REQUEST_TIMEOUT_MS in ClientApp/src/api/http.ts — and that is
            // sized as this ceiling plus thirty seconds for everything that is not the portal.
            // The client cannot read this setting, so raising the minute here without raising the
            // ninety there would have browsers abandoning answers this server is about to send.
            .Validate(
                options => options.RequestTimeout > TimeSpan.Zero
                    && options.RequestTimeout <= TimeSpan.FromMinutes(1),
                $"{LicensingOptions.SectionName}:RequestTimeout must be between nothing and one "
                + "minute. This call sits in front of every authenticated request, so a portal "
                + "that has stopped answering must cost a person one timeout rather than one page.")
            .Validate(
                options => options.MaximumCacheLifetime > TimeSpan.Zero
                    && options.MaximumCacheLifetime <= options.FailOpenWindow,
                $"{LicensingOptions.SectionName}:MaximumCacheLifetime must be positive and no "
                + "longer than :FailOpenWindow. It is the ceiling on how long an answer is "
                + "trusted, and an answer trusted for longer than the window that outlives it "
                + "would make the window unreachable.")
            .ValidateOnStart();

        // Chained on both shapes, not only the configured one. What a Self-Host reports is
        // nothing, because the reporter it is given does nothing — and one code path that always
        // reports through the port beats two that have to agree about when not to.
        services.AddOptions<OpenIdConnectOptions>(OpenIdConnectDefaults.AuthenticationScheme)
            .PostConfigure(SeatUsageSignInRecorder.ChainOnto);

        // Bound once here rather than resolved from the container, because what is being decided
        // is which services exist at all. The options validation above runs at startup and says
        // what is wrong with a half-filled section; this only has to tell the two valid shapes
        // apart.
        //
        // Read while services are still being registered, which is the same eager read
        // AddTodoWerkDataProtection makes and carries the same caveat: for a WebApplicationBuilder
        // the sources a test layers through ConfigureAppConfiguration arrive at Build(), after this
        // has run. A test that wants a Hosted Service has to supply these three through the host's
        // own settings — TodoWerkWebApplicationFactory.WithStartupSetting — and LicensingTests says
        // so where somebody copying it will read it.
        var settings = section.Get<LicensingOptions>() ?? new LicensingOptions();

        if (!settings.IsConfigured)
        {
            services.AddSingleton<SelfHostLicenceResolver>();
            services.AddSingleton<ILicenceResolver>(provider => provider.GetRequiredService<SelfHostLicenceResolver>());
            services.AddSingleton<ILicenceGate>(provider => provider.GetRequiredService<SelfHostLicenceResolver>());
            services.AddSingleton<ISeatUsageReporter, SelfHostSeatUsageReporter>();

            return services;
        }

        services.AddSingleton<LicenceCache>();

        services.AddHttpClient(FirstPartyPortalClient.HttpClientName, (provider, client) =>
        {
            var licensing = provider.GetRequiredService<IOptions<LicensingOptions>>().Value;

            client.BaseAddress = new Uri(licensing.PortalHost, UriKind.Absolute);
            client.Timeout = licensing.RequestTimeout;

            // The application key is deliberately not here. It goes on each request inside the
            // client, where a test standing in for the portal can see it: on this registration it
            // would be a fact about the container rather than about the call.
        });

        services.AddSingleton<FirstPartyPortalClient>();

        // One object behind three registrations, for the same reason the Tenant Member store is:
        // the request filter, the Licence endpoint and the background claims must not be able to
        // reach different conclusions about the same person, and they cannot if there is one cache
        // and one set of rules over it.
        services.AddSingleton<PortalLicenceResolver>();
        services.AddSingleton<ILicenceResolver>(provider => provider.GetRequiredService<PortalLicenceResolver>());
        services.AddSingleton<ILicenceGate>(provider => provider.GetRequiredService<PortalLicenceResolver>());

        // Registered by its own type as well, for the same reason the resolver is: the erasure
        // purge has to reach the once-a-day claim it holds, and that claim is keyed on an object id.
        services.AddSingleton<PortalSeatUsageReporter>();
        services.AddSingleton<ISeatUsageReporter>(provider => provider.GetRequiredService<PortalSeatUsageReporter>());

        // Scoped, like every other purge, and last in nobody's order: it evicts a dictionary entry
        // and has no bearing on what the others can still find.
        services.AddScoped<IPersonalDataPurge, LicencePersonalDataPurge>();

        return services;
    }
}
