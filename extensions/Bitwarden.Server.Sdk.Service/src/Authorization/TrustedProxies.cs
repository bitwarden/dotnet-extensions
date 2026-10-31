using System.Net;
using Microsoft.Extensions.Configuration;

namespace Bitwarden.Server.Sdk.Service;

/// <summary>
/// The proxies whose word a service takes for the address a request came from.
/// </summary>
/// <remarks>
/// <para>
/// <c>ForwardedHeaders:KnownProxies</c> names them, addresses or host names separated by commas
/// (a proxy container named <c>nginx</c>, for example), and
/// <c>ForwardedHeaders:KnownNetworks</c> names networks of them in CIDR notation. The loopback
/// addresses are always trusted, as ASP.NET's own forwarded headers trust them: a process on the
/// same host is already inside.
/// </para>
/// <para>
/// A host name is looked up when a request comes from an address not otherwise trusted, and again
/// at most every <see cref="Refresh"/>, so a proxy that starts after the service, or comes back
/// at another address, is trusted without restarting the service. A name that can't be looked up
/// trusts nobody until the next look.
/// </para>
/// </remarks>
internal sealed class TrustedProxies
{
    /// <summary>
    /// The configuration section, named as ASP.NET's forwarded-header options are.
    /// </summary>
    internal const string Section = "ForwardedHeaders";

    /// <summary>
    /// How long a lookup of the proxies' names is taken as true.
    /// </summary>
    internal static readonly TimeSpan Refresh = TimeSpan.FromSeconds(30);

    private readonly IPAddress[] _addresses;
    private readonly System.Net.IPNetwork[] _networks;
    private readonly string[] _names;
    private readonly TimeProvider _time;
    private readonly Func<string, CancellationToken, Task<IPAddress[]>> _lookUp;
    private readonly SemaphoreSlim _looking = new(1, 1);
    private volatile Looked _looked = new([], DateTimeOffset.MinValue);

    /// <param name="configuration">Names the proxies.</param>
    /// <param name="time">Says when a lookup is stale.</param>
    public TrustedProxies(IConfiguration configuration, TimeProvider time)
        : this(configuration, time, Dns.GetHostAddressesAsync)
    {
    }

    /// <param name="configuration">Names the proxies.</param>
    /// <param name="time">Says when a lookup is stale.</param>
    /// <param name="lookUp">Looks a host name up.</param>
    internal TrustedProxies(
        IConfiguration configuration,
        TimeProvider time,
        Func<string, CancellationToken, Task<IPAddress[]>> lookUp)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var proxies = Split(configuration[$"{Section}:KnownProxies"]);

        _addresses = [.. proxies
            .Select(proxy => IPAddress.TryParse(proxy, out var address) ? Canonical(address) : null)
            .OfType<IPAddress>()];
        _names = [.. proxies.Where(proxy => !IPAddress.TryParse(proxy, out _))];
        _networks = [.. Split(configuration[$"{Section}:KnownNetworks"])
            .Select(network => System.Net.IPNetwork.TryParse(network, out var parsed) ? parsed : (System.Net.IPNetwork?)null)
            .OfType<System.Net.IPNetwork>()];
        _time = time;
        _lookUp = lookUp;
    }

    /// <summary>
    /// Whether a request from this address may say where it was forwarded from.
    /// </summary>
    /// <param name="peer">The address the connection came from.</param>
    /// <param name="cancellationToken">Cancels a lookup.</param>
    public async ValueTask<bool> TrustsAsync(IPAddress peer, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(peer);

        peer = Canonical(peer);

        if (IPAddress.IsLoopback(peer) || _addresses.Contains(peer) || _networks.Any(network => network.Contains(peer)))
        {
            return true;
        }

        if (_names.Length == 0)
        {
            return false;
        }

        var looked = _looked;

        // A stale lookup is repeated even for an address it found: a proxy that moved left that address to be given
        // to someone else.
        if (_time.GetUtcNow() - looked.At < Refresh)
        {
            return looked.Addresses.Contains(peer);
        }

        return (await LookUpAsync(cancellationToken)).Addresses.Contains(peer);
    }

    /// <summary>
    /// An IPv4 address carried in IPv6 is the IPv4 address it carries.
    /// </summary>
    internal static IPAddress Canonical(IPAddress address) =>
        address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;

    private async Task<Looked> LookUpAsync(CancellationToken cancellationToken)
    {
        await _looking.WaitAsync(cancellationToken);

        try
        {
            // Somebody else may have looked while this waited.
            if (_time.GetUtcNow() - _looked.At < Refresh)
            {
                return _looked;
            }

            var found = new List<IPAddress>();

            foreach (var name in _names)
            {
                try
                {
                    found.AddRange((await _lookUp(name, cancellationToken)).Select(Canonical));
                }
                catch (Exception exception) when (exception is System.Net.Sockets.SocketException or ArgumentException)
                {
                    // Not there (yet), or not a name: trusted by nobody until the next look.
                }
            }

            return _looked = new Looked([.. found], _time.GetUtcNow());
        }
        finally
        {
            _looking.Release();
        }
    }

    private static string[] Split(string? value) =>
        value?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];

    /// <summary>
    /// What the names were found at, and when.
    /// </summary>
    private sealed record Looked(IPAddress[] Addresses, DateTimeOffset At);
}
