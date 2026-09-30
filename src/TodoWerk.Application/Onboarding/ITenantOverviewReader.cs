namespace TodoWerk.Application.Onboarding;

/// <summary>The Tenant Member table as the overview reads it: aggregates over one tenant.</summary>
public interface ITenantOverviewReader
{
    /// <summary>
    /// The member count, how many fall inside each window in <paramref name="windows"/>, and when
    /// the first of them signed in — computed from the database at request time rather than read
    /// from a maintained total.
    /// <para>
    /// A maintained total would be a second source of truth for a figure nobody makes decisions on,
    /// and it would be wrong exactly where that is hardest to notice: after a partial scan failure
    /// or a cancellation sweep.
    /// </para>
    /// </summary>
    Task<TenantMemberCounts> ReadAsync(
        string tenantId,
        IReadOnlyList<TimeSpan> windows,
        CancellationToken cancellationToken);
}

/// <summary>
/// What the Tenant Member table alone can answer. The Occurrence total is not here: it belongs to
/// the index, which is another module's, and it arrives through a port instead.
/// </summary>
/// <param name="MemberCount">People who have ever signed in, anonymised rows included.</param>
/// <param name="IdentifiableMemberCount">
/// The rows that still name somebody. This is what the suppression floor and the average's
/// denominator have to be computed from, because it is the number of people the statistics still
/// describe: an anonymised row holds no Occurrences and no activity, so counting it toward the
/// floor would release figures about fewer identifiable people than the floor promises.
/// </param>
/// <param name="ActiveCounts">One count per requested window, in the order they were asked for.</param>
/// <param name="FirstSignedInAt">Null only when nobody has ever signed in.</param>
public sealed record TenantMemberCounts(
    int MemberCount,
    int IdentifiableMemberCount,
    IReadOnlyList<int> ActiveCounts,
    DateTimeOffset? FirstSignedInAt);
