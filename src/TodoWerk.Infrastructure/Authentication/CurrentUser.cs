using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Identity.Web;
using TodoWerk.Application.Abstractions.Authentication;

namespace TodoWerk.Infrastructure.Authentication;

internal sealed class CurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => httpContextAccessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated ?? false;

    public string? ObjectId => Principal?.GetObjectId();

    public string? TenantId => Principal?.GetTenantId();

    /// <summary>
    /// The person's name, from the <c>name</c> claim. Not Microsoft.Identity.Web's
    /// <c>GetDisplayName()</c>: that prefers <c>preferred_username</c>, which on a work account is
    /// the UPN, so the header rendered an email address where a name belongs. Both claim types are
    /// tried because the OpenID Connect handler rewrites inbound short names to the
    /// WS-Federation URIs while <c>MapInboundClaims</c> is on.
    /// </summary>
    public string? DisplayName =>
        Principal?.FindFirstValue("name")
        ?? Principal?.FindFirstValue(ClaimTypes.Name)
        ?? Username;

    public string? Username => Principal?.GetLoginHint() ?? Principal?.FindFirstValue(ClaimTypes.Upn);
}
