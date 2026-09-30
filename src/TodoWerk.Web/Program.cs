using System.Net;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Identity.Web;
using TodoWerk.Application;
using TodoWerk.Infrastructure;
using TodoWerk.Infrastructure.Authentication;
using Microsoft.Extensions.Options;
using TodoWerk.Web.Diagnostics;
using TodoWerk.Web.Documents;
using TodoWerk.Web.Endpoints;
using TodoWerk.Web.Handbook;
using TodoWerk.Web.Legal;
using TodoWerk.Web.Security;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration, builder.Environment);

// What the sign-in callback answers when Entra ID sends back anything but a ticket. Configured
// here rather than inside AddTodoWerkAuthentication because the destination is this layer's
// business — the SPA's card, or the Teams popup's auth-end document — and because it has to be
// layered after Microsoft.Identity.Web has configured the same options.
builder.Services.Configure<OpenIdConnectOptions>(
    OpenIdConnectDefaults.AuthenticationScheme,
    SignInFailure.ChainOnto);

// Behind a TLS-terminating proxy the real scheme and client address arrive as headers. Without
// reading them HTTPS redirection sees plain HTTP and bounces a request that already came in over
// TLS, and every client looks like the proxy — which would collapse the rate limiter into one
// bucket for the whole deployment. Only the proxies named in configuration are believed: trusting
// the header from anyone lets a caller pick their own address, and their own bucket.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

    foreach (var proxy in builder.Configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [])
    {
        options.KnownProxies.Add(Parsed(proxy, IPAddress.Parse, "ForwardedHeaders:KnownProxies"));
    }

    foreach (var network in builder.Configuration.GetSection("ForwardedHeaders:KnownNetworks").Get<string[]>() ?? [])
    {
        options.KnownIPNetworks.Add(Parsed(network, System.Net.IPNetwork.Parse, "ForwardedHeaders:KnownNetworks"));
    }

    // Every other option in this application says what is wrong with it rather than throwing a
    // parse error from somewhere in the startup path.
    static T Parsed<T>(string value, Func<string, T> parse, string key)
    {
        try
        {
            return parse(value);
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException(
                $"{key} contains '{value}', which is not a valid address. TodoWerk only believes "
                + "forwarded headers from the proxies named here, so a typo would silently let "
                + "callers choose their own address.",
                exception);
        }
    }
});

builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.Name = "todowerk.antiforgery";
    options.Cookie.SecurePolicy = SecureCookiePolicy.For(builder.Environment);

    // Not Strict: a Strict cookie — and so every mutating request — would be simply absent inside
    // the Teams tab's iframe (ADR-0010). It carries the secret half of the
    // antiforgery pair, and that pair is now the whole of the CSRF defence: see the comment on
    // SecureCookiePolicy.SameSiteFor before weakening anything here.
    options.Cookie.SameSite = SecureCookiePolicy.SameSiteFor(builder.Environment);
});

// A wide per-user ceiling, not a throttle anyone legitimate should meet: the Workbench polling
// flat out is ~40 requests a minute. What this bounds is the pathological caller — the inventory
// aggregate and the near-duplicate pass do real work per request, and without a ceiling one
// hostile session decides how much of it the process does.
//
// Partitioned by the signed-in user where there is one. The middleware runs after authorization
// for that reason: the authorization policy is what resolves the session cookie into a principal,
// and partitioning before it would put every mutating request into the caller's address bucket —
// behind a proxy, one bucket for everybody.
builder.Services.AddRateLimiter(limiter =>
{
    limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    limiter.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.User.GetObjectId() is { } objectId
                ? $"user:{objectId}"
                : $"address:{context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 600,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            }));
});

// Enums cross the wire as their names. The SPA mirrors these contracts by hand, and a numeric
// `kind` would silently change meaning the day a member is inserted rather than appended.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// Every error response is RFC 7807, including the ones the framework produces.
builder.Services.AddProblemDetails();

builder.Services.AddHealthChecks();

// Who the terms of use name as the operator. Bound rather than read once, so a deployment that
// changes its contact address does not need a code change; rendered once and held thereafter.
builder.Services.Configure<LegalOptions>(builder.Configuration.GetSection(LegalOptions.SectionName));
builder.Services.AddSingleton<LegalPages>();

// Where the About Page points beyond this host, and where the Guide is — every value optional, and
// all of them absent on a Self-Host that has none to name (ADR-0013). Validated at startup: a value
// that is present and not a web address is a typo, and its consequence would be a dead link on the
// one page the Teams admin centre shows as the app's support link.
builder.Services.AddOptions<HandbookOptions>()
    .Bind(builder.Configuration.GetSection(HandbookOptions.SectionName))
    .Validate(
        options => options.AllAbsoluteWebAddresses,
        $"{HandbookOptions.SectionName}: every value must be an absolute http(s) address, or absent.")
    .ValidateOnStart();
builder.Services.AddSingleton(provider => DocumentNav.For(provider.GetRequiredService<IOptions<HandbookOptions>>().Value));
builder.Services.AddSingleton<HandbookPages>();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

// First, so everything after it — the redirect, the cookies, the rate limiter — sees the scheme
// and address the client really used rather than the proxy's.
app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

// Ahead of the static files too, so assets carry the headers as well as documents. The sign-out
// form posts here and is answered with a redirect to the identity provider, so that origin is
// named in form-action or a strict engine blocks the last hop of signing out.
app.UseSecurityHeaders(app.Environment, builder.Configuration["EntraId:Instance"]);

// Ahead of the static-file middleware: serving index.html short-circuits the pipeline, and
// that document is exactly the response the SPA needs its antiforgery token on. It runs before
// authentication, which is why it resolves the signed-in user itself.
app.UseMiddleware<AntiforgeryTokenMiddleware>();

app.UseDefaultFiles();
app.UseStaticFiles();

// Wraps everything that can answer 401, because the one 401 it is watching for is written by the
// authentication middleware below rather than by any endpoint.
app.UseMiddleware<TeamsBootstrapDiagnostics>();

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

// After authorization, which is what resolves the session cookie into a principal there is
// anything to resolve a Licence for — and after the rate limiter, so that a caller cannot spend
// this process's licence resolutions any faster than they can spend anything else.
app.UseLicenceGate();

app.MapHealthChecks("/health").AllowAnonymous();
app.MapVersionEndpoints();
app.MapAuthEndpoints();
app.MapLicenceEndpoints();
app.MapTaskListEndpoints();
app.MapIndexEndpoints();
app.MapChangeEndpoints();
app.MapMarkerRuleEndpoints();
app.MapOnboardingEndpoints();
app.MapTeamsEndpoints();
app.MapLegalEndpoints();
app.MapHandbookEndpoints();

// The SPA owns client-side routing; anything not matched above is its entry document.
app.MapFallbackToFile("index.html").AllowAnonymous();

await app.RunAsync();

/// <summary>Test entry point for <c>WebApplicationFactory</c>.</summary>
public partial class Program;
