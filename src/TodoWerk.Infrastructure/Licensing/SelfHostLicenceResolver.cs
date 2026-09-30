using TodoWerk.Application.Abstractions.Authentication;
using TodoWerk.Application.Abstractions.Licensing;
using TodoWerk.Application.Licensing;
using TodoWerk.Domain.Licensing;

namespace TodoWerk.Infrastructure.Licensing;

/// <summary>
/// What a Self-Host resolves with: nothing. Registered in place of the portal resolver when the
/// <c>Licensing</c> section is absent, so that "makes no call to any licensing endpoint" is a fact
/// about which services exist rather than a branch somebody could forget to write.
/// <para>
/// It is why the absent section is checked once at startup and never again: there is no portal
/// client registered for anything to accidentally reach for, and nothing here to configure wrong.
/// </para>
/// </summary>
internal sealed class SelfHostLicenceResolver : ILicenceResolver, ILicenceGate
{
    public Task<LicenceResolution> ResolveAsync(IndexUser user, CancellationToken cancellationToken) =>
        Task.FromResult(LicenceResolution.NoAuthority);

    public Task<bool> IsLicensedAsync(IndexUser user, CancellationToken cancellationToken) =>
        Task.FromResult(true);
}

/// <summary>
/// What a Self-Host reports: nothing, to nobody. The counterpart to
/// <see cref="SelfHostLicenceResolver"/> on the seat-metering side.
/// </summary>
internal sealed class SelfHostSeatUsageReporter : ISeatUsageReporter
{
    public Task ReportSignInAsync(IndexUser user, CancellationToken cancellationToken) => Task.CompletedTask;
}
