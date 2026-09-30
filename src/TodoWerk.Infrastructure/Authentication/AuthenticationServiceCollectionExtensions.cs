using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Web;
using TodoWerk.Application.Abstractions.Authentication;

namespace TodoWerk.Infrastructure.Authentication;

public static class AuthenticationServiceCollectionExtensions
{
    /// <summary>
    /// Sign-in: the authorization-code flow against a confidential client, with every Graph token
    /// held server-side and the browser holding only a session cookie (ADR-0002).
    /// <para>
    /// One flow, whether or not the tenant approved TodoWerk centrally. Tenant Consent changes who
    /// is prompted and nothing here — there is no second mode
    /// ([ADR-0008](../../../docs/adr/0008-tenant-consent-is-delegated.md)).
    /// </para>
    /// </summary>
    public static IServiceCollection AddTodoWerkAuthentication(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.AddOptions<EntraIdOptions>()
            .Bind(configuration.GetSection(EntraIdOptions.SectionName))
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.ClientId),
                $"{EntraIdOptions.SectionName}:ClientId must be configured.")
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.ClientSecret),
                $"{EntraIdOptions.SectionName}:ClientSecret must be configured — the backend is a "
                + "confidential client and needs a credential to redeem the authorization code.")
            .Validate(
                options => !string.Equals(options.TenantId, "common", StringComparison.OrdinalIgnoreCase),
                $"{EntraIdOptions.SectionName}:TenantId must not be 'common'. TodoWerk is B2B only; "
                + "use a tenant id or 'organizations'.")
            // Optional, and unset on every deployment with no Teams App Package — but a value that
            // is present and not an absolute URI is a typo, and its consequence is a Teams tab that
            // authenticates nobody with nothing on screen to say why. An empty string binds to
            // nothing and passes here; whitespace binds to a relative URI and does not.
            .Validate(
                options => options.ApplicationIdUri is null or { IsAbsoluteUri: true },
                $"{EntraIdOptions.SectionName}:ApplicationIdUri must be an absolute URI of the form "
                + "api://<host>/<clientId>, or absent. It is the audience a Teams client acquires "
                + "the tab's token for, and Teams single sign-on accepts no other shape.")
            // Without this the checks above never run: nothing resolves IOptions<EntraIdOptions>,
            // because Microsoft.Identity.Web reads the configuration section itself. A
            // misconfigured app would then start clean and fail every single request — health
            // checks included — with an opaque IDW10106 from inside the library.
            .ValidateOnStart();

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, CurrentUser>();
        services.AddScoped<ApiProblemCookieEvents>();

        // OpenID Connect stays the default scheme. Making the cookie the default authenticate
        // scheme looks tempting — it is what populates HttpContext.User for free — but
        // Microsoft.Identity.Web resolves which MicrosoftIdentityOptions to use for token
        // acquisition from the effective scheme, and those options are bound to the OpenID
        // Connect scheme name. Under the cookie scheme they come back unconfigured and every
        // Graph call fails. Anything needing the user before authorization runs must therefore
        // authenticate the cookie scheme explicitly, as AntiforgeryTokenMiddleware does.
        services.AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
            .AddMicrosoftIdentityWebApp(
                configuration.GetSection(EntraIdOptions.SectionName),
                cookieScheme: CookieAuthenticationDefaults.AuthenticationScheme)
            .EnableTokenAcquisitionToCallDownstreamApi([.. GraphScopes.SignIn])
            .AddDistributedTokenCaches();

        // The Teams tab's token, and nothing else. One endpoint names this scheme, and what that
        // endpoint does is exchange the token for the session cookie every other endpoint already
        // wanted — so TodoWerk still has one way of knowing who is calling
        // ([ADR-0010](../../../docs/adr/0010-teams-tab-session-and-framing.md), TeamsSsoDefaults).
        //
        // The same registration and the same secret as the web app above: a Teams client acquires
        // a token for TodoWerk's Application ID URI, and this is TodoWerk receiving it.
        services.AddAuthentication()
            .AddMicrosoftIdentityWebApi(
                configuration.GetSection(EntraIdOptions.SectionName),
                jwtBearerScheme: TeamsSsoDefaults.AuthenticationScheme)
            // No initial scopes: this side never challenges anybody, and the one exchange it
            // performs names the scopes it wants at the call. The cache is the same distributed
            // one the browser path writes to, because it is the same person's tokens.
            .EnableTokenAcquisitionToCallDownstreamApi()
            .AddDistributedTokenCaches();

        // Microsoft.Identity.Web derives the acceptable audiences from the client id alone, which
        // gets `<clientId>` and `api://<clientId>`. TodoWerk's Application ID URI is neither: Teams
        // single sign-on requires the host-qualified form `api://<host>/<clientId>`, which is the
        // audience the Teams client actually asks for. Without this the exchange endpoint refuses
        // every real token with IDX10214 and nothing says why.
        //
        // PostConfigure so it is the last word, and additive so the derived audiences survive —
        // the manual-upload and Store packages both name this URI and a deployment that has no
        // package configures none, which is why the empty case simply changes nothing.
        services.AddOptions<JwtBearerOptions>(TeamsSsoDefaults.AuthenticationScheme)
            .PostConfigure<IOptions<EntraIdOptions>>((options, entraId) =>
            {
                if (entraId.Value.ApplicationIdUri is not { } applicationIdUri)
                {
                    return;
                }

                options.TokenValidationParameters.ValidAudiences =
                [
                    .. options.TokenValidationParameters.ValidAudiences ?? [],
                    applicationIdUri.ToString(),
                ];
            });

        services.AddScoped<TeamsSsoSignIn>();

        services.Configure<CookieAuthenticationOptions>(
            CookieAuthenticationDefaults.AuthenticationScheme,
            options =>
            {
                options.Cookie.Name = "todowerk.session";
                options.Cookie.HttpOnly = true;
                options.Cookie.SecurePolicy = SecureCookiePolicy.For(environment);

                // None outside Development, not Lax and never Strict. Strict would drop the
                // cookie on the OpenID Connect callback, which is a cross-site POST back to the
                // app; None is what the Teams Tab additionally needs, because in the tab this is
                // a third-party cookie and Lax is as absent there as Strict would be (ADR-0010).
                // Unpartitioned by omission, and that is load-bearing: the consent popup sets
                // this cookie in a popped-out window, and a partitioned cookie would land in that
                // window's partition and be invisible to the tab that opened it.
                options.Cookie.SameSite = SecureCookiePolicy.SameSiteFor(environment);

                options.SlidingExpiration = true;
                options.ExpireTimeSpan = TimeSpan.FromHours(8);

                options.EventsType = typeof(ApiProblemCookieEvents);
            });

        // The default challenge scheme is OpenID Connect, which bounces the caller to Entra ID.
        // API endpoints must instead fail closed with a 401 the SPA can act on, so they
        // challenge the cookie scheme: for known API endpoints the cookie handler returns
        // 401/403 rather than redirecting (ASP.NET Core 10 behavior).
        services.AddAuthorizationBuilder()
            .AddPolicy(AuthorizationPolicies.Api, policy => policy
                .AddAuthenticationSchemes(CookieAuthenticationDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser())
            // Used by exactly one endpoint, and it is the one that hands out the cookie the policy
            // above wants. Anything else naming this policy is a bug: it would be a second way of
            // being signed in, which is what ADR-0010 declined.
            .AddPolicy(AuthorizationPolicies.TeamsSso, policy => policy
                .AddAuthenticationSchemes(TeamsSsoDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser());

        return services;
    }
}
