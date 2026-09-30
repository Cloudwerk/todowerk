using TodoWerk.Application.Abstractions.Authentication;
using TodoWerk.Application.Abstractions.Erasure;

namespace TodoWerk.Infrastructure.Licensing;

/// <summary>
/// Drops one person's cached licence answer, so that erasure leaves nothing of them in this
/// process either.
/// <para>
/// The cache is memory and holds an Entra object id with an answer beside it. That is short-lived
/// and never written down, so it is not what ADR-0009 is about — but somebody who has just asked
/// to be forgotten should not still be a key in a dictionary, and evicting it costs a line.
/// </para>
/// <para>
/// It also makes erasure work the way somebody would expect it to: the next request in the same
/// process resolves from scratch rather than serving a cached answer about somebody the database
/// no longer holds.
/// </para>
/// </summary>
internal sealed class LicencePersonalDataPurge(LicenceCache cache, PortalSeatUsageReporter seats)
    : IPersonalDataPurge
{
    public string Describes => "the cached licence answer and seat claim";

    public Task<PurgeOutcome> PurgeAsync(IndexUser user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);

        // Two entries at most, removed or already absent: the cached answer, and the note that
        // this person's seat has been reported today. Both are keyed on an Entra object id, and
        // forgetting one without the other would make this purge's promise half true.
        //
        // Nothing to converge on: a dictionary removal has no second pass, and a concurrent
        // resolution writing a key back is a person who is still signing in, which the membership
        // row's own erasure is what stops.
        var removed = (cache.Forget(user) ? 1 : 0) + (seats.Forget(user) ? 1 : 0);

        return Task.FromResult(PurgeOutcome.Complete(removed));
    }
}
