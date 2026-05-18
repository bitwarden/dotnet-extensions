using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Bitwarden.Server.Sdk.Licensing;

/// <summary>
/// Provides access to the public certificate used to validate license signatures.
/// </summary>
public interface IIssuerCertificateProvider
{
    /// <summary>
    /// Returns the issuer certificate.
    /// </summary>
    /// <returns>The X509 certificate (public key only) used to validate licenses.</returns>
    X509Certificate2 Get();
}

internal sealed class IssuerCertificateProvider : IIssuerCertificateProvider
{
    private readonly IHostEnvironment _hostEnvironment;
    private readonly StaticLicensingOptions _licensingOptions;

    private readonly Lazy<X509Certificate2> _certificate;

    public IssuerCertificateProvider(IHostEnvironment hostEnvironment, StaticLicensingOptions licensingOptions)
    {
        _hostEnvironment = hostEnvironment;
        _licensingOptions = licensingOptions;
        _certificate = new Lazy<X509Certificate2>(GetCore);
    }

    public X509Certificate2 Get()
    {
        return _certificate.Value;
    }

    private X509Certificate2 GetCore()
    {
        var assembly = Assembly.Load(_hostEnvironment.ApplicationName);
        var thumbprint = _licensingOptions.GetThumbprint(_hostEnvironment.EnvironmentName);

        var fullCertificateResourceName = $"{assembly.GetName().Name}.Licensing.{thumbprint}.cer";

        using var file = assembly.GetManifestResourceStream(fullCertificateResourceName)
            ?? throw new InvalidOperationException($"Missing resource by name: {fullCertificateResourceName}");

        using var ms = new MemoryStream();
        file.CopyTo(ms);
        return new X509Certificate2(ms.ToArray());
    }
}
