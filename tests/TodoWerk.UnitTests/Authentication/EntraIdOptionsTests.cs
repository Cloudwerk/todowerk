using Microsoft.Extensions.Configuration;
using TodoWerk.Infrastructure.Authentication;
using Xunit;

namespace TodoWerk.UnitTests.Authentication;

/// <summary>
/// The one setting in this section that is optional, and the one shape of it that a deployment
/// without a Teams App Package actually has.
/// <para>
/// <c>appsettings.json</c> ships every setting in the section with an empty value so the shape is
/// discoverable, which for a <see cref="Uri"/> means the binder meets an empty string rather than a
/// missing key. A binder that threw on that would fail the boot of every deployment that has no
/// Teams tab — the majority, and the ones least likely to know what the setting is for.
/// </para>
/// </summary>
public sealed class EntraIdOptionsTests
{
    [Fact]
    public void AnEmptyApplicationIdUri_BindsToNothingRatherThanThrowing()
    {
        var options = Bind(new Dictionary<string, string?>
        {
            [$"{EntraIdOptions.SectionName}:ApplicationIdUri"] = string.Empty,
        });

        Assert.Null(options.ApplicationIdUri);
    }

    /// <summary>
    /// Whitespace does not, and this is where that stops being harmless: the binder turns it into a
    /// <em>relative</em> URI rather than into nothing, which would then be added to the accepted
    /// audiences as the string "   ". Startup validation is what refuses it, and this test is here
    /// so the reason that rule exists is written down next to the behaviour that motivates it.
    /// </summary>
    [Fact]
    public void WhitespaceBindsToARelativeUri_WhichIsWhyStartupValidationRefusesIt()
    {
        var options = Bind(new Dictionary<string, string?>
        {
            [$"{EntraIdOptions.SectionName}:ApplicationIdUri"] = "   ",
        });

        Assert.NotNull(options.ApplicationIdUri);
        Assert.False(options.ApplicationIdUri.IsAbsoluteUri);
    }

    [Fact]
    public void AMissingApplicationIdUri_BindsToNothing()
    {
        Assert.Null(Bind([]).ApplicationIdUri);
    }

    /// <summary>
    /// The host-qualified form Teams single sign-on requires, which is neither of the two audiences
    /// Microsoft.Identity.Web derives from the client id on its own — and the reason the setting
    /// exists at all.
    /// </summary>
    [Fact]
    public void AHostQualifiedApplicationIdUri_BindsAsGiven()
    {
        var options = Bind(new Dictionary<string, string?>
        {
            [$"{EntraIdOptions.SectionName}:ApplicationIdUri"] =
                "api://todowerk.example.com/11111111-1111-1111-1111-111111111111",
        });

        Assert.Equal(
            "api://todowerk.example.com/11111111-1111-1111-1111-111111111111",
            options.ApplicationIdUri?.ToString());
    }

    private static EntraIdOptions Bind(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var options = new EntraIdOptions();

        configuration.GetSection(EntraIdOptions.SectionName).Bind(options);

        return options;
    }
}
