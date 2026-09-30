using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace TodoWerk.IntegrationTests;

/// <summary>
/// The token a Teams client hands the tab from <c>getAuthToken()</c>: an Entra ID access token for
/// TodoWerk's own Application ID URI, carrying the OpenID scopes and nothing else — never
/// <c>Tasks.ReadWrite</c>, which is the whole reason the exchange behind it can fail for consent.
/// <para>
/// Every axis a test might want to spoil is a parameter, so a refusal can be attributed to the one
/// thing that was wrong rather than to whichever check happened to run first.
/// </para>
/// </summary>
internal static class TeamsSsoToken
{
    private const string Authority =
        $"https://login.microsoftonline.com/{TodoWerkWebApplicationFactory.TenantId}/v2.0";

    internal static string Mint(
        string? audience = null,
        string? issuer = null,
        SigningCredentials? credentials = null,
        string objectId = FakeEntraAndGraphHandler.UserObjectId,
        string tenantId = TodoWerkWebApplicationFactory.TenantId,
        bool withIdentityClaims = true)
    {
        var now = DateTimeOffset.UtcNow;

        var claims = new Dictionary<string, object>
        {
            ["sub"] = "fake-pairwise-subject",
            ["preferred_username"] = "signed-in@todowerk.test",
            ["name"] = "Signed In",
            ["ver"] = "2.0",
            // What Teams actually asks for. The Graph permission is deliberately absent.
            ["scp"] = "access_as_user",
        };

        if (withIdentityClaims)
        {
            claims["oid"] = objectId;
            claims["tid"] = tenantId;
        }

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer ?? Authority,
            Audience = audience ?? TodoWerkWebApplicationFactory.ApplicationIdUri,
            IssuedAt = now.AddMinutes(-1).UtcDateTime,
            NotBefore = now.AddMinutes(-1).UtcDateTime,
            Expires = now.AddHours(1).UtcDateTime,
            Claims = claims,
            SigningCredentials = credentials ?? FakeEntraSigning.Credentials,
        });
    }

    /// <summary>
    /// A key the host was never told to trust, for the one test that is about the signature rather
    /// than about the claims. Generated once for the run, like the real one.
    /// </summary>
    internal static SigningCredentials UntrustedCredentials { get; } = new(
        new RsaSecurityKey(System.Security.Cryptography.RSA.Create(2048)) { KeyId = "not-the-fake-key" },
        SecurityAlgorithms.RsaSha256);
}
