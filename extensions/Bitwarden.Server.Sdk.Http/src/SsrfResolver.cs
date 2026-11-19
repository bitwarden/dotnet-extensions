using System.Net;

namespace Bitwarden.Server.Sdk.Http;

/// <summary>
/// Resolves the names <see cref="SsrfProtectionHandler"/> checks: the system's DNS, which a test replaces.
/// </summary>
internal class SsrfResolver
{
    /// <summary>
    /// The addresses <paramref name="host"/> resolves to. Throws <see cref="System.Net.Sockets.SocketException"/> for a
    /// name that can't be resolved, and <see cref="ArgumentOutOfRangeException"/> for one longer than DNS allows.
    /// </summary>
    public virtual Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken) =>
        Dns.GetHostAddressesAsync(host, cancellationToken);
}
