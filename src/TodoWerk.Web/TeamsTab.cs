namespace TodoWerk.Web;

/// <summary>
/// Where the Teams surface lives, and the one fact three places have to agree on: the framing
/// headers branch on this path, the endpoints serve their documents from it, and the app manifest
/// points <c>contentUrl</c> at it.
/// <para>
/// The path <em>is</em> the discriminator — there is no <c>isInTeams</c> flag anywhere, in a
/// querystring or otherwise. A second assertion of the same fact is a second place for it to be
/// wrong, and the one that mattered would be the one the security headers did not read.
/// </para>
/// </summary>
internal static class TeamsTab
{
    /// <summary>The tab's own document. Everything below it is the Teams surface.</summary>
    public const string BasePath = "/teams";

    /// <summary>
    /// The consent popup's last stop, and the only local return URL the popup ever asks for. It
    /// initializes TeamsJS, calls <c>authentication.notifySuccess()</c>, and does nothing else.
    /// </summary>
    public const string AuthEndPath = "/teams/auth-end";

    /// <summary>
    /// The header the tab puts on the one request that follows a successful token exchange, and on
    /// nothing else. It names what the request is rather than who is making it: a 401 answered to a
    /// request wearing it is the blocked-cookie signature, which the server cannot see for itself
    /// because it only ever sees one half of the pair.
    /// <para>
    /// Read by <c>TeamsBootstrapDiagnostics</c> and by nothing that decides access. Client half:
    /// <c>teamsBootstrapHeader</c> in <c>ClientApp/src/api/client.ts</c>.
    /// </para>
    /// </summary>
    public const string BootstrapHeader = "X-TodoWerk-Teams-Bootstrap";

    /// <summary>Built by Vite from <c>ClientApp/teams/index.html</c>.</summary>
    public const string TabDocument = "teams/index.html";

    /// <summary>Built by Vite from <c>ClientApp/teams/auth-end.html</c>.</summary>
    public const string AuthEndDocument = "teams/auth-end.html";

    /// <summary>
    /// The exact addresses the two documents answer on, including the ones the static-file
    /// middleware serves them from directly. Named one by one rather than matched as a prefix: the
    /// SPA's fallback answers everything else under <c>/teams/</c> with the browser Workbench, and
    /// a prefix would hand that document the relaxed framing headers meant for the tab — which is
    /// the whole of what <see cref="Hosts"/> exists to prevent.
    /// </summary>
    private static readonly string[] Framable =
    [
        BasePath,
        BasePath + "/",
        AuthEndPath,
        "/" + TabDocument,
        "/" + AuthEndDocument,
    ];

    /// <summary>
    /// Whether a request is for one of the tab's own documents, and therefore whether it may be
    /// framed.
    /// <para>
    /// Complete rather than leaky even though scripts and stylesheets live outside the path:
    /// <c>frame-ancestors</c> applies to documents only, so a subresource needs no exception to be
    /// loaded inside a frame.
    /// </para>
    /// </summary>
    public static bool Hosts(PathString path) =>
        Framable.Any(framable => path.Equals(framable, StringComparison.OrdinalIgnoreCase));
}
