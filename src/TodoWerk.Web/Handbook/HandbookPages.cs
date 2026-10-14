using System.Globalization;
using System.Text;
using Microsoft.Extensions.Options;
using TodoWerk.Application.Changes;
using TodoWerk.Application.Onboarding;
using TodoWerk.Infrastructure.Indexing;
using TodoWerk.Web.Diagnostics;
using TodoWerk.Web.Documents;
using TodoWerk.Web.Legal;

namespace TodoWerk.Web.Handbook;

/// <summary>
/// The two Handbook pages the application serves itself: the About Page, which the App Package's
/// <c>developer.websiteUrl</c> names and the Teams admin centre shows as the app's support link,
/// and the Administrator's Guide, which its <c>publisherDocsUrl</c> names. Both are read by people
/// who have not signed in and may never, so both are anonymous; both are the markdown files under
/// <c>handbook/</c> in the repository, compiled in and rendered once at startup.
/// <para>
/// The Guide — the Handbook's other half, for the person using the product — is deliberately not
/// here. It is served on the Hosted Service's host by something other than this application, and
/// the application knows only its address
/// ([ADR-0013](../../../docs/adr/0013-the-handbook-is-split-by-kinship.md)).
/// </para>
/// <para>
/// The Administrator's Guide states this installation's own numbers — the statistics floor, how
/// long a change stays undoable, how long somebody may stay away before they are forgotten — read
/// from the same options the code enforces, so the page cannot describe a default the deployment
/// has overridden. That is the one mechanical tie between the prose and the code; the prose itself
/// is a second telling of what the ADRs decide, for a different reader.
/// </para>
/// </summary>
internal sealed class HandbookPages(
    IOptions<LegalOptions> legal,
    IOptions<HandbookOptions> handbook,
    IOptions<OnboardingOptions> onboarding,
    IOptions<ChangeOptions> changes,
    IOptions<IndexingOptions> indexing,
    DocumentNav nav)
{
    public const string AboutPath = "/about";

    public const string AdministratorsPath = "/administrators";

    /// <summary>Where the two files live in the repository, so their relative links resolve from there.</summary>
    private const string SourceDirectory = "handbook/";

    /// <summary>Where a commit of the public repository is shown, ending where the full sha goes.</summary>
    private const string CommitUrl = "https://github.com/Cloudwerk/todowerk/commit/";

    /// <summary>
    /// Named one by one rather than as a prefix, for the reason <see cref="LegalPages.Hosts"/>
    /// gives: everything else is the SPA's, and wants its antiforgery token.
    /// </summary>
    public static bool Hosts(PathString path) =>
        path.Equals(AboutPath, StringComparison.OrdinalIgnoreCase)
        || path.Equals(AdministratorsPath, StringComparison.OrdinalIgnoreCase);

    private readonly Lazy<string> _about = new(() => RenderAbout(legal.Value, handbook.Value, nav));

    private readonly Lazy<string> _administrators = new(() =>
        RenderAdministrators(legal.Value, handbook.Value, onboarding.Value, changes.Value, indexing.Value, nav));

    public string About => _about.Value;

    public string Administrators => _administrators.Value;

    private static string RenderAbout(LegalOptions legal, HandbookOptions handbook, DocumentNav nav)
    {
        var markdown = Read("about.md")
            .Replace("{{HandbookLinks}}", HandbookLinks(handbook), StringComparison.Ordinal)
            .Replace("{{OperatorLinks}}", OperatorLinks(legal, handbook), StringComparison.Ordinal)
            .Replace("{{Operator}}", legal.OperatorOrFallback, StringComparison.Ordinal)
            .Replace("{{OperatorContact}}", DocumentPage.AsClickableContact(legal.OperatorContactOrFallback), StringComparison.Ordinal)
            .Replace("{{Build}}", BuildLine(BuildVersion.OfRunningApplication()), StringComparison.Ordinal);

        return DocumentPage.Render(markdown, SourceDirectory, nav, fallbackTitle: "About TodoWerk");
    }

    private static string RenderAdministrators(
        LegalOptions legal,
        HandbookOptions handbook,
        OnboardingOptions onboarding,
        ChangeOptions changes,
        IndexingOptions indexing,
        DocumentNav nav)
    {
        var markdown = Read("administrators.md")
            .Replace("{{GuideLine}}", GuideLine(handbook), StringComparison.Ordinal)
            .Replace("{{Operator}}", legal.OperatorOrFallback, StringComparison.Ordinal)
            .Replace("{{OperatorContact}}", DocumentPage.AsClickableContact(legal.OperatorContactOrFallback), StringComparison.Ordinal)
            .Replace("{{StatisticsFloor}}", onboarding.StatisticsFloor.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{{ChangeRetentionDays}}", DocumentPage.Days(changes.ChangeRetention), StringComparison.Ordinal)
            .Replace("{{DormancyWindowDays}}", DocumentPage.Days(onboarding.DormancyWindow), StringComparison.Ordinal)
            .Replace("{{IdleAfterDays}}", DocumentPage.Days(indexing.IdleAfter), StringComparison.Ordinal);

        return DocumentPage.Render(markdown, SourceDirectory, nav, fallbackTitle: "TodoWerk for administrators");
    }

    /// <summary>
    /// Which build this page is served by: the version, the commit linked to the public repository,
    /// and the commit's date — what <c>/version</c> reports, and what a bug report needs. The version
    /// is the release tag the build was made from (Directory.Build.props), so a build after a release
    /// names itself a pre-release of the next one rather than the release.
    /// </summary>
    internal static string BuildLine(BuildVersion build)
    {
        ArgumentNullException.ThrowIfNull(build);

        if (build.Commit is not { } commit)
        {
            return "This installation was built outside a git working tree, so it cannot say which revision of TodoWerk it runs.";
        }

        var shortCommit = commit.Length > 7 ? commit[..7] : commit;
        var version = build.Version == BuildVersion.Unknown ? string.Empty : $" {build.Version}";
        var line = $"This installation runs TodoWerk{version} at commit [`{shortCommit}`]({CommitUrl}{commit})";

        return build.CommitDate is { } date
            ? $"{line}, of {date.ToString("d MMMM yyyy", CultureInfo.InvariantCulture)}."
            : $"{line}.";
    }

    private static string Read(string resource) =>
        DocumentPage.ReadResource($"{typeof(HandbookPages).Namespace}.{resource}");

    /// <summary>
    /// The list under "Read more": the Guide when this deployment has one, and the Administrator's
    /// Guide always — written as the relative link the repository file would use, so that it goes
    /// through the same resolution as every other link and lands on the served page.
    /// </summary>
    private static string HandbookLinks(HandbookOptions handbook)
    {
        var lines = new StringBuilder();

        if (handbook.GuideUrl is { } guide)
        {
            lines.Append("- [The Guide](").Append(guide.AbsoluteUri)
                .AppendLine(") — how to use TodoWerk: what the Workbench shows, what each of the three changes does, and what preview and undo guarantee.");
        }

        lines.AppendLine("- [For administrators](administrators.md) — what approving TodoWerk for an organisation grants and what it does not, what it stores about a person, and how to make it available.");

        return lines.ToString();
    }

    /// <summary>
    /// The one sentence in the Administrator's Guide that mentions the Guide, present only when
    /// there is one to mention.
    /// </summary>
    private static string GuideLine(HandbookOptions handbook) =>
        handbook.GuideUrl is { } guide
            ? $"What the product looks like to the people using it is in [the Guide]({guide.AbsoluteUri})."
            : string.Empty;

    /// <summary>
    /// A whole section, or nothing: a heading over an empty list would be a page admitting it had
    /// nothing to say. Each link appears only where the deployment named a target.
    /// </summary>
    private static string OperatorLinks(LegalOptions legal, HandbookOptions handbook)
    {
        var operatorName = legal.OperatorOrFallback;
        var links = new StringBuilder();

        if (handbook.LandingPageUrl is { } landingPage)
        {
            links.Append("- [TodoWerk from ").Append(operatorName).Append("](").Append(landingPage.AbsoluteUri)
                .AppendLine(") — the product page: what it costs, and how to buy it.");
        }

        if (handbook.OrderUrl is { } order)
        {
            links.Append("- [Order TodoWerk](").Append(order.AbsoluteUri).AppendLine(")");
        }

        if (handbook.TermsOfBusinessUrl is { } termsOfBusiness)
        {
            links.Append("- [Terms of business](").Append(termsOfBusiness.AbsoluteUri)
                .AppendLine(") — the commercial terms a subscription is bought under. The terms of use above are the ones you sign in under.");
        }

        if (handbook.ImprintUrl is { } imprint)
        {
            links.Append("- [Imprint](").Append(imprint.AbsoluteUri).AppendLine(")");
        }

        return links.Length == 0
            ? string.Empty
            : $"## {operatorName} on the web{Environment.NewLine}{Environment.NewLine}{links}";
    }
}
