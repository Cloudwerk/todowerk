namespace TodoWerk.Application.Onboarding;

/// <summary>
/// Whether a Tenant Consent grant has been recorded for a tenant, and the one write that records
/// one.
/// </summary>
public interface ITenantConsentStore
{
    Task<bool> IsGrantedAsync(string tenantId, CancellationToken cancellationToken);

    /// <summary>
    /// Records that an administrator approved TodoWerk for this tenant. Idempotent: a second
    /// approval — a second administrator, or the same one returning to the callback URL — leaves
    /// the first row and its moment alone.
    /// </summary>
    Task RecordGrantAsync(string tenantId, CancellationToken cancellationToken);
}
