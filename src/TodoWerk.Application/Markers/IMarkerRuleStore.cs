using TodoWerk.Application.Abstractions.Authentication;
using TodoWerk.Application.Abstractions.Markers;
using TodoWerk.Domain.Hashtags;
using TodoWerk.SharedKernel;

namespace TodoWerk.Application.Markers;

/// <summary>
/// Where Marker Rules are kept, from the request path's point of view. Everything here is per
/// person: no surface in TodoWerk shows one person another's rules, and this port has no shape
/// that could.
/// <para>
/// The refusals live down here rather than in the handlers because two of them are races — a
/// Marker taken between the check and the insert, a second rule for the same Hashtag from two tabs
/// — and only the code holding the unique index can tell a lost race from a real conflict.
/// </para>
/// </summary>
public interface IMarkerRuleStore
{
    /// <summary>This person's rules, in position order. Deleted ones are not rules any more.</summary>
    Task<IReadOnlyList<MarkerRuleRecord>> ListAsync(IndexUser user, CancellationToken cancellationToken);

    /// <summary>
    /// The Markers of the rules this person has deleted — still at the front of the titles they
    /// were applied to, so coverage has to read a block with them in it.
    /// </summary>
    Task<IReadOnlyList<AbandonedMarkerSnapshot>> ListAbandonedMarkersAsync(
        IndexUser user,
        CancellationToken cancellationToken);

    /// <summary>
    /// Adds a rule at the end of the list. Refused when the Hashtag already has one, or when the
    /// Marker belongs to another rule — named, in both cases.
    /// </summary>
    Task<Result<MarkerRuleRecord>> CreateAsync(
        IndexUser user,
        string key,
        string spelling,
        Marker marker,
        CancellationToken cancellationToken);

    /// <summary>
    /// Gives the rule a new Marker, recording the one blocks are still carrying so the next Apply
    /// can swap it.
    /// </summary>
    Task<Result<MarkerRuleRecord>> ChangeMarkerAsync(
        IndexUser user,
        Guid ruleId,
        Marker marker,
        CancellationToken cancellationToken);

    /// <summary>Swaps the rule with its neighbour, which is what reordering the block means.</summary>
    Task<Result<MarkerRuleRecord>> MoveAsync(
        IndexUser user,
        Guid ruleId,
        MarkerRuleMove move,
        CancellationToken cancellationToken);

    /// <summary>
    /// Deletes the rule, which writes nothing to anybody's tasks (ADR-0014). The row is kept and
    /// marked rather than removed, because its Marker is still in every block it was applied to and
    /// has to go on being read as part of one.
    /// </summary>
    Task<bool> DeleteAsync(IndexUser user, Guid ruleId, CancellationToken cancellationToken);
}
