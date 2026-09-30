namespace TodoWerk.Web.Handbook;

/// <summary>
/// Where the About Page points beyond this host, and where the Guide is. Every value is optional
/// and every one is absent on a Self-Host that has none of them: the About Page renders each link
/// only where a target is named, and a deployment that names nothing still serves a page that is
/// true ([ADR-0013](../../../docs/adr/0013-the-handbook-is-split-by-kinship.md)).
/// <para>
/// The support contact is deliberately not here — it is <c>Legal:OperatorContact</c>, because the
/// person to ask about an installation is the operator the terms already name. The link to the
/// issue tracker is not here either: it is a fact about the software rather than about the
/// deployment, and is not configurable.
/// </para>
/// <para>
/// Bound as <see cref="Uri"/> rather than as text so that a value which is present and not an
/// address fails startup rather than becoming a dead link on the one page the Teams admin centre
/// shows as the app's support link. An empty string binds to nothing and is simply unset.
/// </para>
/// </summary>
public sealed class HandbookOptions
{
    public const string SectionName = "Handbook";

    /// <summary>
    /// The Guide's address. On the Hosted Service it is served on this host under <c>/guide/</c>
    /// by something other than this application, so the application learns it here or not at all:
    /// the help menu in the header carries a Guide entry when there is one and none when there is
    /// not, and the shared header of every served document does the same.
    /// </summary>
    public Uri? GuideUrl { get; set; }

    /// <summary>The operator's imprint — the legal notice a German website is required to carry.</summary>
    public Uri? ImprintUrl { get; set; }

    /// <summary>
    /// The operator's terms of business — the commercial terms a subscription is bought under,
    /// which are not the terms of use a person signs in under. Those are <c>TERMS.md</c>.
    /// </summary>
    public Uri? TermsOfBusinessUrl { get; set; }

    /// <summary>Where TodoWerk is ordered.</summary>
    public Uri? OrderUrl { get; set; }

    /// <summary>
    /// The Landing Page: TodoWerk's product page on the operator's own website, where the pitch and
    /// the price live. Distinct from the About Page, which is on this host and exists because a
    /// manifest has to name something.
    /// </summary>
    public Uri? LandingPageUrl { get; set; }

    /// <summary>The settings nobody has filled in, fully qualified, for the startup log.</summary>
    public IReadOnlyList<string> UnsetSettings =>
        [.. Settings.Where(setting => setting.Value is null).Select(setting => $"{SectionName}:{setting.Key}")];

    /// <summary>
    /// Absent is fine; present and not a web address is not. <c>javascript:</c> and friends are
    /// refused here rather than escaped later, because a link's scheme is the one thing HTML
    /// encoding does not protect against.
    /// </summary>
    internal bool AllAbsoluteWebAddresses =>
        Settings.All(setting => setting.Value is null or { IsAbsoluteUri: true, Scheme: "http" or "https" });

    private IEnumerable<(string Key, Uri? Value)> Settings =>
    [
        (nameof(GuideUrl), GuideUrl),
        (nameof(ImprintUrl), ImprintUrl),
        (nameof(TermsOfBusinessUrl), TermsOfBusinessUrl),
        (nameof(OrderUrl), OrderUrl),
        (nameof(LandingPageUrl), LandingPageUrl),
    ];
}
