namespace TodoWerk.Application.Abstractions.Authentication;

/// <summary>The signed-in user of the current request.</summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    /// <summary>Entra ID object id (the <c>oid</c> claim).</summary>
    string? ObjectId { get; }

    /// <summary>Entra ID tenant id (the <c>tid</c> claim).</summary>
    string? TenantId { get; }

    string? DisplayName { get; }

    string? Username { get; }
}
