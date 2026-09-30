namespace TodoWerk.Application.Onboarding;

/// <summary>
/// What a tenant is shown about its own use of TodoWerk. Mirrored by <c>TenantOverview</c> in the
/// client's <c>src/api/types.ts</c>.
/// </summary>
/// <param name="Statistics">
/// Absent — not empty, not zeroed — while fewer identifiable people are on record than the
/// configured floor. Identifiable, not cumulative: somebody who has been forgotten still counts in
/// the total but holds none of the data the figures describe, so the floor is measured against the
/// people it actually protects. Withheld from the response rather than from the rendering, because
/// a payload carrying the numbers and a screen choosing not to draw them is one refactor away from
/// disclosing them.
/// </param>
/// <param name="TenantConsentGrantedThroughTodoWerk">
/// Whether an administrator approved TodoWerk for this tenant <em>through TodoWerk</em>. A grant
/// made in the Entra portal is invisible here, so false means "not recorded", never "not
/// approved" — and the wording on screen has to say which.
/// </param>
public sealed record TenantOverviewDto(
    TenantStatisticsDto? Statistics,
    bool TenantConsentGrantedThroughTodoWerk);

/// <summary>
/// Counts, and nothing but counts. No name, no per-person row, no Hashtag: using TodoWerk must not
/// expose somebody to the people they work with.
/// </summary>
/// <param name="MemberCount">
/// How many people have ever signed in, including those since forgotten. A person who was erased
/// and later returned counts twice — recognising them would take the identifier anonymisation
/// removes, so the cumulative figure records arrivals rather than distinct persons (ADR-0009).
/// </param>
/// <param name="Activity">How many signed in inside each trailing window, shortest first.</param>
/// <param name="FirstSignedInAt">When the first of them started, so a reader can tell how established this is.</param>
/// <param name="TotalOccurrences">How many Hashtag Occurrences they hold between them.</param>
/// <param name="AverageOccurrencesPerMember">
/// The total over the people still on record — not over the cumulative count, whose forgotten
/// members hold nothing and would only water the figure down. Computed here rather than in the
/// browser: the client is not sent the denominator, and a division it performed itself would
/// invite the two to disagree.
/// </param>
public sealed record TenantStatisticsDto(
    int MemberCount,
    IReadOnlyList<TenantActivityWindowDto> Activity,
    DateTimeOffset FirstSignedInAt,
    long TotalOccurrences,
    double AverageOccurrencesPerMember);

/// <summary>
/// One trailing window and how many people fall inside it. The window travels as a number of days
/// so the label is generated from the figure rather than promised beside it — the longest of the
/// three is the retention window from configuration, and a hard-coded "last year" would start
/// lying the day somebody changed it.
/// </summary>
public sealed record TenantActivityWindowDto(int WindowDays, int MemberCount);
