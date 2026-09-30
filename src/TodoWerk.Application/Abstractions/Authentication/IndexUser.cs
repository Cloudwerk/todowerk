namespace TodoWerk.Application.Abstractions.Authentication;

/// <summary>
/// Whose tasks are being read or written. Passed explicitly rather than resolved from the ambient
/// request, because the work runs in the background where there is no request to resolve it from
/// — and because a job that indexes or rewrites the wrong person's tasks is the worst bug this
/// product could have.
/// <para>
/// Shared rather than owned by a module: the index reads for this user, a Change writes for this
/// user, and the token cache is keyed on exactly these two claims (ADR-0002). Three modules
/// naming the same person three ways would be three chances to key one of them differently.
/// </para>
/// </summary>
/// <param name="TenantId">The Entra ID tenant, from the <c>tid</c> claim.</param>
/// <param name="UserId">The Entra ID object id, from the <c>oid</c> claim.</param>
public sealed record IndexUser(string TenantId, string UserId)
{
    /// <summary>
    /// The signed-in user of the current request, or null when nobody is signed in. Every caller
    /// on the request path goes through here, so the claims a scan is keyed on and the claims the
    /// API authorises against cannot drift apart.
    /// </summary>
    public static IndexUser? From(ICurrentUser currentUser)
    {
        ArgumentNullException.ThrowIfNull(currentUser);

        return currentUser is { IsAuthenticated: true, TenantId: { Length: > 0 } tenantId, ObjectId: { Length: > 0 } objectId }
            ? new IndexUser(tenantId, objectId)
            : null;
    }
}
