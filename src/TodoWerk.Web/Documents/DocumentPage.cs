using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using TodoWerk.Web.Handbook;
using TodoWerk.Web.Legal;

namespace TodoWerk.Web.Documents;

/// <summary>
/// The one shape every document TodoWerk serves about itself takes: the terms of use, the privacy
/// notice, the About Page and the Administrator's Guide. Each is a markdown file in the repository,
/// compiled in as a resource, rendered once at startup and held — so the page a stranger reads and
/// the file a contributor reads are the same document, and there is no second copy to forget
/// ([ADR-0013](../../../docs/adr/0013-the-handbook-is-split-by-kinship.md)).
/// <para>
/// One self-contained page: no script, no font and nothing fetched from anywhere else, so it
/// renders identically wherever it is fetched from and needs nothing the content security policy
/// would have to be widened for. The styles are inline, which <c>style-src</c> already allows for
/// Fluent's sake, and the one picture — the app icon in the header — is inlined as a <c>data:</c>
/// address, which <c>img-src</c> already allows.
/// </para>
/// </summary>
internal static partial class DocumentPage
{
    /// <summary>
    /// Where a relative link in any document resolves to. The documents reference the ADRs, the
    /// runbooks, the security policy and each other; only the four served here become paths on
    /// this host, and everything else becomes the same file in the public repository, because
    /// that is where it is.
    /// </summary>
    public const string RepositoryBase = "https://github.com/Cloudwerk/todowerk/blob/main/";

    /// <summary>The brand teal, and the manifest's <c>accentColor</c>.</summary>
    private const string Accent = "#006970";

    /// <summary>
    /// The App Package's colour icon, read once and inlined into the header of every page, so the
    /// header carries the same tile Teams shows.
    /// </summary>
    private static readonly Lazy<string> Icon = new(() =>
    {
        using var stream = typeof(DocumentPage).GetTypeInfo().Assembly
            .GetManifestResourceStream("TodoWerk.Web.Documents.icon.png")
            ?? throw new InvalidOperationException(
                "The embedded resource 'TodoWerk.Web.Documents.icon.png' is missing; see TodoWerk.Web.csproj.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);

        return "data:image/png;base64," + Convert.ToBase64String(buffer.ToArray());
    });

    /// <summary>
    /// The files served by the application, by their path in the repository, and the address each
    /// answers on. A link from one document to another lands on the served page rather than on
    /// GitHub, and a link to anything else lands on GitHub rather than on the SPA's fallback —
    /// which would be a broken link that returns 200, the kind nobody notices.
    /// </summary>
    private static readonly Dictionary<string, string> Served = new(StringComparer.Ordinal)
    {
        ["TERMS.md"] = LegalPages.TermsPath,
        ["PRIVACY.md"] = LegalPages.PrivacyPath,
        ["handbook/about.md"] = HandbookPages.AboutPath,
        ["handbook/administrators.md"] = HandbookPages.AdministratorsPath,
    };

    /// <summary>
    /// Tables, because the privacy notice is largely tables; autolinks, so that a bare web address
    /// in any document is one a reader can click; generic attributes, so that a document can mark
    /// its opening sentence as the lead (<c>{.lead}</c>) without carrying any markup of its own.
    /// Raw HTML is disabled: the documents contain none,
    /// and every value substituted into a document before it is parsed comes from configuration,
    /// so a value taken from configuration cannot become markup.
    /// </summary>
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UsePipeTables()
        .UseAutoLinks()
        .UseGenericAttributes()
        .DisableHtml()
        .Build();

    /// <summary>
    /// Renders one document. <paramref name="sourceDirectory"/> is where the file lives in the
    /// repository — empty for the root, otherwise with a trailing slash — so that its relative
    /// links resolve the way GitHub resolves them.
    /// </summary>
    public static string Render(string markdown, string sourceDirectory, DocumentNav nav, string fallbackTitle)
    {
        var document = Markdown.Parse(markdown, Pipeline);
        var documentBase = new Uri(RepositoryBase + sourceDirectory, UriKind.Absolute);

        foreach (var link in document.Descendants<LinkInline>())
        {
            link.Url = Resolve(link.Url, documentBase);
        }

        // The title is the document's own first heading rather than a name repeated here, for the
        // same reason the body is: one place to change it.
        var title = Heading().Match(markdown) is { Success: true } heading
            ? heading.Groups[1].Value.Trim()
            : fallbackTitle;

        return Page(title, document.ToHtml(Pipeline), nav);
    }

    public static string ReadResource(string logicalName)
    {
        using var stream = typeof(DocumentPage).GetTypeInfo().Assembly.GetManifestResourceStream(logicalName)
            ?? throw new InvalidOperationException(
                $"The embedded resource '{logicalName}' is missing. The documents TodoWerk serves are "
                + "compiled into TodoWerk.Web as resources; see its .csproj.");

        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }

    /// <summary>
    /// An address a document tells a reader to write to should be one they can click, so a contact
    /// that is an email address is wrapped as a markdown autolink. Anything else — including the
    /// unconfigured fallback, which is a phrase rather than an address — is left as written.
    /// </summary>
    public static string AsClickableContact(string value) => EmailShaped().IsMatch(value) ? $"<{value}>" : value;

    /// <summary>
    /// A configured period as the whole number of days a sentence can carry. Shared by every
    /// document that states one of this installation's own numbers, so they all round the same way.
    /// </summary>
    public static string Days(TimeSpan period) =>
        Math.Round(period.TotalDays).ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// Absolute addresses, in-page anchors and paths on this host are left alone. Everything else
    /// is resolved against the document's own place in the repository, exactly as GitHub would,
    /// and then either swapped for the address this application serves that file on or left
    /// pointing at the repository.
    /// </summary>
    private static string? Resolve(string? url, Uri documentBase)
    {
        if (string.IsNullOrEmpty(url) || url.StartsWith('#') || url.StartsWith('/'))
        {
            return url;
        }

        if (Uri.TryCreate(url, UriKind.Absolute, out _))
        {
            return url;
        }

        var absolute = new Uri(documentBase, url);
        var file = absolute.GetLeftPart(UriPartial.Path);

        return file.StartsWith(RepositoryBase, StringComparison.Ordinal)
            && Served.TryGetValue(file[RepositoryBase.Length..], out var path)
                ? path + absolute.Fragment
                : absolute.ToString();
    }

    private static string Page(string title, string body, DocumentNav nav)
    {
        var links = string.Concat(nav.Links.Select(link =>
            $"<a href=\"{WebUtility.HtmlEncode(link.Href)}\">{WebUtility.HtmlEncode(link.Text)}</a>"));

        return $$"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>{{WebUtility.HtmlEncode(title)}} — TodoWerk</title>
            <style>
            :root {
              color-scheme: light dark;
              --ink: #1b1b1b;
              --muted: #5c5c5c;
              --paper: #ffffff;
              --rule: #e2e2e2;
              --accent: {{Accent}};
            }
            @media (prefers-color-scheme: dark) {
              :root {
                --ink: #eaeaea;
                --muted: #a8a8a8;
                --paper: #161616;
                --rule: #333333;
                --accent: #5fd3da;
              }
            }
            * { box-sizing: border-box; }
            body {
              margin: 0;
              background: var(--paper);
              color: var(--ink);
              font: 16px/1.65 -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, Helvetica, Arial, sans-serif;
            }
            .wrap { max-width: 44rem; margin: 0 auto; padding: 2.5rem 1.25rem 4rem; }
            header { display: flex; flex-wrap: wrap; align-items: center; gap: .5rem 1.5rem; border-bottom: 1px solid var(--rule); padding-bottom: 1rem; margin-bottom: 2.5rem; }
            header a { color: var(--muted); text-decoration: none; }
            header a:hover { color: var(--accent); text-decoration: underline; }
            header .name { display: inline-flex; align-items: center; gap: .6rem; color: var(--accent); font-weight: 600; letter-spacing: .01em; }
            header .name img { width: 1.75rem; height: 1.75rem; border-radius: .375rem; }
            nav { display: flex; flex-wrap: wrap; gap: .25rem 1rem; margin-inline-start: auto; font-size: .875rem; }
            h1 { font-size: 1.9rem; line-height: 1.25; margin: 0 0 1.5rem; }
            h2 { font-size: 1.2rem; line-height: 1.35; margin: 2.5rem 0 .75rem; }
            h3 { font-size: 1.05rem; line-height: 1.35; margin: 1.75rem 0 .5rem; }
            p, li { overflow-wrap: break-word; }
            .lead { font-size: 1.25rem; line-height: 1.5; margin: 0 0 2rem; }
            blockquote { margin: 1.25rem 0; padding: 1rem 1.25rem; border-left: .25rem solid var(--accent); border-radius: 0 .5rem .5rem 0; background: color-mix(in srgb, var(--accent) 6%, transparent); }
            blockquote > :first-child { margin-top: 0; }
            blockquote > :last-child { margin-bottom: 0; }
            a { color: var(--accent); }
            strong { font-weight: 600; }
            ul { padding-left: 1.25rem; }
            li + li { margin-top: .4rem; }
            table { border-collapse: collapse; width: 100%; margin: 1.25rem 0; font-size: .9375rem; display: block; overflow-x: auto; }
            th, td { border: 1px solid var(--rule); padding: .5rem .625rem; text-align: left; vertical-align: top; }
            th { background: color-mix(in srgb, var(--accent) 8%, transparent); font-weight: 600; }
            footer { border-top: 1px solid var(--rule); margin-top: 3.5rem; padding-top: 1rem; color: var(--muted); font-size: .8125rem; }
            footer a { color: var(--muted); }
            </style>
            </head>
            <body>
            <div class="wrap">
            <header>
            <a class="name" href="/"><img src="{{Icon.Value}}" alt="" width="28" height="28">TodoWerk</a>
            <nav>{{links}}</nav>
            </header>
            <main>
            {{body}}
            </main>
            <footer>
            TodoWerk is free software under the GNU Affero General Public License v3.0.
            The source, and the history of this document, are at
            <a href="https://github.com/Cloudwerk/todowerk">github.com/Cloudwerk/todowerk</a>.
            </footer>
            </div>
            </body>
            </html>
            """;
    }

    [GeneratedRegex(@"^#\s+(.+)$", RegexOptions.Multiline)]
    private static partial Regex Heading();

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailShaped();
}
