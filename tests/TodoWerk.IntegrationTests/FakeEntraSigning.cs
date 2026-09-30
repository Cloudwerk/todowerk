using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace TodoWerk.IntegrationTests;

/// <summary>
/// The key the fake Entra ID signs its id tokens with, and whose public half the application's
/// OpenID Connect handler is told to trust.
/// <para>
/// The token-cache tests never needed one: MSAL parses an id token for its claims and does not
/// validate the signature — a confidential client trusts the TLS channel to the token endpoint —
/// so a junk signature was enough. The OpenID Connect middleware does validate, so a sign-in
/// cannot be walked end to end without a key both halves agree on.
/// </para>
/// </summary>
internal static class FakeEntraSigning
{
    /// <summary>Named in the token header and in the trusted key, because that is how the two are matched.</summary>
    internal const string KeyId = "fake-signing-key";

    /// <summary>
    /// One key pair for the whole test run, exported as parameters rather than held as a live
    /// <see cref="RSA"/>: the struct needs no disposal and no ownership rules, and every
    /// <see cref="RsaSecurityKey"/> built from it below can be created and dropped freely.
    /// </summary>
    private static readonly RSAParameters KeyPair = CreateKeyPair();

    /// <summary>What the handler validates with — the public half only, as a real JWKS serves.</summary>
    internal static SecurityKey PublicKey { get; } = new RsaSecurityKey(
        new RSAParameters { Modulus = KeyPair.Modulus, Exponent = KeyPair.Exponent })
    {
        KeyId = KeyId,
    };

    /// <summary>What the fake token endpoint signs with.</summary>
    internal static SigningCredentials Credentials { get; } = new(
        new RsaSecurityKey(KeyPair) { KeyId = KeyId },
        SecurityAlgorithms.RsaSha256);

    private static RSAParameters CreateKeyPair()
    {
        using var rsa = RSA.Create(2048);

        return rsa.ExportParameters(includePrivateParameters: true);
    }
}
