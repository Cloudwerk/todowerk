using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using TodoWerk.Infrastructure.Authentication;

namespace TodoWerk.IntegrationTests;

/// <summary>
/// Boots the real application with the real authentication pipeline. Only the Entra ID discovery
/// document is pre-supplied — endpoints and the id token signing key — so a challenge builds a
/// genuine authorization-code redirect without any network call, and <see cref="TestSignIn"/> can
/// walk the callback through the real correlation-cookie and token validation. Not nonce
/// validation: the framework does not compare the nonce on pure code flow, which
/// <c>SignInFlowTests</c> measures and pins rather than assumes.
/// </summary>
public sealed class TodoWerkWebApplicationFactory : WebApplicationFactory<Program>
{
    /// <summary>The tenant every test runs against. A guid, so tenanted-authority checks hold.</summary>
    public const string TenantId = "00000000-0000-0000-0000-000000000000";

    public const string ClientId = "11111111-1111-1111-1111-111111111111";

    public const string ClientSecret = "integration-test-secret";

    private const string Authority = $"https://login.microsoftonline.com/{TenantId}/v2.0";

    /// <summary>
    /// Valid enough to satisfy startup validation, unreachable on purpose. A test that needs a
    /// database asks for one through <see cref="SqlServerDatabaseFixture"/> and overrides this;
    /// a test that accidentally opens a connection fails loudly here rather than quietly
    /// finding a developer's own database.
    /// </summary>
    public const string UnreachableConnectionString =
        "Server=no-test-should-connect-here;Database=TodoWerk;Trusted_Connection=True";

    /// <summary>
    /// The Application ID URI a Teams client would acquire its token for — the host-qualified
    /// form Teams single sign-on requires, which is neither of the two audiences
    /// Microsoft.Identity.Web derives from the client id on its own.
    /// </summary>
    public const string ApplicationIdUri = $"api://todowerk.test/{ClientId}";

    /// <summary>
    /// The settings that have no default anywhere and that a host will not start without. Public
    /// because <see cref="ConfigurationValidationTests"/> asks what one changed setting does to
    /// startup validation, and a second hand-written copy of "a configuration that passes" would
    /// drift from this one.
    /// <para>
    /// Not the whole of what a host started here reads: the factory's content root is the web
    /// project, so the application also layers its own <c>appsettings.json</c> underneath — the
    /// <c>Indexing</c> section among it. That section is only absent from a container built from
    /// this dictionary alone because every one of its settings has a matching default in
    /// <c>IndexingOptions</c>. Should the two ever disagree, the test using this will say so by
    /// failing on an option nobody changed.
    /// </para>
    /// </summary>
    public static Dictionary<string, string?> BaseConfiguration() => new()
    {
        ["EntraId:TenantId"] = TenantId,
        ["EntraId:ClientId"] = ClientId,
        ["EntraId:ClientSecret"] = ClientSecret,
        ["EntraId:ApplicationIdUri"] = ApplicationIdUri,
        ["ConnectionStrings:TodoWerk"] = UnreachableConnectionString,
    };

    private readonly Dictionary<string, string?> _configurationOverrides = [];

    private readonly Dictionary<string, string?> _startupSettings = [];

    private Func<HttpMessageHandler>? _outboundHandlerFactory;

    private HttpMessageHandler? _sharedOutboundHandler;

    private ILoggerProvider? _loggerProvider;

    private bool _withoutBackgroundWorkers;

    private string _environmentName = Environments.Development;

    /// <summary>
    /// Boots the host as something other than Development. Two things TodoWerk does only outside
    /// Development are worth a test — the content security policy, and the cookie attributes that
    /// make the Teams frame possible — and neither is observable from a Development host.
    /// <para>
    /// A host started this way must be given a Data Protection key ring and a certificate to
    /// encrypt it with, because outside Development the absence of either is a deliberate refusal
    /// to start. <see cref="DeployedLikeConfiguration"/> supplies both.
    /// </para>
    /// </summary>
    public TodoWerkWebApplicationFactory WithEnvironment(string environmentName)
    {
        _environmentName = environmentName;
        return this;
    }

    /// <summary>Replaces one configuration value, for tests that assert on startup validation.</summary>
    public TodoWerkWebApplicationFactory WithConfigurationOverride(string key, string? value)
    {
        _configurationOverrides[key] = value;
        return this;
    }

    /// <summary>
    /// Replaces one configuration value and makes it visible while the application is still
    /// registering its services, which an ordinary override is not.
    /// <para>
    /// Almost nothing needs this: TodoWerk binds its options lazily, so a value supplied the
    /// ordinary way is there by the time anything reads it. <c>AddTodoWerkDataProtection</c> is the
    /// exception — it reads its three settings straight off <c>IConfiguration</c> while services
    /// are being registered, and outside Development it refuses to start without two of them.
    /// </para>
    /// <para>
    /// Kept separate rather than applied to everything, because it is not free: a host given a Data
    /// Protection key ring this way really persists keys to it, where one that never sees the
    /// setting falls back to the machine's own. Handing that to every test changed the behaviour of
    /// tests that had no interest in it.
    /// </para>
    /// </summary>
    public TodoWerkWebApplicationFactory WithStartupSetting(string key, string? value)
    {
        _startupSettings[key] = value;
        return this;
    }

    /// <summary>
    /// Leaves every background worker out of the host — the scan worker and the change worker
    /// alike — for tests that drive the queues themselves.
    /// <para>
    /// A long poll interval does not do this: a worker runs its first tick the moment it starts
    /// and only then waits, so with one present a test that queues work is racing a second
    /// claimant for the row it just wrote. That race decides nothing about the product — it only
    /// decides whether the test sees what it set up.
    /// </para>
    /// </summary>
    public TodoWerkWebApplicationFactory WithoutBackgroundWorkers()
    {
        _withoutBackgroundWorkers = true;
        return this;
    }

    /// <summary>
    /// Routes every outbound <see cref="HttpClient"/> the application creates — MSAL's calls to
    /// Entra ID and the typed Graph client alike — through handlers from this factory, so a
    /// test can stand in for both clouds without the application noticing. Nothing else in the
    /// app makes HTTP calls: the OpenID Connect handler's discovery is pre-supplied above. A
    /// factory rather than an instance, because <see cref="IHttpClientFactory"/> disposes the
    /// handlers it builds pipelines from — each pipeline gets its own.
    /// </summary>
    public TodoWerkWebApplicationFactory WithOutboundHttpHandler(Func<HttpMessageHandler> handlerFactory)
    {
        _outboundHandlerFactory = handlerFactory;
        return this;
    }

    /// <summary>
    /// The same, for one handler the test keeps a reference to — a handler that records what the
    /// application sent, which can only answer "every call it made" if every pipeline shares one
    /// instance.
    /// <para>
    /// Sharing it means the client factory must not dispose it, so handler rotation is turned off
    /// for this host: an entry that expires — two minutes by default — disposes the primary
    /// handler it was built from, and a handler disposed under the pipelines still holding it
    /// fails every later call with <see cref="ObjectDisposedException"/>, in the middle of a test
    /// that has no idea why. Ownership stays with the caller, who disposes it when the host is
    /// gone.
    /// </para>
    /// </summary>
    public TodoWerkWebApplicationFactory WithSharedOutboundHttpHandler(HttpMessageHandler handler)
    {
        _sharedOutboundHandler = handler;
        return this;
    }

    /// <summary>
    /// Keeps everything the host logs, for the one test whose subject is a log line rather than a
    /// response. Added as a provider rather than replacing the logging configuration, so what is
    /// recorded is what a deployment would have written.
    /// </summary>
    public TodoWerkWebApplicationFactory WithLoggerProvider(ILoggerProvider provider)
    {
        _loggerProvider = provider;
        return this;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(_environmentName);

        // The few settings that have to be readable while Program's own body is still running,
        // pushed through UseSetting because ConfigureAppConfiguration cannot do it: for a
        // WebApplicationBuilder those sources are layered at Build() time, after the body has run
        // and after AddInfrastructure has already read what it needs.
        foreach (var (key, value) in _startupSettings)
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(BaseConfiguration());

            if (_configurationOverrides.Count > 0)
            {
                configuration.AddInMemoryCollection(_configurationOverrides);
            }

            // Last, so a value asked for at startup is also the value the running app reads: host
            // settings sit early in the chain and appsettings.json would otherwise win over them.
            if (_startupSettings.Count > 0)
            {
                configuration.AddInMemoryCollection(_startupSettings);
            }
        });

        builder.ConfigureTestServices(services =>
        {
            services.Configure<OpenIdConnectOptions>(
                OpenIdConnectDefaults.AuthenticationScheme,
                options =>
                {
                    var configuration = new OpenIdConnectConfiguration
                    {
                        Issuer = Authority,
                        AuthorizationEndpoint = $"{Authority}/authorize",
                        TokenEndpoint = $"{Authority}/token",
                        EndSessionEndpoint = $"{Authority}/logout",
                    };

                    // The one thing a discovery document supplies that a challenge does not need
                    // and a completed sign-in cannot do without: the key the id token is signed
                    // with. Supplied here rather than served from the fake's JWKS endpoint because
                    // the configuration is pre-supplied, so the handler never fetches it.
                    configuration.SigningKeys.Add(FakeEntraSigning.PublicKey);

                    options.Configuration = configuration;
                });

            // The issuer, and only the issuer.
            //
            // Microsoft.Identity.Web installs an AAD issuer validator that works out which issuers
            // are acceptable by fetching the authority's own metadata — over an HttpClient of its
            // own, which is not the one this factory can stand in for. The tenant here is a guid
            // Microsoft has never heard of, so that fetch fails however the test is wired, and every
            // completed sign-in dies on IDX40001 after passing everything else. Replaced with an
            // exact match against the issuer the fake stamps into its tokens.
            //
            // A PostConfigure, because the validator is installed as the options are configured and
            // this has to be the last word. Nothing else about validation is relaxed: the signature,
            // the issuer, the audience and the lifetime are all still checked, and the sign-in
            // tests turn on those plus the correlation cookie. The nonce is deliberately not in
            // that list — the framework does not compare it on code flow, and SignInFlowTests pins
            // that measured fact rather than letting this comment claim otherwise.
            services.PostConfigure<OpenIdConnectOptions>(
                OpenIdConnectDefaults.AuthenticationScheme,
                options =>
                {
                    options.TokenValidationParameters.ValidIssuer = Authority;
                    options.TokenValidationParameters.IssuerValidator = null;
                });

            // The Teams SSO scheme, given the same two things the OpenID Connect one is given: the
            // key its tokens are signed with, and an issuer to match exactly. Without the first it
            // would fetch a JWKS document over a back-channel this factory cannot stand in for;
            // without the second every token dies on IDX40001 inside Microsoft.Identity.Web's
            // metadata-fetching issuer validator, for a tenant Microsoft has never heard of.
            services.PostConfigure<JwtBearerOptions>(
                TeamsSsoDefaults.AuthenticationScheme,
                options =>
                {
                    var configuration = new OpenIdConnectConfiguration { Issuer = Authority };
                    configuration.SigningKeys.Add(FakeEntraSigning.PublicKey);

                    // The manager, not only the configuration. JwtBearer's own post-configure has
                    // already built a metadata-fetching one from the authority by the time this
                    // runs, and the handler prefers whatever manager it finds — so setting
                    // Configuration alone leaves the fetch in place and every token dies on
                    // IDX10500 with no keys, which reads like a signing problem and is not one.
                    options.Configuration = configuration;
                    options.ConfigurationManager =
                        new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);

                    options.TokenValidationParameters.ValidIssuer = Authority;
                    options.TokenValidationParameters.IssuerValidator = null;
                });

            if (_loggerProvider is not null)
            {
                services.AddSingleton(_loggerProvider);
            }

            if (_outboundHandlerFactory is not null)
            {
                var handlerFactory = _outboundHandlerFactory;
                services.ConfigureHttpClientDefaults(http =>
                    http.ConfigurePrimaryHttpMessageHandler(handlerFactory));
            }

            if (_sharedOutboundHandler is not null)
            {
                var shared = _sharedOutboundHandler;
                services.ConfigureHttpClientDefaults(http => http
                    .ConfigurePrimaryHttpMessageHandler(() => shared)
                    .SetHandlerLifetime(Timeout.InfiniteTimeSpan));
            }

            if (_withoutBackgroundWorkers)
            {
                services.RemoveAll<Microsoft.Extensions.Hosting.IHostedService>();
            }
        });
    }
}
