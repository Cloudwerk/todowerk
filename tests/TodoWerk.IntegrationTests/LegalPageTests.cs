using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace TodoWerk.IntegrationTests;

/// <summary>
/// The terms of use and the privacy notice, as a stranger sees them. Three places have to point at
/// these two addresses — the Entra ID app registration's Branding &amp; properties, the Teams app
/// manifest's <c>developer</c> block and a marketplace listing — and every one of those
/// audiences arrives without a session, so "anonymous" is the requirement rather than a detail.
/// </summary>
public sealed class LegalPageTests
{
    private const string Terms = "/legal/terms";

    private const string Privacy = "/legal/privacy";

    [Theory]
    [InlineData(Terms)]
    [InlineData(Privacy)]
    public async Task AreServedToSomebodyWhoHasNotSignedIn(string path)
    {
        using var factory = new TodoWerkWebApplicationFactory().WithoutBackgroundWorkers();
        using var client = Anonymous(factory);

        using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Null(response.Headers.Location);
    }

    /// <summary>
    /// The whole reason the documents are rendered rather than hand-written twice: what is served
    /// is <c>TERMS.md</c> and <c>PRIVACY.md</c> themselves. A heading each document owns is enough
    /// to say which one came back — asserting on more would be asserting on prose.
    /// </summary>
    [Theory]
    [InlineData(Terms, "Terms of use")]
    [InlineData(Privacy, "Privacy notice")]
    public async Task AreTheDocumentsInTheRepositoryRatherThanACopyOfThem(string path, string heading)
    {
        using var factory = new TodoWerkWebApplicationFactory().WithoutBackgroundWorkers();

        var page = await Fetch(factory, path);

        Assert.Contains($"<h1>{heading}</h1>", page, StringComparison.Ordinal);
        Assert.Contains($"<title>{heading} — TodoWerk</title>", page, StringComparison.Ordinal);
    }

    /// <summary>
    /// TodoWerk is free software, so the same terms are served by CloudWerk's deployment and by a
    /// Self-Host — and the one thing that must differ between them is who the person signing in is
    /// agreeing with.
    /// </summary>
    [Fact]
    public async Task TermsNameTheOperatorTheDeploymentIsConfiguredWith()
    {
        using var factory = new TodoWerkWebApplicationFactory()
            .WithoutBackgroundWorkers()
            .WithConfigurationOverride("Legal:Operator", "Contoso Ltd")
            .WithConfigurationOverride("Legal:OperatorContact", "legal@contoso.example")
            .WithConfigurationOverride("Legal:GoverningLaw", "England and Wales");

        var page = await Fetch(factory, Terms);

        Assert.Contains("Contoso Ltd", page, StringComparison.Ordinal);
        Assert.Contains("href=\"mailto:legal@contoso.example\"", page, StringComparison.Ordinal);
        Assert.Contains("England and Wales", page, StringComparison.Ordinal);
    }

    /// <summary>
    /// Retention is configuration, and both documents promise the number. A deployment that
    /// shortens the dormancy window has to have its notice and its terms say the shorter number,
    /// or the page a regulator reads promises something the sweep does not keep. The same tie the
    /// Administrator's Guide has, for the same reason.
    /// </summary>
    [Theory]
    [InlineData(Terms, "anything left untouched for 200 days is destroyed")]
    [InlineData(Privacy, "200 days without a sign-in")]
    public async Task StateThisInstallationsOwnDormancyWindow(string path, string sentence)
    {
        using var factory = new TodoWerkWebApplicationFactory()
            .WithoutBackgroundWorkers()
            .WithConfigurationOverride("Onboarding:DormancyWindow", "200.00:00:00");

        var page = await Fetch(factory, path);

        Assert.Contains(sentence, page, StringComparison.Ordinal);
        Assert.DoesNotContain("{{", page, StringComparison.Ordinal);
    }

    /// <summary>
    /// An installation that has configured nobody still serves a document that reads as English.
    /// The alternative — a page of braces, or a 404 behind a link a consent dialog is already
    /// showing — is worse than a document that admits it names no one.
    /// </summary>
    [Theory]
    [InlineData(Terms)]
    [InlineData(Privacy)]
    public async Task LeaveNoPlaceholderUnsubstituted(string path)
    {
        using var factory = new TodoWerkWebApplicationFactory().WithoutBackgroundWorkers();

        var page = await Fetch(factory, path);

        Assert.DoesNotContain("{{", page, StringComparison.Ordinal);
        Assert.DoesNotContain("}}", page, StringComparison.Ordinal);
    }

    /// <summary>
    /// A relative link is relative to the repository the documents live in, not to the address they
    /// are served on. Left alone, every one of them would answer with the SPA — which is a broken
    /// legal document that returns 200, the kind nobody notices.
    /// </summary>
    [Fact]
    public async Task RelativeLinksResolveToTheOtherPageOrToTheRepository()
    {
        using var factory = new TodoWerkWebApplicationFactory().WithoutBackgroundWorkers();

        var page = await Fetch(factory, Terms);

        Assert.Contains($"href=\"{Privacy}\"", page, StringComparison.Ordinal);
        Assert.Contains(
            "href=\"https://github.com/Cloudwerk/todowerk/blob/main/SECURITY.md\"",
            page,
            StringComparison.Ordinal);
        Assert.DoesNotContain("href=\"SECURITY.md\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("href=\"docs/", page, StringComparison.Ordinal);
    }

    /// <summary>
    /// The privacy notice is mostly tables — what is stored, and for how long. A renderer without
    /// the tables extension turns them into paragraphs of pipe characters, which still returns 200.
    /// </summary>
    [Fact]
    public async Task ThePrivacyNoticesTablesAreRenderedAsTables()
    {
        using var factory = new TodoWerkWebApplicationFactory().WithoutBackgroundWorkers();

        var page = await Fetch(factory, Privacy);

        Assert.Contains("<table>", page, StringComparison.Ordinal);
        Assert.DoesNotContain("| What |", page, StringComparison.Ordinal);
    }

    /// <summary>
    /// A fallback is invisible — the terms still render and still read as English — so a deployment
    /// that never set these would never notice. Every unset value is named once at
    /// startup instead, including on a deployment that filled in some of them: each falls back on
    /// its own, and a half-configured one still serves wording that names nobody.
    /// </summary>
    [Fact]
    public async Task EveryUnsetLegalSettingIsReportedAtStartup()
    {
        var logs = await StartDeployedAndFetchTerms();

        Assert.Contains(
            logs.Records,
            record => record.Level == LogLevel.Warning
                && record.Message.Contains(
                    "Legal:Operator, Legal:OperatorContact, Legal:GoverningLaw not set",
                    StringComparison.Ordinal));
    }

    [Fact]
    public async Task ADeploymentThatNamedOnlyItselfIsStillToldAboutTheRest()
    {
        var logs = await StartDeployedAndFetchTerms(factory =>
            factory.WithConfigurationOverride("Legal:Operator", "Contoso Ltd"));

        Assert.Contains(
            logs.Records,
            record => record.Level == LogLevel.Warning
                && record.Message.Contains(
                    "Legal:OperatorContact, Legal:GoverningLaw not set",
                    StringComparison.Ordinal));
    }

    /// <summary>
    /// Staging rather than Development, because that warning is for deployments and a developer
    /// trying TodoWerk out is not one — a host that never leaves Development would report this
    /// green by never reaching the code it is about.
    /// </summary>
    private static async Task<RecordedLogs> StartDeployedAndFetchTerms(
        Action<TodoWerkWebApplicationFactory>? configure = null)
    {
        var logs = new RecordedLogs();
        using var deployed = new DeployedLikeConfiguration();

        using var factory = new TodoWerkWebApplicationFactory()
            .WithEnvironment(Environments.Staging)
            .WithoutBackgroundWorkers()
            .WithLoggerProvider(logs);

        foreach (var (key, value) in deployed.Settings)
        {
            factory.WithStartupSetting(key, value);
        }

        configure?.Invoke(factory);

        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });

        using var response = await client.GetAsync(Terms, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();

        return logs;
    }

    /// <summary>
    /// Somebody reading the privacy notice has not signed in, has not decided to, and may never.
    /// A page that sets cookies on them while they read about what is stored about them is the one
    /// page in the application that must not.
    /// </summary>
    [Theory]
    [InlineData(Terms)]
    [InlineData(Privacy)]
    public async Task SetNoCookiesOnSomebodyWhoIsOnlyReading(string path)
    {
        using var factory = new TodoWerkWebApplicationFactory().WithoutBackgroundWorkers();
        using var client = Anonymous(factory);

        using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);

        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    private static HttpClient Anonymous(TodoWerkWebApplicationFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static async Task<string> Fetch(TodoWerkWebApplicationFactory factory, string path)
    {
        using var client = Anonymous(factory);
        using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
    }
}
