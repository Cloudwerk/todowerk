using TodoWerk.Application.Abstractions.Authentication;

namespace TodoWerk.Application.Abstractions.Erasure;

/// <summary>
/// One module's answer to "destroy everything you hold about this person". Implemented once per
/// place personal data accumulates — the index, the Changes and their journals, the token cache —
/// and orchestrated by the Onboarding module, which must never name those types.
/// <para>
/// This port exists for exactly that reason. Erasure is the one operation that touches every
/// module, so writing it as a method that reaches into each of them would put a dependency from
/// Onboarding onto Indexing and Changes that the module-boundary tests forbid — and the two
/// deliberate cross-module references already in the codebase are documented exceptions, not a
/// licence for more.
/// </para>
/// <para>
/// Implementations must be idempotent. A crash part-way through leaves the Tenant Member row still
/// identifiable, which is what lets the same erasure be attempted again against rows some of which
/// have already gone.
/// </para>
/// </summary>
public interface IPersonalDataPurge
{
    /// <summary>
    /// What this purge covers, for the log line the sweep writes. A sentence fragment, not a class
    /// name: it ends up in an operator's log, and "the hashtag index" is what they are looking for.
    /// </summary>
    string Describes { get; }

    /// <summary>
    /// Destroys everything this module holds about <paramref name="user"/>, and nothing about
    /// anybody else.
    /// </summary>
    Task<PurgeOutcome> PurgeAsync(IndexUser user, CancellationToken cancellationToken);
}

/// <summary>What one purge did, and whether it can say it finished.</summary>
/// <param name="Removed">
/// How much went, for the log: rows deleted, or entries evicted where the store does not report a
/// count. Zero is an ordinary answer — somebody who signed in once and never scanned has nothing in
/// most of these places.
/// </param>
/// <param name="NothingLeft">
/// False when the purge stopped while it was still finding rows.
/// <para>
/// A purge that repeats itself to catch what a concurrent worker wrote has to be bounded, and a
/// bound that is reached is not the same outcome as a bound that was not needed. Erasure must not
/// anonymise the membership record on the strength of the second: the row is the only thing left
/// that can find the residue, so removing the identifier would make what remains permanently
/// unreachable while the person was told they had been forgotten.
/// </para>
/// </param>
public sealed record PurgeOutcome(int Removed, bool NothingLeft)
{
    /// <summary>Converged, having removed <paramref name="removed"/>.</summary>
    public static PurgeOutcome Complete(int removed) => new(removed, NothingLeft: true);

    /// <summary>Stopped while rows were still going.</summary>
    public static PurgeOutcome Unfinished(int removed) => new(removed, NothingLeft: false);
}
