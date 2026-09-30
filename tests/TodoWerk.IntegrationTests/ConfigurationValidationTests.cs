using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using TodoWerk.Application;
using TodoWerk.Infrastructure;
using TodoWerk.Infrastructure.Authentication;
using TodoWerk.Infrastructure.Indexing;
using TodoWerk.Application.Licensing;
using TodoWerk.Infrastructure.Persistence;
using Xunit;

namespace TodoWerk.IntegrationTests;

/// <summary>
/// A misconfigured deployment must fail at startup, not on every request. Without
/// <c>ValidateOnStart</c> these checks never run — nothing resolves
/// <c>IOptions&lt;EntraIdOptions&gt;</c>, because Microsoft.Identity.Web reads the
/// configuration section directly, and nothing resolves the database options until the first
/// query — and the app starts clean, then answers every request (health checks included) with
/// an opaque library error.
/// </summary>
public sealed class ConfigurationValidationTests
{
    [Theory]
    [InlineData("EntraId:ClientId", "")]
    [InlineData("EntraId:ClientSecret", "")]
    [InlineData("EntraId:TenantId", "common")]
    [InlineData("ConnectionStrings:TodoWerk", "")]
    // Present but not a connection string, which is what a mistyped setup step produces.
    // Validating only presence would let the app announce its ports and fail seconds later, in a
    // background worker, on every tick.
    [InlineData("ConnectionStrings:TodoWerk", "Delegated")]
    // Optional, so its empty spelling passes — but present and not an absolute URI is a typo whose
    // only symptom would be a Teams tab that signs nobody in.
    [InlineData("EntraId:ApplicationIdUri", "   ")]
    [InlineData("EntraId:ApplicationIdUri", "todowerk.example.com")]
    // Equal to the default SyncInterval. At or below it, everybody is idle before their next sync
    // is due, and the scheduled sync is off for the whole deployment with nothing in the log.
    [InlineData("Indexing:IdleAfter", "00:30:00")]
    public void Startup_WithInvalidConfiguration_FailsFastWithAnActionableMessage(
        string key,
        string value)
    {
        using var services = ApplicationServicesWith(key, value);
        var validator = services.GetRequiredService<IStartupValidator>();

        var exception = Assert.Throws<OptionsValidationException>(validator.Validate);

        Assert.Contains(key, string.Join(' ', exception.Failures), StringComparison.Ordinal);
    }

    /// <summary>
    /// And the other half of it, which matters as much: the connection string this factory
    /// supplies is well-formed and points at a server that does not exist. Startup validates the
    /// shape of the setting and stops there — whether the database has to answer before the app
    /// will serve is <c>DatabaseReadinessCheck</c>'s decision, and it deliberately says no. If this
    /// test ever starts needing a reachable database, that decision has been reversed by accident.
    /// <para>
    /// The three assertions after it are a partial join to the theory above, which builds its own
    /// container: they pin that the rules exist in the host the application really boots, so the
    /// theory is at least checking registrations that are present in the deployed app. They are
    /// not the whole join, and it is worth being exact about the part that is missing — nothing
    /// here would notice a <c>PostConfigure</c> in <c>Program</c> that quietly filled an empty
    /// setting back in, because these rules would still be registered and the theory's container
    /// would never see that call. A bare <c>IStartupValidator</c> assertion would be weaker
    /// still: several libraries in this container register one.
    /// </para>
    /// </summary>
    [Fact]
    public void Startup_WithValidConfiguration_Succeeds()
    {
        using var factory = new TodoWerkWebApplicationFactory();

        using var client = factory.CreateClient();

        Assert.NotNull(client);
        Assert.NotEmpty(factory.Services.GetServices<IValidateOptions<EntraIdOptions>>());
        Assert.NotEmpty(factory.Services.GetServices<IValidateOptions<DatabaseOptions>>());
        Assert.NotEmpty(factory.Services.GetServices<IValidateOptions<IndexingOptions>>());
    }

    /// <summary>
    /// Complete or absent, never in between. A half-filled <c>Licensing</c> section is the shape a
    /// Hosted Service takes when a secret failed to arrive, and starting anyway means a deployment
    /// that licenses everybody until somebody notices.
    /// <para>
    /// Rows of their own rather than of the theory above, because the failure this asserts on names
    /// the *section* rather than the key: the three settings are present-or-absent as one thing, so
    /// there is no single key to point at.
    /// </para>
    /// <para>
    /// Asked of the validator rather than by booting a host to watch it refuse to start: a boot
    /// that is expected to fail is the race this file's helper documents, and CONTRIBUTING
    /// § Testing rules it out.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("Licensing:PortalHost", FakeManagementPortal.BaseAddress)]
    [InlineData("Licensing:ApplicationKey", FakeManagementPortal.ApplicationKey)]
    [InlineData("Licensing:SolutionSlug", FakeManagementPortal.SolutionSlug)]
    public void AHalfFilledLicensingSectionRefusesToStart(string key, string value)
    {
        using var services = ApplicationServicesWith(key, value);
        var validator = services.GetRequiredService<IStartupValidator>();

        var exception = Assert.Throws<OptionsValidationException>(validator.Validate);

        Assert.Contains(
            LicensingOptions.SectionName,
            string.Join(' ', exception.Failures),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A Hosted Service's three settings, with the host missing its scheme — what copying a
    /// hostname out of a browser produces, and something the first-party paths cannot be appended
    /// to. The whole section has to be present before the one wrong value in it is the thing that
    /// fails, which is why this sets three settings where everything else here sets one.
    /// </summary>
    [Fact]
    public void APortalHostThatIsNotAnAbsoluteUrlRefusesToStart()
    {
        using var services = ApplicationServicesWith(
            ("Licensing:PortalHost", "portal.todowerk.test"),
            ("Licensing:ApplicationKey", FakeManagementPortal.ApplicationKey),
            ("Licensing:SolutionSlug", FakeManagementPortal.SolutionSlug));
        var validator = services.GetRequiredService<IStartupValidator>();

        var exception = Assert.Throws<OptionsValidationException>(validator.Validate);

        Assert.Contains("PortalHost", string.Join(' ', exception.Failures), StringComparison.Ordinal);
    }

    /// <summary>
    /// The registrations a started host would validate, with one setting replaced — asked
    /// directly, rather than by booting the application and watching it fail.
    /// <para>
    /// Under <c>WebApplicationFactory</c> the application's entry point runs on a thread of its
    /// own, and startup validation runs there, inside <c>await app.RunAsync()</c> — after the built
    /// host has already been handed to the test thread. <c>RunAsync</c> disposes the host in its
    /// <c>finally</c>, so a failed start is a race between that disposal and the test thread
    /// reaching into the same container to wait for the host to start. What <c>CreateClient</c>
    /// raises is then decided by that race rather than by the configuration: sometimes the
    /// validation failure, sometimes an <c>ObjectDisposedException</c> from the framework's own
    /// wait. An assertion whose answer arrives by thread scheduling cannot be made reliable, only
    /// avoided.
    /// </para>
    /// <para>
    /// <see cref="IStartupValidator"/> is what <c>Host.StartAsync</c> calls, before the first
    /// hosted service and before anything is listening, and <c>ValidateOnStart</c> is what puts
    /// these options in front of it — drop that one call and the cases below stop throwing. So
    /// this asks the question the host asks, and answers it on one thread.
    /// </para>
    /// </summary>
    private static ServiceProvider ApplicationServicesWith(string key, string value) =>
        ApplicationServicesWith((key, value));

    /// <summary>
    /// The same, for a case that needs a whole section present before one value inside it is the
    /// thing that fails.
    /// </summary>
    private static ServiceProvider ApplicationServicesWith(params (string Key, string Value)[] replaced)
    {
        var settings = TodoWerkWebApplicationFactory.BaseConfiguration();

        foreach (var (key, value) in replaced)
        {
            settings[key] = value;
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        var services = new ServiceCollection();
        services.AddApplication();
        services.AddInfrastructure(configuration, DevelopmentEnvironment);

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Development, because that is the only environment in which the key-ring settings the
    /// factory leaves out are optional — outside it, data protection refuses to be registered at
    /// all and this fixture would fail before reaching the setting under test.
    /// </summary>
    private static readonly IHostEnvironment DevelopmentEnvironment = new TestHostEnvironment();

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;

        public string ApplicationName { get; set; } = "TodoWerk.Web";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
