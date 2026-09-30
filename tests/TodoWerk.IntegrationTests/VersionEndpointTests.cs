using System.Net;
using System.Reflection;
using Microsoft.AspNetCore.Mvc.Testing;
using TodoWerk.Web.Diagnostics;
using Xunit;

namespace TodoWerk.IntegrationTests;

/// <summary>
/// The endpoint that says which build is answering. It exists because a deployment pulls its image
/// by tag and a tag can be moved: without this, "which version is running in production" is only
/// answerable by matching an image digest by hand against a registry listing.
/// </summary>
public sealed class VersionEndpointTests
{
    private const string Version = "/version";

    /// <summary>
    /// Everyone who needs this arrives without a session — an operator checking that a redeploy
    /// took, a smoke test, somebody filing a bug against a deployment they do not administer. An
    /// endpoint behind sign-in would answer none of them.
    /// </summary>
    [Fact]
    public async Task IsAnsweredToSomebodyWhoHasNotSignedIn()
    {
        using var factory = new TodoWerkWebApplicationFactory().WithoutBackgroundWorkers();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });

        using var response = await client.GetAsync(Version, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Null(response.Headers.Location);
    }

    /// <summary>
    /// The commit is the whole point — a payload that reported <c>1.0.0</c> and nothing else would
    /// identify every build ever made equally well. The assembly is asked the same question here
    /// rather than a literal being asserted, because the answer changes with every commit; what is
    /// pinned is that the endpoint reports what the compiler stamped rather than something of its
    /// own invention.
    /// </summary>
    [Fact]
    public async Task ReportsTheVersionAndCommitStampedOnTheApplicationAssembly()
    {
        var expected = BuildVersion.Parse(
            typeof(Program).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion);

        using var factory = new TodoWerkWebApplicationFactory().WithoutBackgroundWorkers();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });

        var payload = await client.GetStringAsync(Version, TestContext.Current.CancellationToken);

        Assert.Contains($"\"version\":\"{expected.Version}\"", payload, StringComparison.Ordinal);
        Assert.Contains(
            expected.Commit is null ? "\"commit\":null" : $"\"commit\":\"{expected.Commit}\"",
            payload,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The shipped configuration leaves the <c>Licensing</c> section empty, and the endpoint says so
    /// in the word the runbooks use, so an operator can check with one anonymous request whether
    /// licensing is configured.
    /// </summary>
    [Fact]
    public async Task SaysTheLicensingSectionIsAbsentWhenItIs()
    {
        using var factory = new TodoWerkWebApplicationFactory().WithoutBackgroundWorkers();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });

        var payload = await client.GetStringAsync(Version, TestContext.Current.CancellationToken);

        Assert.Contains("\"licensing\":\"absent\"", payload, StringComparison.Ordinal);
    }

    /// <summary>
    /// With the section configured the deployment asks ManagementPortal about every signed-in
    /// person, and the endpoint says that and nothing else about it: not the host, not the slug,
    /// and never the key. Nor does answering cost a portal call — <c>/version</c> is anonymous, and
    /// there is nobody to resolve a Licence for.
    /// </summary>
    [Fact]
    public async Task SaysTheDeploymentAsksThePortalWhenTheSectionIsConfiguredAndNamesNothingAboutIt()
    {
        using var portal = new FakeManagementPortal(_ =>
            FakeManagementPortal.PortalAnswer.Ok(FakeManagementPortal.Valid("tenant", "2027-03-14T00:00:00Z")));
        using var factory = new TodoWerkWebApplicationFactory()
            .WithoutBackgroundWorkers()
            .WithStartupSetting("Licensing:PortalHost", FakeManagementPortal.BaseAddress)
            .WithStartupSetting("Licensing:ApplicationKey", FakeManagementPortal.ApplicationKey)
            .WithStartupSetting("Licensing:SolutionSlug", FakeManagementPortal.SolutionSlug)
            .WithSharedOutboundHttpHandler(portal);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });

        var payload = await client.GetStringAsync(Version, TestContext.Current.CancellationToken);

        Assert.Contains("\"licensing\":\"portal\"", payload, StringComparison.Ordinal);
        Assert.DoesNotContain(FakeManagementPortal.Host, payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(FakeManagementPortal.ApplicationKey, payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(FakeManagementPortal.SolutionSlug, payload, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(portal.Calls);
    }

    /// <summary>
    /// A build made outside a git working tree has no revision appended, and one made without the
    /// attribute at all has nothing to read. Neither is a reason to fail a request: "this build
    /// does not know which build it is" is the answer an operator needs, and it is a different
    /// answer from a 500.
    /// </summary>
    [Theory]
    [InlineData("1.0.0+083082fc41fe91c3c31d93b75a896834565eb59f", "1.0.0", "083082fc41fe91c3c31d93b75a896834565eb59f")]
    [InlineData("1.0.0", "1.0.0", null)]
    [InlineData("1.0.0+", "1.0.0", null)]
    [InlineData(null, BuildVersion.Unknown, null)]
    [InlineData("   ", BuildVersion.Unknown, null)]
    public void SeparatesTheSourceRevisionFromTheVersionOrSaysItHasNone(
        string? informationalVersion,
        string version,
        string? commit)
    {
        var parsed = BuildVersion.Parse(informationalVersion);

        Assert.Equal(version, parsed.Version);
        Assert.Equal(commit, parsed.Commit);
    }
}
