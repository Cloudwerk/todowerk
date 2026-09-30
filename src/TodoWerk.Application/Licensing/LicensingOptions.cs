namespace TodoWerk.Application.Licensing;

/// <summary>
/// How this deployment reaches ManagementPortal, and the two numbers TodoWerk decides for itself.
/// <para>
/// The section is either complete or absent, and absent is what a Self-Host is: no portal client
/// is registered, no outbound call is made, every resolution answers "no authority" and no banner
/// or panel appears anywhere. A half-filled section is neither, and fails startup validation
/// rather than becoming a Hosted Service that cannot reach its portal.
/// </para>
/// <para>
/// In the Application layer rather than beside <c>IndexingOptions</c> in Infrastructure, for the
/// reason <c>ChangeOptions</c> is: these are read on both sides — the client that calls the portal
/// is there, the query that decides whether a Trial is ending soon is here.
/// </para>
/// </summary>
public sealed class LicensingOptions
{
    public const string SectionName = "Licensing";

    /// <summary>
    /// The portal's origin, absolute, scheme included — <c>https://portal.example.com</c>. The
    /// first-party paths are appended to it, so it carries no path of its own.
    /// </summary>
    public string PortalHost { get; set; } = string.Empty;

    /// <summary>
    /// The application API key CloudWerk configured for TodoWerk, sent as <c>X-Application-Key</c>.
    /// A server secret: it opens every entitlement for this solution, and the SPA is public code,
    /// so it never reaches a browser, a bundle or a log line.
    /// </summary>
    public string ApplicationKey { get; set; } = string.Empty;

    /// <summary>The solution's slug in the portal. Fixed, and the one the key is bound to.</summary>
    public string SolutionSlug { get; set; } = string.Empty;

    /// <summary>
    /// How long TodoWerk keeps serving on somebody's last positive answer while the portal cannot
    /// be reached, counted from that answer. After it, they meet the "could not be verified" card
    /// and every request retries.
    /// <para>
    /// TodoWerk's number rather than the portal's, and the one place this deployment overrides the
    /// contract. The portal delivers a <c>failOpenSeconds</c> of its own and TodoWerk honours the
    /// <c>recheckSeconds</c> beside it — how fresh an answer has to be is the portal's business,
    /// because an operator binding a purchase has to land quickly. How long TodoWerk stays up
    /// during CloudWerk's own outage is not: both ends of the call are CloudWerk's, so no customer
    /// can bypass anything by making the portal unreachable, and what a short window buys is
    /// TodoWerk going dark for every tenant over an outage nobody outside CloudWerk caused. The
    /// portal's value is the fallback when it sends none.
    /// </para>
    /// </summary>
    public TimeSpan FailOpenWindow { get; set; } = TimeSpan.FromHours(24);

    /// <summary>
    /// How close to its end a Trial has to be before the banner turns into the warning. Seven days
    /// by default, and the banner names the date rather than counting down, so this decides which
    /// sentence appears and nothing about how it is worded.
    /// </summary>
    public TimeSpan TrialEndingSoon { get; set; } = TimeSpan.FromDays(7);

    /// <summary>
    /// How long one call to the portal may take before it counts as unreachable. Short on purpose:
    /// this call sits in front of every authenticated request, and a portal that has stopped
    /// answering must cost a person one timeout rather than one page load.
    /// </summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// How long an answer is trusted when the portal names no <c>recheckSeconds</c> of its own.
    /// A ceiling as well as a fallback: an answer may never be held longer than this, whatever the
    /// portal asks for, because an operator cutting an organisation off has to land in minutes.
    /// </summary>
    public TimeSpan MaximumCacheLifetime { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// The three settings that decide whether there is an authority to ask at all, and whether
    /// this deployment is a Hosted Service or a Self-Host. All three or none.
    /// </summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(PortalHost)
        && !string.IsNullOrWhiteSpace(ApplicationKey)
        && !string.IsNullOrWhiteSpace(SolutionSlug);

    /// <summary>A Self-Host: nothing filled in, nobody to ask, nothing sent anywhere.</summary>
    public bool IsAbsent =>
        string.IsNullOrWhiteSpace(PortalHost)
        && string.IsNullOrWhiteSpace(ApplicationKey)
        && string.IsNullOrWhiteSpace(SolutionSlug);
}
