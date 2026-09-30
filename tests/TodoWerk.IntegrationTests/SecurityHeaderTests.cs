using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace TodoWerk.IntegrationTests;

/// <summary>
/// What TodoWerk sends a browser once it is deployed: the framing headers, which branch on the
/// Teams tab's path and nowhere else, and the three cookies, which have to survive a third-party
/// context or the tab holds no session at all
/// ([ADR-0010](../../docs/adr/0010-teams-tab-session-and-framing.md)).
/// <para>
/// Everything here needs a host that is <em>not</em> Development, because both behaviours are
/// switched off there: the content security policy is not sent at all, and the cookies keep
/// <c>Lax</c> so that a developer serving plain HTTP is not silently signed out. A Development
/// host would report every assertion below green by never reaching the code they are about.
/// </para>
/// </summary>
public sealed class SecurityHeaderTests : IDisposable
{
    private readonly DeployedLikeConfiguration _deployed = new();

    private readonly TodoWerkWebApplicationFactory _factory;

    public SecurityHeaderTests()
    {
        _factory = new TodoWerkWebApplicationFactory()
            .WithEnvironment(Environments.Staging)
            .WithoutBackgroundWorkers();

        foreach (var (key, value) in _deployed.Settings)
        {
            // At startup rather than merely in configuration: outside Development the host reads
            // these while it is registering services and refuses to start without them.
            _factory.WithStartupSetting(key, value);
        }
    }

    /// <summary>
    /// Over HTTPS, because a deployed host redirects plain HTTP before any of these headers are
    /// written — and because <c>Secure</c> cookies are the subject.
    /// </summary>
    private HttpClient CreateClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        BaseAddress = new Uri("https://localhost"),
    });

    [Fact]
    public async Task TheTabsDocument_NamesTheTeamsHostsAndSendsNoXFrameOptions()
    {
        using var client = CreateClient();

        using var response = await client.GetAsync("/teams", TestContext.Current.CancellationToken);

        var policy = Assert.Single(response.Headers.GetValues("Content-Security-Policy"));

        // Microsoft's documented list, whole: every ancestor frame is checked, not only the one in
        // the address bar, so Outlook on the web's origins are needed too (ADR-0010).
        Assert.Contains(
            "frame-ancestors 'self' https://teams.microsoft.com https://*.teams.microsoft.com "
            + "https://*.cloud.microsoft https://*.microsoft365.com https://*.office.com "
            + "https://outlook.office.com https://outlook.office365.com "
            + "https://outlook-sdf.office.com https://outlook-sdf.office365.com",
            policy,
            StringComparison.Ordinal);
        Assert.DoesNotContain("frame-ancestors 'none'", policy, StringComparison.Ordinal);

        // Dropped rather than narrowed, because X-Frame-Options has no allow-list: a tab that
        // renders at all is a tab whose document sent none.
        Assert.False(response.Headers.Contains("X-Frame-Options"));
    }

    [Fact]
    public async Task TheConsentPopupsLandingPage_IsFramableToo()
    {
        using var client = CreateClient();

        using var response = await client.GetAsync("/teams/auth-end", TestContext.Current.CancellationToken);

        Assert.Contains(
            "frame-ancestors 'self' https://teams.microsoft.com",
            Assert.Single(response.Headers.GetValues("Content-Security-Policy")),
            StringComparison.Ordinal);
        Assert.False(response.Headers.Contains("X-Frame-Options"));
    }

    /// <summary>
    /// Every other document, and <c>/</c> above all: the Workbench drives renames, merges and
    /// erasure, and is exactly as unframable as it was before the tab existed.
    /// </summary>
    [Theory]
    [InlineData("/")]
    [InlineData("/health")]
    [InlineData("/api/me")]
    [InlineData("/tenant")]
    // Not the Teams surface: a path that merely begins with the same letters is not it.
    [InlineData("/teams-elsewhere")]
    // Nor is anything else under the tab's path. The SPA's fallback answers these with the browser
    // Workbench document, and a prefix match would have handed that document the relaxed framing
    // headers meant for the tab.
    [InlineData("/teams/anything-else")]
    [InlineData("/teams/auth-end/deeper")]
    public async Task EveryOtherDocument_KeepsFrameAncestorsNoneAndDeny(string path)
    {
        using var client = CreateClient();

        using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);

        Assert.Contains(
            "frame-ancestors 'none'",
            Assert.Single(response.Headers.GetValues("Content-Security-Policy")),
            StringComparison.Ordinal);
        Assert.Equal("DENY", Assert.Single(response.Headers.GetValues("X-Frame-Options")));
    }

    /// <summary>
    /// The branch is on the request path and <c>frame-ancestors</c> applies to documents only, so
    /// a script or a stylesheet needs no exception to load inside the frame — and does not get one.
    /// </summary>
    [Fact]
    public async Task StaticAssets_AreUnaffectedByTheBranch()
    {
        using var client = CreateClient();

        using var response = await client.GetAsync(
            "/assets/does-not-exist.js",
            TestContext.Current.CancellationToken);

        Assert.Contains(
            "frame-ancestors 'none'",
            Assert.Single(response.Headers.GetValues("Content-Security-Policy")),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The two cookies a safe request publishes on its own. <c>SameSite=None</c> and <c>Secure</c>,
    /// and — spelled out because its absence is the load-bearing part — not <c>Partitioned</c>: a
    /// partitioned cookie set in the popped-out consent window would land in that window's
    /// partition and be invisible to the tab that opened it.
    /// </summary>
    [Theory]
    [InlineData("XSRF-TOKEN")]
    [InlineData("todowerk.antiforgery")]
    public async Task TheAntiforgeryPair_IsSameSiteNoneSecureAndUnpartitioned(string cookieName)
    {
        using var client = CreateClient();

        using var response = await client.GetAsync("/health", TestContext.Current.CancellationToken);

        var cookie = Assert.Single(
            response.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith($"{cookieName}=", StringComparison.Ordinal));

        Assert.Contains("samesite=none", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("partitioned", cookie, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// And the session cookie, read off the options the cookie handler will use rather than off a
    /// response — writing one means completing a sign-in, and what is being asserted is a setting
    /// rather than a flow.
    /// </summary>
    [Fact]
    public void TheSessionCookie_IsSameSiteNoneAndAlwaysSecure()
    {
        var options = _factory.Services
            .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme);

        Assert.Equal("todowerk.session", options.Cookie.Name);
        Assert.Equal(SameSiteMode.None, options.Cookie.SameSite);
        Assert.Equal(CookieSecurePolicy.Always, options.Cookie.SecurePolicy);
        Assert.True(options.Cookie.HttpOnly);
    }

    public void Dispose()
    {
        _factory.Dispose();
        _deployed.Dispose();
    }
}
