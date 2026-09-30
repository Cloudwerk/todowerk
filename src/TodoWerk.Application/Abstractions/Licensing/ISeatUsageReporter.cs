using TodoWerk.Application.Abstractions.Authentication;

namespace TodoWerk.Application.Abstractions.Licensing;

/// <summary>
/// Reports that a person is here, on an interactive sign-in. A figure and a reconnect signal,
/// never a gate: a Tenant Licence is unlimited and a Personal Licence is one person, so there is
/// nothing for a count to enforce (ADR-0012).
/// <para>
/// A port in the shared layer for the same reason <see cref="ILicenceGate"/> is one: the two
/// places a human signs in — the OpenID Connect callback and the Teams tab's token exchange — are
/// authentication code, and neither has any business naming a Licensing type.
/// </para>
/// </summary>
public interface ISeatUsageReporter
{
    /// <summary>
    /// Reports this person's arrival, at most once per person per day. Never throws and never
    /// waits on the portal: a sign-in that failed because a usage figure could not be filed would
    /// be a product refusing to work over its own bookkeeping.
    /// </summary>
    Task ReportSignInAsync(IndexUser user, CancellationToken cancellationToken);
}
