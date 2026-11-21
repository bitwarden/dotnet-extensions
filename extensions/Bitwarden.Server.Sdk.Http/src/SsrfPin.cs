using System.Net;

namespace Bitwarden.Server.Sdk.Http;

/// <summary>
/// The address <see cref="SsrfProtectionHandler"/> checked for one request, which its connection must reach.
/// </summary>
/// <param name="Host">The host the request names, in its ASCII form, or an IP address without brackets.</param>
/// <param name="Port">The port the request names.</param>
/// <param name="Address">The checked address.</param>
/// <param name="Proxied">
/// Whether the request goes through a proxy, and so names <paramref name="Address"/> rather than the host: a proxy
/// would otherwise resolve the name itself.
/// </param>
internal sealed record SsrfPin(string Host, int Port, IPAddress Address, bool Proxied);
