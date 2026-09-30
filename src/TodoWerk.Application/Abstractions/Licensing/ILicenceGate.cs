using TodoWerk.Application.Abstractions.Authentication;

namespace TodoWerk.Application.Abstractions.Licensing;

/// <summary>
/// Whether background work may run for one person. The port the scan claim and the Change claim
/// ask through, so that a denied person's queue stops without either worker naming a Licensing
/// type — the same arrangement erasure uses to reach each module's purge.
/// <para>
/// One boolean and nothing else, deliberately. A worker has no card to render and no problem code
/// to answer with: the only thing it can do with the answer is start the work or leave the row
/// alone, and handing it the kind, the end date and the portal's sentence would be handing it
/// four things it must not act on.
/// </para>
/// </summary>
public interface ILicenceGate
{
    /// <summary>
    /// True when work may start for this person. Answered from the same cache the request path
    /// reads, so a Workbench that is serving and a scan that is refused cannot disagree.
    /// </summary>
    Task<bool> IsLicensedAsync(IndexUser user, CancellationToken cancellationToken);
}
