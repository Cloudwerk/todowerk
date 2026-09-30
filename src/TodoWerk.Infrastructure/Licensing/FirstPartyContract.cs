using System.Text.Json;
using System.Text.Json.Serialization;

namespace TodoWerk.Infrastructure.Licensing;

/// <summary>
/// The First-Party Path's wire shapes, in the portal's own vocabulary and casing.
/// <para>
/// Kept in one file and out of the Domain deliberately: this is somebody else's vocabulary, in
/// somebody else's casing, and the moment it appears in a domain type TodoWerk has frozen a wire
/// format it does not own. Everything here is translated at the client's edge.
/// </para>
/// </summary>
/// <param name="TenantId">The Entra tenant id, from the token TodoWerk's own sign-in validated.</param>
/// <param name="ObjectId">The Entra object id, from the same token. Never from anything a browser sent.</param>
/// <param name="SolutionSlug">TodoWerk's slug in the portal; the one the application key is bound to.</param>
/// <param name="PackageVersion">This build's informational version, recorded in the portal's call trace.</param>
/// <param name="CheckKind">
/// <c>install</c> on a person's first resolution in this process, <c>periodic</c> afterwards.
/// A trace field: the portal answers the same either way.
/// </param>
internal sealed record FirstPartyResolveRequest(
    [property: JsonPropertyName("tenantId")] string TenantId,
    [property: JsonPropertyName("objectId")] string ObjectId,
    [property: JsonPropertyName("solutionSlug")] string SolutionSlug,
    [property: JsonPropertyName("packageVersion")] string? PackageVersion,
    [property: JsonPropertyName("checkKind")] string CheckKind);

/// <summary>
/// What the portal answers, always over HTTP 200 for an authenticated well-formed call. Every
/// field is optional here even where the contract says "always": a client that throws on a missing
/// field turns a portal deploy into a TodoWerk outage, and the resolver treats an unreadable
/// answer as unreachability, which is a state it already knows how to survive.
/// </summary>
internal sealed record FirstPartyResolveResponse
{
    /// <summary>The one bit that is enforced on.</summary>
    [JsonPropertyName("valid")]
    public bool Valid { get; init; }

    /// <summary><c>active</c>, <c>suspended</c>, <c>expired</c> or <c>invalid</c>. Logged, never branched on.</summary>
    [JsonPropertyName("status")]
    public string? Status { get; init; }

    /// <summary>Human-readable and safe to render verbatim: an expired Trial and an expired subscription read differently.</summary>
    [JsonPropertyName("message")]
    public string? Message { get; init; }

    /// <summary>UTC with the <c>Z</c>. Null when the licence has no end.</summary>
    [JsonPropertyName("expiresUtc")]
    public DateTimeOffset? ExpiresUtc { get; init; }

    /// <summary>How long this answer may be trusted. Honoured, clamped to the configured ceiling.</summary>
    [JsonPropertyName("recheckSeconds")]
    public int? RecheckSeconds { get; init; }

    /// <summary>
    /// The portal's own fail-open window. Read as the fallback only: this deployment's
    /// <c>Licensing:FailOpenWindow</c> decides how long TodoWerk keeps serving during a CloudWerk
    /// outage, and the reasoning is on that setting.
    /// </summary>
    [JsonPropertyName("failOpenSeconds")]
    public int? FailOpenSeconds { get; init; }

    /// <summary><c>tenant</c>, <c>personal</c> or <c>trial</c>.</summary>
    [JsonPropertyName("kind")]
    public string? Kind { get; init; }

    /// <summary>What seat usage is reported against. Present only on a valid answer.</summary>
    [JsonPropertyName("licenseId")]
    public string? LicenseId { get; init; }

    /// <summary>
    /// The per-licence secret usage tokens are keyed on. Present only on a valid answer, and never
    /// forwarded anywhere: not to a client, not into a log, not into another licence's token.
    /// </summary>
    [JsonPropertyName("usageSecret")]
    public string? UsageSecret { get; init; }

    /// <summary>
    /// Where a person buys TodoWerk, composed by the portal from its own running host and its own
    /// catalog. Absent — never null and never empty — where the portal has nothing to sell for
    /// this solution, which is a legitimate state rather than a fault.
    /// <para>
    /// The one field that rides on a <em>refusal</em> as well as on a valid answer, so that a link
    /// can reach an expired Trial — the one moment somebody wants to buy. The client
    /// configuration document is withheld on every non-valid answer, so nothing read from it could
    /// carry one.
    /// </para>
    /// <para>
    /// Read as a <see cref="JsonElement"/> rather than as the <c>string?</c> the contract promises,
    /// and this is the one field on this shape worth the awkwardness. Typed as a string, a portal
    /// that ever sent a number here would fail the whole answer to parse — which this client
    /// treats as unreachability, so a cosmetic field would deny every person until somebody
    /// deployed. Nothing decides on this value: dropping a malformed one costs a link, and
    /// dropping the answer costs the product. Every other field here is load-bearing enough that
    /// an answer it cannot read is genuinely an answer worth refusing.
    /// </para>
    /// </summary>
    [JsonPropertyName("purchaseUrl")]
    public JsonElement? PurchaseUrl { get; init; }
}

/// <summary>
/// One seat report. The object id is deliberately not on it: what identifies the person is a hash
/// keyed on the per-licence secret, computed on this server, so the raw id never leaves it.
/// </summary>
internal sealed record FirstPartyUsageRequest(
    [property: JsonPropertyName("tenantId")] string TenantId,
    [property: JsonPropertyName("solutionSlug")] string SolutionSlug,
    [property: JsonPropertyName("licenseId")] string LicenseId,
    [property: JsonPropertyName("usageToken")] string UsageToken);

/// <summary>
/// What the portal answers a seat report, over HTTP 200 whether or not it stored anything.
/// <para>
/// A refusal is a normal answer here rather than an error status, so callers must read the body
/// and not the status code. Optional for the same reason as the resolve response's fields: an
/// answer this build cannot read is treated as a report that did not land, never as an exception
/// on a path nothing is waiting for.
/// </para>
/// </summary>
internal sealed record FirstPartyUsageResponse
{
    /// <summary>
    /// Whether the portal stored a seat for this person under this licence. False for a licence it
    /// refused, false when the per-licence seat-token ceiling has been reached — and false, by
    /// design rather than by refusal, for a solution metered as Unlimited, which never meters.
    /// </summary>
    [JsonPropertyName("recorded")]
    public bool Recorded { get; init; }

    /// <summary>
    /// The licence's seat state in the portal's own words: <c>within</c>, <c>over</c>,
    /// <c>unlimited</c> or <c>invalid</c>. <c>invalid</c> is the licence being refused — not this
    /// tenant's, not this solution's, expired, suspended or unknown — and <c>over</c> is soft, a
    /// seat stored above the licence's limit. Named in the warning, so the line says which this was.
    /// </summary>
    [JsonPropertyName("seatStatus")]
    public string? SeatStatus { get; init; }

    /// <summary>Seats counted against the licence after this report. The contract's shape; nothing branches on it.</summary>
    [JsonPropertyName("seatsUsed")]
    public int? SeatsUsed { get; init; }

    /// <summary>The licence's seat limit, absent where there is none. The contract's shape; nothing branches on it.</summary>
    [JsonPropertyName("seatLimit")]
    public int? SeatLimit { get; init; }
}
