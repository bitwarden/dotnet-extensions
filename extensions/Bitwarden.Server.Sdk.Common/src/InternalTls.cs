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
/// <c>Tls:RequireHttps</c>. Neither is needed on a developer's machine, where services can speak
/// plain HTTP inside one network.
/// </para>
/// </remarks>
public sealed class InternalTls
{
    /// <summary>
    /// The configuration section this binds from.
    /// </summary>
    public const string Section = "Tls";

    /// <summary>
    /// PEM files of the certificate authorities trusted for calls, besides the system's roots.
    /// </summary>
    public string[] CertificateAuthorities { get; set; } = [];

    /// <summary>
    /// Whether a service refuses to start with a plain <c>http://</c> address for another service or Identity.
    /// </summary>
    public bool RequireHttps { get; set; }

    /// <summary>
    /// Reads every certificate in every named file.
    /// </summary>
    /// <returns>The certificates, none when no file is named.</returns>
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
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or CryptographicException)
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
    /// <paramref name="authorities"/>. The name must match either way.
    /// </summary>
    /// <param name="authorities">The certificate authorities trusted besides the system's.</param>
    /// <param name="certificate">The server's certificate.</param>
    /// <param name="chain">The chain the system built, whose extra certificates are reused.</param>
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

        if (errors != SslPolicyErrors.RemoteCertificateChainErrors || certificate is null || authorities.Count == 0)
        {
            return false;
        }

        using var custom = new X509Chain();
        custom.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        custom.ChainPolicy.CustomTrustStore.AddRange(authorities);
        custom.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;

        if (chain is not null)
        {
            foreach (var element in chain.ChainElements)
            {
                custom.ChainPolicy.ExtraStore.Add(element.Certificate);
            }
        }

        using var server = new X509Certificate2(certificate);
        return custom.Build(server);
    }
}
