using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TodoWerk.Application.Licensing;
using TodoWerk.Infrastructure.Licensing;
using Xunit;

namespace TodoWerk.IntegrationTests;

/// <summary>
/// What the shipped <c>appsettings.json</c> actually means, read the way the running application
/// reads it.
/// <para>
/// A <c>TimeSpan</c> written <c>"24:00:00"</c> parses as twenty-four <em>days</em>, not hours:
/// .NET reads a leading component above 23 as <c>d:hh:mm</c>. Startup validation only asks for a
/// positive value, so nothing but a test that reads the file would notice.
/// </para>
/// </summary>
public sealed class LicensingConfigurationTests
{
    /// <summary>
    /// Every value the running application ends up with must equal the code default beside it.
    /// The four timing settings are no longer restated in the file at all — the property
    /// initialiser is the single place each one is written, so the two cannot disagree. This
    /// stays as the guard for the day somebody puts one back: a disagreement is invisible until
    /// somebody's licence behaves unlike its documentation.
    /// </summary>
    [Fact]
    public void TheShippedFileNeverDisagreesWithTheDefaults()
    {
        var shipped = Shipped();
        var defaults = new LicensingOptions();

        Assert.Equal(defaults.FailOpenWindow, shipped.FailOpenWindow);
        Assert.Equal(defaults.TrialEndingSoon, shipped.TrialEndingSoon);
        Assert.Equal(defaults.RequestTimeout, shipped.RequestTimeout);
        Assert.Equal(defaults.MaximumCacheLifetime, shipped.MaximumCacheLifetime);
    }

    /// <summary>
    /// And the window itself, spelled out. The assertion above would still pass if both the file
    /// and the default were changed to something absurd together; this one says what the number is
    /// meant to be: twenty-four hours, a <c>TimeSpan</c> that <c>d.hh:mm:ss</c> writes as
    /// <c>1.00:00:00</c>.
    /// </summary>
    [Fact]
    public void TheFailOpenWindowIsTwentyFourHoursAndNotTwentyFourDays()
    {
        Assert.Equal(TimeSpan.FromHours(24), Shipped().FailOpenWindow);
        Assert.Equal(TimeSpan.FromHours(24), new LicensingOptions().FailOpenWindow);
    }

    /// <summary>
    /// The shipped file leaves the three portal settings empty, which is what makes an installation
    /// that changes nothing a Self-Host: no client to ManagementPortal, no outbound call, no banner.
    /// </summary>
    [Fact]
    public void TheShippedFileIsASelfHost()
    {
        var shipped = Shipped();

        Assert.True(shipped.IsAbsent);
        Assert.False(shipped.IsConfigured);
    }

    /// <summary>
    /// An application key that picked up a trailing newline — the usual shape of a Docker secret
    /// read carelessly — cannot be sent as a header. Caught at startup, where the message names the
    /// setting, rather than as one failed resolution per request.
    /// </summary>
    [Fact]
    public void AnApplicationKeyWithATrailingNewlineRefusesToStart()
    {
        var services = new ServiceCollection();

        services.AddTodoWerkLicensing(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Licensing:PortalHost"] = "https://portal.todowerk.test",
                ["Licensing:ApplicationKey"] = "a-good-key\n",
                ["Licensing:SolutionSlug"] = "todowerk",
            })
            .Build());

        using var provider = services.BuildServiceProvider();

        var exception = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<LicensingOptions>>().Value);

        Assert.Contains("ApplicationKey", string.Join(' ', exception.Failures), StringComparison.Ordinal);
    }

    /// <summary>
    /// The file the application really loads, bound through the same options type. Its content root
    /// is the web project, exactly as <see cref="TodoWerkWebApplicationFactory"/> leaves it.
    /// </summary>
    private static LicensingOptions Shipped()
    {
        using var factory = new TodoWerkWebApplicationFactory().WithoutBackgroundWorkers();

        using var client = factory.CreateClient();

        return factory.Services.GetRequiredService<IOptions<LicensingOptions>>().Value;
    }
}
