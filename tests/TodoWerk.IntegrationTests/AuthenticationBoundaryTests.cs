using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace TodoWerk.IntegrationTests;

public sealed class AuthenticationBoundaryTests(TodoWerkWebApplicationFactory factory)
    : IClassFixture<TodoWerkWebApplicationFactory>
{
    private HttpClient CreateClient() => factory.CreateClient(
        new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

    /// <summary>
    /// The one endpoint that is a browser navigation rather than a fetch, and therefore the one
    /// that must not answer a caller with no session with problem-JSON. An administrator following
    /// the approval link after their session expired would otherwise be shown a JSON document in a
    /// tab.
    /// <para>
    /// It sends them to Microsoft's admin-consent screen rather than to TodoWerk's sign-in. The
    /// link has a second caller — somebody whose sign-in has just ended without an approval — and
    /// sending them to sign in would be sending them back to the screen that refused them.
    /// Microsoft asks whoever presses it to sign in, and asks for the one thing this link is about.
    /// </para>
    /// </summary>
    [Fact]
    public async Task TenantConsent_WithoutASession_SendsTheBrowserToMicrosoftRatherThanReturningJson()
    {
        using var client = CreateClient();

        using var response = await client.GetAsync("/auth/tenant-consent", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        var location = response.Headers.Location?.ToString() ?? string.Empty;

        Assert.Contains("/v2.0/adminconsent", location, StringComparison.Ordinal);
        Assert.DoesNotContain("/auth/sign-in", location, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/api/task-lists")]
    [InlineData("/api/me")]
    [InlineData("/api/tenant/overview")]
    public async Task ApiEndpoints_WithoutSession_Return401AsJsonProblemNotARedirect(string path)
    {
        using var client = CreateClient();

        using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsPayload>(
            TestContext.Current.CancellationToken);

        Assert.NotNull(problem);
        Assert.Equal(401, problem.Status);
    }

    [Fact]
    public async Task SignIn_RedirectsToTheIdentityProvider()
    {
        using var client = CreateClient();

        using var response = await client.GetAsync("/auth/sign-in", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        Assert.Contains(
            "login.microsoftonline.com",
            response.Headers.Location.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task SignIn_WithAnAbsoluteReturnUrl_DoesNotBecomeAnOpenRedirect()
    {
        using var client = CreateClient();

        using var response = await client.GetAsync(
            "/auth/sign-in?returnUrl=https://evil.example.com/",
            TestContext.Current.CancellationToken);

        var location = response.Headers.Location!.ToString();

        Assert.DoesNotContain("evil.example.com", location, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The decision table itself, because the functional round trip cannot show it: the return
    /// URL rides encrypted inside the OpenID Connect state. The two backslash rows are the ones
    /// that turn a naive "no double slash" check into an open redirect — the WHATWG parser reads
    /// <c>/\host</c> exactly like <c>//host</c>.
    /// </summary>
    [Theory]
    [InlineData("/", true)]
    [InlineData("/hashtags", true)]
    [InlineData("/hashtags?page=2", true)]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("//evil.example", false)]
    [InlineData("/\\evil.example", false)]
    [InlineData("\\\\evil.example", false)]
    [InlineData("https://evil.example/", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("/legit\r\nSet-Cookie:war=lost", false)]
    [InlineData("~/hashtags", false)]
    public void ReturnUrls_AreOnlyAcceptedWhenLocal(string? candidate, bool accepted) =>
        Assert.Equal(accepted, TodoWerk.Web.Endpoints.AuthEndpoints.IsLocalReturnUrl(candidate));

    /// <summary>
    /// A hostile return URL must not break sign-in either — the challenge proceeds, aimed at "/".
    /// </summary>
    [Theory]
    [InlineData("/auth/sign-in?returnUrl=%2F%5Cevil.example")]
    [InlineData("/auth/sign-in?returnUrl=%2F%2Fevil.example")]
    public async Task SignIn_WithAHostileReturnUrl_StillChallengesNormally(string path)
    {
        using var client = CreateClient();

        using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains(
            "login.microsoftonline.com",
            response.Headers.Location!.ToString(),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The invariant behind every antiforgery test: no endpoint that mutates may exist without
    /// the validation filter, or without a written reason why the filter is not what protects it.
    /// Read off the endpoint metadata, so the first forgotten <c>ValidateAntiforgery()</c> on a new
    /// endpoint fails here rather than in production.
    /// </summary>
    [Fact]
    public void EveryMutatingEndpoint_ValidatesTheAntiforgeryTokenOrSaysWhyItCannot()
    {
        var endpoints = MutatingEndpoints();

        Assert.NotEmpty(endpoints);
        Assert.All(
            endpoints,
            endpoint => Assert.True(
                endpoint.Metadata.GetMetadata<TodoWerk.Web.Security.AntiforgeryProtectedMetadata>() is not null
                || endpoint.Metadata.GetMetadata<TodoWerk.Web.Security.AntiforgeryNotApplicableMetadata>() is not null,
                $"'{endpoint.DisplayName}' mutates but neither validates the antiforgery token nor "
                + "says why it does not."));
    }

    /// <summary>
    /// And the exemptions are exactly one, by name. Without this the escape hatch above is a way
    /// to opt any endpoint out of CSRF protection with a sentence — which is worth having for the
    /// one endpoint that accepts no ambient credential, and worth being unable to do quietly.
    /// </summary>
    [Fact]
    public void OnlyTheTeamsExchange_IsExemptFromTheAntiforgeryPair()
    {
        var exempt = MutatingEndpoints()
            .Where(endpoint =>
                endpoint.Metadata.GetMetadata<TodoWerk.Web.Security.AntiforgeryNotApplicableMetadata>() is not null)
            .Select(endpoint => endpoint.DisplayName)
            .ToList();

        Assert.Equal(["HTTP: POST /api/teams/session"], exempt);
    }

    private List<Microsoft.AspNetCore.Http.Endpoint> AllEndpoints() =>
        [.. factory.Services.GetServices<Microsoft.AspNetCore.Routing.EndpointDataSource>()
            .SelectMany(source => source.Endpoints)];

    private List<Microsoft.AspNetCore.Http.Endpoint> MutatingEndpoints() =>
        [.. AllEndpoints()
            .Where(endpoint => endpoint.Metadata
                .GetMetadata<Microsoft.AspNetCore.Routing.HttpMethodMetadata>()?
                .HttpMethods.Any(method => method is not ("GET" or "HEAD")) == true)];

    /// <summary>
    /// Every endpoint says whether it needs a session, one way or the other. Nothing here is about
    /// which answer is right — only that somebody gave one.
    /// <para>
    /// This is the precondition the licence gate rests on, and it is invisible from the gate's own
    /// side. <c>LicenceGateMiddleware</c> stands in front of everything carrying
    /// <see cref="Microsoft.AspNetCore.Authorization.IAuthorizeData"/>, and there is no fallback
    /// policy — so an endpoint that simply forgot <c>RequireAuthorization()</c> is not gated *and*
    /// not authenticated, while carrying no <c>AllowUnlicensedMetadata</c> for
    /// <c>LicensingTests.OnlyFourEndpointsStayOpenToSomebodyWithoutALicence</c> to notice. That
    /// list can only see the opt-outs somebody wrote down;
    /// this sees the one that was never written at all.
    /// </para>
    /// <para>
    /// It is only half-silent, which is why it is worth a cheap test rather than a redesign: such
    /// an endpoint reading <c>ICurrentUser</c> misbehaves visibly for a signed-out caller. The
    /// other half is the quiet one — it keeps answering somebody the product has shut out.
    /// </para>
    /// </summary>
    [Fact]
    public void EveryEndpoint_SaysWhetherItNeedsASession()
    {
        var endpoints = AllEndpoints();

        // Guarded, because a filter that matched nothing would pass this loudly and mean nothing.
        Assert.NotEmpty(endpoints);
        Assert.All(
            endpoints,
            endpoint => Assert.True(
                endpoint.Metadata.GetMetadata<Microsoft.AspNetCore.Authorization.IAuthorizeData>() is not null
                || endpoint.Metadata.GetMetadata<Microsoft.AspNetCore.Authorization.IAllowAnonymous>() is not null,
                $"'{endpoint.DisplayName}' neither requires authorization nor allows anonymous "
                + "callers, so it is outside the licence gate as well as outside the sign-in."));
    }

    [Fact]
    public async Task SignOut_WithoutASession_Returns401RatherThanSigningAnyoneOut()
    {
        using var client = CreateClient();

        using var response = await client.PostAsync(
            "/auth/sign-out",
            content: null,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Health_IsAnonymous()
    {
        using var client = CreateClient();

        using var response = await client.GetAsync("/health", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// The SPA document is served by the static-file middleware, which short-circuits the
    /// pipeline. It still has to carry the antiforgery token, or the first mutating request
    /// after a cold load has nothing to send.
    /// </summary>
    [Theory]
    [InlineData("/")]
    [InlineData("/health")]
    public async Task SafeRequests_PublishTheAntiforgeryTokenToTheClient(string path)
    {
        using var client = CreateClient();

        using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);

        var setCookies = response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.ToArray()
            : [];

        Assert.Contains(setCookies, cookie => cookie.StartsWith("XSRF-TOKEN=", StringComparison.Ordinal));
    }

    /// <summary>
    /// Microsoft.Identity.Web reads the options it needs for token acquisition from the effective
    /// authentication scheme, and those options are bound to the OpenID Connect scheme. Making the
    /// cookie the default — the obvious way to populate <c>HttpContext.User</c> everywhere — makes
    /// it infer <c>Cookies</c> instead and fail every Graph call with IDW10503. Nothing else in the
    /// suite notices, because no test here calls Graph.
    /// </summary>
    [Fact]
    public void DefaultScheme_StaysOpenIdConnect_SoTokenAcquisitionFindsItsOptions()
    {
        var options = factory.Services
            .GetRequiredService<IOptions<AuthenticationOptions>>()
            .Value;

        var effective = options.DefaultAuthenticateScheme ?? options.DefaultScheme;

        Assert.Equal(OpenIdConnectDefaults.AuthenticationScheme, effective);
    }

    private sealed record ProblemDetailsPayload(string? Title, int? Status, string? Detail);
}
