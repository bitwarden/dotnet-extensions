using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Bitwarden.Server.Sdk.Http;

namespace Bitwarden.Server.Sdk.Service;

/// <summary>
/// TLS as a service host applies it, bound from <see cref="InternalTls.Section"/>: the internal certificate authorities
/// its bearer handler trusts for Identity's keys, whether it refuses plain HTTP, and what it serves.
/// </summary>
/// <remarks>
/// <para>
/// <c>Tls:CertificateAuthorities</c> is <see cref="InternalTls"/>'s, read the same way. The rest is the host's own:
/// <c>Tls:RequireHttps</c>, <c>Tls:ServerCertificate</c> with <c>Tls:ServerKey</c>, and <c>Tls:HealthPort</c>. None is
/// needed on a developer's machine, where services can speak plain HTTP inside one network.
/// </para>
/// </remarks>
internal sealed class ServiceTls
{
    /// <summary>
    /// PEM files of the internal root certificate authorities: <see cref="InternalTls.CertificateAuthorities"/>.
    /// </summary>
    public string[] CertificateAuthorities { get; set; } = [];

    /// <summary>
    /// Whether calls to other services must use <c>https://</c>, so a host refuses a plain address,
    /// and whether the host itself may listen only on <c>https://</c>, but for <see cref="HealthPort"/>.
    /// </summary>
    /// <remarks>
    /// <c>Tls:RequireHttps</c>. Unset: plain addresses are allowed.
    /// </remarks>
    public bool RequireHttps { get; set; }

    /// <summary>
    /// The host's server certificate, base64 of its DER encoding, for a deployment that hands a service
    /// its secrets as settings rather than files. It must name the host the other services call it by.
    /// </summary>
    /// <remarks>
    /// <c>Tls:ServerCertificate</c>, with <see cref="ServerKey"/>. Unset: the host serves HTTPS with
    /// ASP.NET Core's own Kestrel settings (<c>Kestrel:Certificates:Default:Path</c> and <c>:KeyPath</c>,
    /// or an endpoint's own), which is also how to serve a chain through an intermediate authority.
    /// </remarks>
    public string? ServerCertificate { get; set; }

    /// <summary>
    /// The private key of <see cref="ServerCertificate"/>, base64 of its PKCS #8 encoding. A secret.
    /// </summary>
    /// <remarks>
    /// <c>Tls:ServerKey</c>.
    /// </remarks>
    public string? ServerKey { get; set; }

    /// <summary>
    /// The one port a host may listen on in plain HTTP with <see cref="RequireHttps"/> set, for
    /// infrastructure that probes it without TLS. On that port the host answers its probes only.
    /// </summary>
    /// <remarks>
    /// <c>Tls:HealthPort</c>. Its listener is configured as any other (<c>ASPNETCORE_URLS</c>, such as
    /// <c>https://+:8443;http://127.0.0.1:8081</c>); keep it on an address only the probing
    /// infrastructure reaches. Unset: no plain port is allowed.
    /// </remarks>
    public int? HealthPort { get; set; }

    /// <summary>
    /// Reads <see cref="ServerCertificate"/> and <see cref="ServerKey"/>.
    /// </summary>
    /// <returns>The certificate with its key, or <see langword="null"/> when neither is set. The caller disposes it.</returns>
    /// <exception cref="InvalidOperationException">
    /// Only one is set, either can't be read, the key isn't the certificate's, or the certificate isn't valid now.
    /// The message names the settings, never a value.
    /// </exception>
    public X509Certificate2? LoadServerCertificate()
    {
        var hasCertificate = !string.IsNullOrWhiteSpace(ServerCertificate);
        var hasKey = !string.IsNullOrWhiteSpace(ServerKey);
        if (!hasCertificate && !hasKey)
        {
            return null;
        }

        if (!hasCertificate || !hasKey)
        {
            throw new InvalidOperationException($"{InternalTls.Section}:ServerCertificate and {InternalTls.Section}:ServerKey go together: set both, or neither.");
        }

        X509Certificate2 certificate;
        try
        {
            certificate = X509CertificateLoader.LoadCertificate(Convert.FromBase64String(ServerCertificate!.Trim()));
        }
        catch (Exception exception) when (exception is FormatException or CryptographicException)
        {
            throw new InvalidOperationException($"{InternalTls.Section}:ServerCertificate can't be read as a base64 DER certificate.", exception);
        }

        using (certificate)
        {
            var now = DateTime.Now;
            if (now < certificate.NotBefore || now > certificate.NotAfter)
            {
                throw new InvalidOperationException($"{InternalTls.Section}:ServerCertificate is not valid now: from {certificate.NotBefore:O} to {certificate.NotAfter:O}.");
            }

            byte[] key;
            try
            {
                key = Convert.FromBase64String(ServerKey!.Trim());
            }
            catch (FormatException exception)
            {
                throw new InvalidOperationException($"{InternalTls.Section}:ServerKey can't be read as a base64 PKCS #8 private key.", exception);
            }

            byte[]? pkcs12 = null;
            try
            {
                using var withKey = WithKey(certificate, key);
                // Exported and read back, so the key is one the platform's TLS can use on every operating system: an
                // ephemeral key, as CopyWithPrivateKey makes, isn't usable by SslStream on Windows.
                pkcs12 = withKey.Export(X509ContentType.Pkcs12);
                return X509CertificateLoader.LoadPkcs12(pkcs12, null);
            }
            catch (Exception exception) when (exception is CryptographicException or ArgumentException)
            {
                throw new InvalidOperationException($"{InternalTls.Section}:ServerKey can't be read as a base64 PKCS #8 private key of {InternalTls.Section}:ServerCertificate.", exception);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
                if (pkcs12 is not null)
                {
                    CryptographicOperations.ZeroMemory(pkcs12);
                }
            }
        }
    }

    private static X509Certificate2 WithKey(X509Certificate2 certificate, byte[] pkcs8)
    {
        switch (certificate.GetKeyAlgorithm())
        {
            case "1.2.840.10045.2.1":
                using (var ecdsa = ECDsa.Create())
                {
                    ecdsa.ImportPkcs8PrivateKey(pkcs8, out _);
                    return certificate.CopyWithPrivateKey(ecdsa);
                }

            case "1.2.840.113549.1.1.1":
                using (var rsa = RSA.Create())
                {
                    rsa.ImportPkcs8PrivateKey(pkcs8, out _);
                    return certificate.CopyWithPrivateKey(rsa);
                }

            default:
                throw new InvalidOperationException($"{InternalTls.Section}:ServerCertificate's key is neither ECDSA nor RSA.");
        }
    }

    /// <summary>
    /// Reads every certificate in every file <see cref="CertificateAuthorities"/> names, as <see cref="InternalTls"/> does.
    /// </summary>
    /// <returns>The certificates, none when no file is named. The caller disposes them.</returns>
    /// <exception cref="InvalidOperationException">A file is missing, can't be read or holds no certificate.</exception>
    public X509Certificate2Collection LoadAuthorities() =>
        new InternalTls { CertificateAuthorities = CertificateAuthorities }.LoadAuthorities();
}
