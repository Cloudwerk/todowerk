using TodoWerk.Application.Abstractions.Authentication;
using TodoWerk.Domain.Licensing;

namespace TodoWerk.Application.Licensing;

/// <summary>
/// The one place that answers what a person holds. Everything that needs to know asks here — the
/// filter in front of every authenticated endpoint, the Licence endpoint the client reads once a
/// session, and the gate the background claims go through — so a single cached answer decides all
/// three and they cannot contradict each other about the same person.
/// <para>
/// Implemented in the Licensing module's Infrastructure half, over the First-Party Path. A
/// deployment with no <c>Licensing</c> section registers an implementation that asks nobody and
/// always answers <see cref="LicenceResolution.NoAuthority"/>.
/// </para>
/// </summary>
public interface ILicenceResolver
{
    /// <summary>
    /// What this person holds, from cache when the cached answer is still fresh enough and from
    /// the portal otherwise. Never throws for an unreachable portal: unreachability is one of the
    /// answers, not an exception the caller has to know how to read.
    /// </summary>
    Task<LicenceResolution> ResolveAsync(IndexUser user, CancellationToken cancellationToken);
}
