using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using TodoWerk.Infrastructure.Authentication;
using Xunit;

namespace TodoWerk.UnitTests.Authentication;

/// <summary>
/// The one place two cookie attributes are decided, and the one environment where they are decided
/// differently.
/// <para>
/// The Development case is not a convenience: Development also serves plain HTTP, and every
/// browser silently drops a <c>SameSite=None</c> cookie that is not <c>Secure</c>. Emitting the
/// deployed pair there would not fail — it would sign a developer out on every request, with
/// nothing on screen to say why.
/// </para>
/// </summary>
public sealed class SecureCookiePolicyTests
{
    [Fact]
    public void OutsideDevelopment_CookiesAreAlwaysSecureAndSameSiteNone()
    {
        var environment = For(Environments.Production);

        Assert.Equal(CookieSecurePolicy.Always, SecureCookiePolicy.For(environment));
        Assert.Equal(SameSiteMode.None, SecureCookiePolicy.SameSiteFor(environment));
    }

    [Fact]
    public void InDevelopment_CookiesFollowTheRequestSchemeAndStayLax()
    {
        var environment = For(Environments.Development);

        Assert.Equal(CookieSecurePolicy.SameAsRequest, SecureCookiePolicy.For(environment));
        Assert.Equal(SameSiteMode.Lax, SecureCookiePolicy.SameSiteFor(environment));
    }

    /// <summary>
    /// Anything that is not the literal <c>Development</c> is deployed as far as these two are
    /// concerned — a staging slot is on the internet like any other.
    /// </summary>
    [Theory]
    [InlineData("Staging")]
    [InlineData("Demo")]
    public void AnyOtherEnvironment_IsTreatedAsDeployed(string environmentName)
    {
        var environment = For(environmentName);

        Assert.Equal(CookieSecurePolicy.Always, SecureCookiePolicy.For(environment));
        Assert.Equal(SameSiteMode.None, SecureCookiePolicy.SameSiteFor(environment));
    }

    private static HostingEnvironment For(string environmentName) => new()
    {
        EnvironmentName = environmentName,
    };

    private sealed class HostingEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;

        public string ApplicationName { get; set; } = "TodoWerk.UnitTests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
