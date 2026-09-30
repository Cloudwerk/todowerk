using Microsoft.Extensions.Options;
using TodoWerk.Application.Onboarding;
using TodoWerk.Web.Documents;

namespace TodoWerk.Web.Legal;

/// <summary>
/// The two documents an app registration and a Store listing have to be able to point a stranger
/// at: the terms of use and the privacy notice. They are served by the application itself so that
/// the page a person at a consent dialog reads and the document in the repository are the same
/// document — <c>TERMS.md</c> and <c>PRIVACY.md</c> are compiled in as resources and rendered
/// here, so there is no second copy to update and none to forget.
/// <para>
/// Rendered once per document and held, because neither can change without a deploy.
/// </para>
/// <para>
/// Nothing on these pages sells anything, and nothing should be added that does. A store listing
/// expects the terms of use and the privacy policy to be free of commerce-related UI, which means
/// no price, no plan comparison and no purchase link — a check against the rendered page rather
/// than against the intent behind it. The About Page, which may link to a way to order, is a
/// neighbour on the same renderer and not a member of this pair.
/// </para>
/// </summary>
internal sealed class LegalPages(IOptions<LegalOptions> options, IOptions<OnboardingOptions> onboarding, DocumentNav nav)
{
    public const string TermsPath = "/legal/terms";

    public const string PrivacyPath = "/legal/privacy";

    /// <summary>
    /// The two addresses, named one by one rather than matched as a <c>/legal</c> prefix. Nothing
    /// else under that prefix is mapped, so a prefix would also cover every unmapped path beneath
    /// it — and those are answered by the SPA's fallback, which is a document that does want its
    /// antiforgery token.
    /// </summary>
    public static bool Hosts(PathString path) =>
        path.Equals(TermsPath, StringComparison.OrdinalIgnoreCase)
        || path.Equals(PrivacyPath, StringComparison.OrdinalIgnoreCase);

    private readonly Lazy<string> _terms = new(() => Render("terms.md", options.Value, onboarding.Value, nav));

    private readonly Lazy<string> _privacy = new(() => Render("privacy.md", options.Value, onboarding.Value, nav));

    public string Terms => _terms.Value;

    public string Privacy => _privacy.Value;

    /// <summary>
    /// The operator's name and contact are substituted into the source before it is parsed, so a
    /// value taken from configuration cannot become markup. Both documents live at the repository
    /// root, so their relative links resolve from there.
    /// <para>
    /// The dormancy window is substituted for the same reason the Administrator's Guide reads it:
    /// retention is configuration, and a notice that stated the default would promise the wrong
    /// number on every deployment that changed it — which is a promise to a regulator, not a typo.
    /// </para>
    /// </summary>
    private static string Render(string resource, LegalOptions legal, OnboardingOptions onboarding, DocumentNav nav)
    {
        var markdown = DocumentPage.ReadResource($"{typeof(LegalPages).Namespace}.{resource}")
            .Replace("{{Operator}}", legal.OperatorOrFallback, StringComparison.Ordinal)
            .Replace("{{OperatorContact}}", DocumentPage.AsClickableContact(legal.OperatorContactOrFallback), StringComparison.Ordinal)
            .Replace("{{GoverningLaw}}", legal.GoverningLawOrFallback, StringComparison.Ordinal)
            .Replace("{{DormancyWindowDays}}", DocumentPage.Days(onboarding.DormancyWindow), StringComparison.Ordinal);

        return DocumentPage.Render(markdown, sourceDirectory: "", nav, fallbackTitle: "Legal");
    }
}
