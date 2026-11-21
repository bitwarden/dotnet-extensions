using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Bitwarden.Server.Sdk.Http;

/// <summary>
/// TLS between a deployment's own services: which certificate authorities a service trusts for its
/// calls besides the system's, and whether it may call anything over plain HTTP.
/// </summary>
/// <remarks>
/// <para>
/// TLS keeps a call between services confidential and unaltered on the network, a Secure Channel.
/// With the tokens the call carries, it is a Trusted Channel. A deployment usually issues its
/// services' certificates from an internal authority that the containers' trust stores don't know.
/// Naming it here trusts it for these calls without changing the image.
/// </para>
/// <para>
/// Bound from <see cref="Section"/>: <c>Tls:CertificateAuthorities</c>, PEM files, and
/// <c>Tls:RequireHttps</c>, which a host applies to its calls; and, for what it serves,
/// <c>Tls:ServerCertificate</c> with <c>Tls:ServerKey</c> and <c>Tls:HealthPort</c>. With
/// <c>Tls:RequireHttps</c> a host also refuses to listen on plain HTTP anywhere but the health port.
/// None is needed on a developer's machine, where services can speak plain HTTP inside one network.
/// </para>
/// </remarks>
public sealed class InternalTls
{
    /// <summary>
    /// The configuration section this binds from.
    /// </summary>
    public const string Section = "Tls";

    /// <summary>
    /// PEM files of the root certificate authorities trusted for calls, besides the system's roots.
    /// An intermediate authority alone is not enough: name the root it chains to.
    /// </summary>
    /// <remarks>
    /// <c>Tls:CertificateAuthorities</c>. Unset: only the system's roots are trusted.
    /// </remarks>
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

    private const string _serverAuthentication = "1.3.6.1.5.5.7.3.1";

    /// <summary>
    /// Reads every certificate in every named file.
    /// </summary>
    /// <returns>The certificates, none when no file is named. The caller disposes them.</returns>
    /// <exception cref="InvalidOperationException">A file is missing, can't be read or holds no certificate.</exception>
    public X509Certificate2Collection LoadAuthorities()
    {
        var authorities = new X509Certificate2Collection();

        foreach (var path in CertificateAuthorities.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            var before = authorities.Count;

            try
            {
                authorities.ImportFromPemFile(path);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or CryptographicException or ArgumentException)
            {
                throw new InvalidOperationException($"{Section}:CertificateAuthorities names '{path}', which can't be read as PEM certificates.", exception);
            }

            if (authorities.Count == before)
            {
                throw new InvalidOperationException($"{Section}:CertificateAuthorities names '{path}', which holds no certificate.");
            }
        }

        return authorities;
    }

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
            throw new InvalidOperationException($"{Section}:ServerCertificate and {Section}:ServerKey go together: set both, or neither.");
        }

        X509Certificate2 certificate;
        try
        {
            certificate = X509CertificateLoader.LoadCertificate(Convert.FromBase64String(ServerCertificate!.Trim()));
        }
        catch (Exception exception) when (exception is FormatException or CryptographicException)
        {
            throw new InvalidOperationException($"{Section}:ServerCertificate can't be read as a base64 DER certificate.", exception);
        }

        using (certificate)
        {
            var now = DateTime.Now;
            if (now < certificate.NotBefore || now > certificate.NotAfter)
            {
                throw new InvalidOperationException($"{Section}:ServerCertificate is not valid now: from {certificate.NotBefore:O} to {certificate.NotAfter:O}.");
            }

            byte[] key;
            try
            {
                key = Convert.FromBase64String(ServerKey!.Trim());
            }
            catch (FormatException exception)
            {
                throw new InvalidOperationException($"{Section}:ServerKey can't be read as a base64 PKCS #8 private key.", exception);
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
                throw new InvalidOperationException($"{Section}:ServerKey can't be read as a base64 PKCS #8 private key of {Section}:ServerCertificate.", exception);
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
                throw new InvalidOperationException($"{Section}:ServerCertificate's key is neither ECDSA nor RSA.");
        }
    }

    /// <summary>
    /// Whether a server's certificate is trusted, by the system or else by a chain to one of
    /// <paramref name="authorities"/>. The name must match either way, and a certificate from one of
    /// the authorities must be valid now and allowed for server authentication. Revocation is checked,
    /// and certificates are downloaded, as the system's chain does it, so without that chain nothing is
    /// trusted beyond the system.
    /// </summary>
    /// <param name="authorities">The certificate authorities trusted besides the system's.</param>
    /// <param name="certificate">The server's certificate.</param>
    /// <param name="chain">The chain the system built, whose certificates, revocation and download settings are reused.</param>
    /// <param name="errors">What the system found wrong.</param>
    public static bool Trusts(
        X509Certificate2Collection authorities,
        X509Certificate? certificate,
        X509Chain? chain,
        SslPolicyErrors errors)
    {
        ArgumentNullException.ThrowIfNull(authorities);

        if (errors == SslPolicyErrors.None)
        {
            return true;
        }

        if (errors != SslPolicyErrors.RemoteCertificateChainErrors)
        {
            return false;
        }

        if (certificate is null || chain is null || authorities.Count == 0)
        {
            return false;
        }

        using var custom = new X509Chain();
        custom.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        custom.ChainPolicy.CustomTrustStore.AddRange(authorities);
        custom.ChainPolicy.ApplicationPolicy.Add(new Oid(_serverAuthentication));
        custom.ChainPolicy.RevocationMode = chain.ChainPolicy.RevocationMode;
        custom.ChainPolicy.RevocationFlag = chain.ChainPolicy.RevocationFlag;
        // Downloads too: where the system's chain may not fetch an issuer or a revocation list, or may wait only so long
        // for one, neither may this chain.
        custom.ChainPolicy.DisableCertificateDownloads = chain.ChainPolicy.DisableCertificateDownloads;
        custom.ChainPolicy.UrlRetrievalTimeout = chain.ChainPolicy.UrlRetrievalTimeout;

        foreach (var element in chain.ChainElements)
        {
            custom.ChainPolicy.ExtraStore.Add(element.Certificate);
        }

        using var server = new X509Certificate2(certificate);
        return custom.Build(server);
    }
}
