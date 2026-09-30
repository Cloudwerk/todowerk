using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace TodoWerk.Infrastructure.Authentication;

public static class DataProtectionServiceCollectionExtensions
{
    private const string KeyRingPathKey = "DataProtection:KeyRingPath";

    private const string CertificatePathKey = "DataProtection:CertificatePath";

    private const string CertificatePasswordKey = "DataProtection:CertificatePassword";

    private const string PreviousCertificatePathKey = "DataProtection:PreviousCertificatePath";

    private const string PreviousCertificatePasswordKey = "DataProtection:PreviousCertificatePassword";

    /// <summary>
    /// Persists the Data Protection key ring outside the instance, and encrypts it at rest.
    /// Session cookies are encrypted with these keys, so an instance-local key ring logs every
    /// user out on each deploy (ADR-0002) — and the MSAL token cache is encrypted with the same
    /// keys, so the key ring path is a secret store, not a scratch directory: written plain, the
    /// master keys it holds are every user's Graph refresh token and the ability to mint any
    /// session cookie. Outside Development a certificate is therefore required, not suggested.
    /// </summary>
    public static IServiceCollection AddTodoWerkDataProtection(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var keyRingPath = configuration[KeyRingPathKey];

        var builder = services.AddDataProtection()
            .SetApplicationName("TodoWerk");

        if (!string.IsNullOrWhiteSpace(keyRingPath))
        {
            builder.PersistKeysToFileSystem(Directory.CreateDirectory(keyRingPath));

            var certificatePath = configuration[CertificatePathKey];

            if (!string.IsNullOrWhiteSpace(certificatePath))
            {
                var certificate = X509CertificateLoader.LoadPkcs12FromFile(
                    certificatePath,
                    configuration[CertificatePasswordKey]);

                builder.ProtectKeysWithCertificate(certificate);

                // New keys are encrypted with the current certificate; existing ones were
                // encrypted with whatever was current when they were created. Without naming the
                // outgoing certificate here, rotating it makes every stored key undecryptable —
                // which signs every user out and empties the token cache, the two things
                // persisting the key ring exists to prevent.
                var previousPath = configuration[PreviousCertificatePathKey];

                builder.UnprotectKeysWithAnyCertificate(string.IsNullOrWhiteSpace(previousPath)
                    ? [certificate]
                    : [
                        certificate,
                        X509CertificateLoader.LoadPkcs12FromFile(
                            previousPath,
                            configuration[PreviousCertificatePasswordKey]),
                    ]);
            }
            else if (!environment.IsDevelopment())
            {
                throw new InvalidOperationException(
                    $"{CertificatePathKey} must be configured outside Development. The key ring "
                    + "encrypts the session cookies and the Graph token cache; persisted without "
                    + "its own encryption, those master keys sit on disk in plain XML.");
            }

            return services;
        }

        if (!environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                $"{KeyRingPathKey} must be configured outside Development. Without a persisted key "
                + "ring, every deploy invalidates all session cookies.");
        }

        // Development falls back to the per-user local key ring, which survives restarts.
        return services;
    }
}
