using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Bitwarden.Server.Sdk.Http;

/// <summary>
/// TLS for calls between a deployment's own services: the internal certificate authorities a client calling them
/// trusts, and only those.
/// </summary>
/// <remarks>
/// <para>
/// TLS keeps a call between services confidential and unaltered on the network, a Secure Channel.
/// With the tokens the call carries, it is a Trusted Channel. A deployment usually issues its
/// services' certificates from an internal authority that the containers' trust stores don't know.
/// Naming it here trusts it for the clients given <see cref="ChainPolicy"/>, without changing the image.
/// </para>
/// <para>
/// Bound from <see cref="Section"/>: <c>Tls:CertificateAuthorities</c>, PEM files. None is needed on a developer's
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
    /// PEM files of the root certificate authorities a client calling the deployment's own services trusts, in place
    /// of the system's roots. An intermediate authority alone is not enough: name the root it chains to.
    /// </summary>
    /// <remarks>
    /// <c>Tls:CertificateAuthorities</c>. Unset: no client is given an internal authority to trust.
    /// </remarks>
    public string[] CertificateAuthorities { get; set; } = [];

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
    /// The chain policy for a client calling the deployment's own services: it trusts a server's certificate only when
    /// it chains to one of <paramref name="authorities"/>, never to one of the system's roots. Set it as
    /// <see cref="System.Net.Security.SslClientAuthenticationOptions.CertificateChainPolicy"/> on such a client's
    /// <see cref="SocketsHttpHandler.SslOptions"/>, and never on a client that calls anything else, a guarded one among
    /// them.
    /// </summary>
    /// <remarks>
    /// <see cref="System.Net.Security.SslStream"/> still checks that the certificate names the host called, is valid
    /// now and is allowed for server authentication. Revocation is not checked: an internal authority may publish no
    /// revocation list, so a certificate's lifetime is what limits a stolen key's use; keep it short. No certificate is
    /// downloaded, so a server must send the intermediates between its certificate and the authority.
    /// </remarks>
    /// <param name="authorities">The authorities trusted, as <see cref="LoadAuthorities"/> reads them. Not disposed.</param>
    /// <returns>A new policy, for one client's handler.</returns>
    /// <exception cref="ArgumentException"><paramref name="authorities"/> is empty, which would trust nothing.</exception>
    public static X509ChainPolicy ChainPolicy(X509Certificate2Collection authorities)
    {
        ArgumentNullException.ThrowIfNull(authorities);
        if (authorities.Count == 0)
        {
            throw new ArgumentException($"No certificate authority is named in {Section}:CertificateAuthorities.", nameof(authorities));
        }

        var policy = new X509ChainPolicy
        {
            TrustMode = X509ChainTrustMode.CustomRootTrust,
            RevocationMode = X509RevocationMode.NoCheck,
            DisableCertificateDownloads = true,
        };
        policy.CustomTrustStore.AddRange(authorities);
        return policy;
    }
}
