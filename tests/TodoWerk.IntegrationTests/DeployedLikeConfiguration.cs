using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace TodoWerk.IntegrationTests;

/// <summary>
/// The two settings a host refuses to start without once it is not Development: a Data Protection
/// key ring, and a certificate to encrypt it with. Both are deliberate refusals — the key ring
/// holds every session cookie's key and every Graph refresh token — so a test that wants to watch
/// the deployed behaviour has to satisfy them rather than switch them off.
/// <para>
/// The certificate is self-signed and lives for the length of the test run. Nothing validates it:
/// Data Protection uses it to encrypt the key ring and nothing else, so a chain nobody trusts is
/// exactly as good as a real one here.
/// </para>
/// </summary>
internal sealed class DeployedLikeConfiguration : IDisposable
{
    private const string CertificatePassword = "todowerk-tests";

    private readonly string _directory;

    public DeployedLikeConfiguration()
    {
        _directory = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(),
            "todowerk-deployed-tests",
            Guid.NewGuid().ToString("N"))).FullName;

        var certificatePath = Path.Combine(_directory, "key-ring.pfx");

        using (var key = RSA.Create(2048))
        {
            var request = new CertificateRequest(
                "CN=TodoWerk integration tests",
                key,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);

            using var certificate = request.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddDays(-1),
                DateTimeOffset.UtcNow.AddDays(1));

            File.WriteAllBytes(certificatePath, certificate.Export(X509ContentType.Pkcs12, CertificatePassword));
        }

        Settings = new Dictionary<string, string?>
        {
            ["DataProtection:KeyRingPath"] = Path.Combine(_directory, "keys"),
            ["DataProtection:CertificatePath"] = certificatePath,
            ["DataProtection:CertificatePassword"] = CertificatePassword,
        };
    }

    /// <summary>Configuration overrides to hand to the factory before it boots.</summary>
    public IReadOnlyDictionary<string, string?> Settings { get; }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A straggling handle on a key file is not worth failing the test run over.
        }
    }
}
