using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace First10.Infrastructure.Persistence;

public static class First10DataProtectionConfiguration
{
    public static IServiceCollection AddFirst10DataProtection(
        this IServiceCollection services,
        IConfiguration? configuration = null,
        bool requireWrappedKeys = false)
    {
        var builder = services.AddDataProtection()
            .SetApplicationName("First10")
            .PersistKeysToDbContext<First10DbContext>();

        var encodedCertificate = configuration?["Security:DataProtectionWrappingCertificateBase64"];
        if (string.IsNullOrWhiteSpace(encodedCertificate))
        {
            if (requireWrappedKeys)
            {
                throw new InvalidOperationException(
                    "Security:DataProtectionWrappingCertificateBase64 is required outside development/testing. " +
                    "The certificate must be held outside the database whose Data Protection keys it wraps.");
            }

            return services;
        }

        byte[] certificateBytes;
        try
        {
            certificateBytes = Convert.FromBase64String(encodedCertificate);
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException(
                "Security:DataProtectionWrappingCertificateBase64 is not valid base64.",
                exception);
        }

        var password = configuration?["Security:DataProtectionWrappingCertificatePassword"];
        var certificate = X509CertificateLoader.LoadPkcs12(
            certificateBytes,
            password,
            X509KeyStorageFlags.EphemeralKeySet | X509KeyStorageFlags.Exportable);
        if (!certificate.HasPrivateKey)
        {
            certificate.Dispose();
            throw new InvalidOperationException("The Data Protection wrapping certificate requires a private key.");
        }

        builder.ProtectKeysWithCertificate(certificate);
        return services;
    }
}
