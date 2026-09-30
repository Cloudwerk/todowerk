using TodoWerk.Web.Handbook;
using TodoWerk.Web.Legal;

namespace TodoWerk.Web.Documents;

/// <summary>
/// Which of the two kinds of document an entry is: the product explaining itself, or what somebody
/// signed in under. It exists because the one surface that presents these as a list rather than as
/// a row — the help menu in the shell — puts a divider between the two, and a divider drawn at a
/// hardcoded position is the third way these lists could drift apart after the set and the order.
/// Every other reader ignores it and renders the entries flat.
/// </summary>
internal enum DocumentGroup
{
    Handbook,
    Legal,
}

/// <summary>One entry in the header every served document shares.</summary>
internal sealed record DocumentLink(string Text, string Href, DocumentGroup Group);

/// <summary>
/// The documents TodoWerk offers a reader, in the order it offers them: the About Page, the
/// Administrator's Guide, the terms of use and the privacy notice — and the Guide, when this
/// deployment has one. The Guide is the one entry that can be absent: it is served on the Hosted
/// Service's host by something other than this application, so the application knows its address
/// from configuration or not at all
/// ([ADR-0013](../../../docs/adr/0013-the-handbook-is-split-by-kinship.md)).
/// <para>
/// This is the only list of them. Two surfaces render it — the header every served document
/// carries, and the help menu in the shell, which reads it from <c>GET /api/handbook</c> — and
/// neither holds a set, an order or an address of its own, because two lists agreeing by
/// inspection alone is a drift nothing would notice: nothing fails when they stop.
/// </para>
/// </summary>
internal sealed class DocumentNav(IReadOnlyList<DocumentLink> links)
{
    public IReadOnlyList<DocumentLink> Links { get; } = links;

    public static DocumentNav For(HandbookOptions handbook)
    {
        List<DocumentLink> links =
        [
            new("About TodoWerk", HandbookPages.AboutPath, DocumentGroup.Handbook),
            new("For administrators", HandbookPages.AdministratorsPath, DocumentGroup.Handbook),
        ];

        if (handbook.GuideUrl is { } guide)
        {
            links.Add(new("Guide", guide.AbsoluteUri, DocumentGroup.Handbook));
        }

        links.Add(new("Terms of use", LegalPages.TermsPath, DocumentGroup.Legal));
        links.Add(new("Privacy notice", LegalPages.PrivacyPath, DocumentGroup.Legal));

        return new DocumentNav(links);
    }
}
