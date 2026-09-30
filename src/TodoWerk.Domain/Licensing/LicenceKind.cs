namespace TodoWerk.Domain.Licensing;

/// <summary>
/// Who a Licence covers. Never how much was paid and never a feature set: the software is
/// identical everywhere, and the only thing that differs is whom the right to run the Hosted
/// Service belongs to
/// ([ADR-0012](../../../docs/adr/0012-three-kinds-of-licence-resolved-per-person.md)).
/// <para>
/// The names are ManagementPortal's <c>kind</c> field in TodoWerk's own casing, and the mapping
/// between the two lives in the Infrastructure client rather than here — a wire vocabulary that
/// happened to match today would otherwise be a wire vocabulary this enum had silently frozen.
/// </para>
/// </summary>
public enum LicenceKind
{
    /// <summary>Everybody in the tenant, with no cap on how many.</summary>
    Tenant = 0,

    /// <summary>One person, keyed by their tenant and their Entra object id.</summary>
    Personal = 1,

    /// <summary>
    /// One person's time-limited term, set by the licensing service and issued on their first
    /// arrival and never twice. TodoWerk carries no trial clock: this is a kind it is told about,
    /// not one it grants.
    /// </summary>
    Trial = 2,
}
