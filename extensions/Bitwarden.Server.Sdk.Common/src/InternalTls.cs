using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Bitwarden.Server.Sdk.Common;

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
/// <c>Tls:RequireHttps</c>. A host applies them to its calls. Neither is needed on a developer's
/// machine, where services can speak plain HTTP inside one network.
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
    public string[] CertificateAuthorities { get; set; } = [];

    /// <summary>
    /// Whether calls to other services must use <c>https://</c>, so a host refuses a plain address.
    /// </summary>
    public bool RequireHttps { get; set; }

    private const string ServerAuthentication = "1.3.6.1.5.5.7.3.1";

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
    /// Whether a server's certificate is trusted, by the system or else by a chain to one of
    /// <paramref name="authorities"/>. The name must match either way, and a certificate from one of
    /// the authorities must be valid now and allowed for server authentication. Revocation is checked
    /// as the system's chain checks it, so without that chain nothing is trusted beyond the system.
    /// </summary>
    /// <param name="authorities">The certificate authorities trusted besides the system's.</param>
    /// <param name="certificate">The server's certificate.</param>
    /// <param name="chain">The chain the system built, whose certificates and revocation settings are reused.</param>
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

        if (errors != SslPolicyErrors.RemoteCertificateChainErrors || certificate is null || chain is null || authorities.Count == 0)
        {
            return false;
        }

        using var custom = new X509Chain();
        custom.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        custom.ChainPolicy.CustomTrustStore.AddRange(authorities);
        custom.ChainPolicy.ApplicationPolicy.Add(new Oid(ServerAuthentication));
        custom.ChainPolicy.RevocationMode = chain.ChainPolicy.RevocationMode;
        custom.ChainPolicy.RevocationFlag = chain.ChainPolicy.RevocationFlag;

        foreach (var element in chain.ChainElements)
        {
            custom.ChainPolicy.ExtraStore.Add(element.Certificate);
        }

        using var server = new X509Certificate2(certificate);
        return custom.Build(server);
    }
}
