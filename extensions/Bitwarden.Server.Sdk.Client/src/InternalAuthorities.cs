using System.Security.Cryptography.X509Certificates;
using Bitwarden.Server.Sdk.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Bitwarden.Server.Sdk.Client;

/// <summary>
/// The deployment's internal root certificate authorities (<c>Tls:CertificateAuthorities</c>), read once for the
/// process; none when none are named.
/// </summary>
internal sealed class InternalAuthorities
{
    public X509Certificate2Collection Certificates { get; private set; } = new();

    /// <summary>
    /// Reads the named files. A file that can't be read throws, which stops the host as it starts.
    /// </summary>
    internal sealed class FromConfiguration(IConfiguration configuration) : IConfigureOptions<InternalAuthorities>
    {
        // Read by hand rather than bound, as the package is trimmed: Tls:CertificateAuthorities:0, :1, ...
        public void Configure(InternalAuthorities options) =>
            options.Certificates = new InternalTls
            {
                CertificateAuthorities =
                [
                    .. configuration.GetSection($"{InternalTls.Section}:{nameof(InternalTls.CertificateAuthorities)}")
                        .GetChildren()
                        .Select(child => child.Value)
                        .OfType<string>(),
                ],
            }.LoadAuthorities();
    }
}
