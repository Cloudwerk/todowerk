using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using TodoWerk.Infrastructure.Authentication;
using Xunit;

namespace TodoWerk.UnitTests.Authentication;

public sealed class CurrentUserTests
{
    /// <summary>
    /// An Entra ID work account carries both claims. Preferring <c>preferred_username</c> puts an
    /// email address in the header where the person's name belongs.
    /// </summary>
    [Fact]
    public void DisplayName_PrefersTheNameClaimOverTheUpn()
    {
        var currentUser = CreateCurrentUser(
            new Claim("name", "Ada Lovelace"),
            new Claim("preferred_username", "ada@example.test"));

        Assert.Equal("Ada Lovelace", currentUser.DisplayName);
    }

    /// <summary>The OpenID Connect handler rewrites <c>name</c> to a WS-Federation URI by default.</summary>
    [Fact]
    public void DisplayName_WhenTheHandlerMappedTheNameClaim_StillFindsIt()
    {
        var currentUser = CreateCurrentUser(
            new Claim(ClaimTypes.Name, "Ada Lovelace"),
            new Claim(ClaimTypes.Upn, "ada@example.test"));

        Assert.Equal("Ada Lovelace", currentUser.DisplayName);
    }

    [Fact]
    public void DisplayName_WithoutAnyNameClaim_FallsBackToTheUsername()
    {
        var currentUser = CreateCurrentUser(new Claim(ClaimTypes.Upn, "ada@example.test"));

        Assert.Equal("ada@example.test", currentUser.DisplayName);
    }

    private static CurrentUser CreateCurrentUser(params Claim[] claims)
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "Test")),
        };

        return new CurrentUser(new HttpContextAccessor { HttpContext = context });
    }
}
