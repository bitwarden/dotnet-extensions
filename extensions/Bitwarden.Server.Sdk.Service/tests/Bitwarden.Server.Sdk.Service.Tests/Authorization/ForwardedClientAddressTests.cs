using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace Bitwarden.Server.Sdk.Service.Tests.Authorization;

// Forwarded headers: the address a request came from is its connection's, or the one a proxy it
// trusts was connected from, and never one a client wrote itself.
public sealed class ForwardedClientAddressTests
{
    private static readonly IPAddress Proxy = IPAddress.Parse("172.18.0.9");
    private static readonly IPAddress Browser = IPAddress.Parse("203.0.113.9");

    [Fact]
    public async Task FromLoopback_TheForwardedAddressIsTheClients()
    {
        var actual = await Address(IPAddress.Loopback, "203.0.113.9", Proxies());

        Assert.Equal(Browser, actual);
    }

    [Fact]
    public async Task FromAKnownProxyByName_TheLastForwardedAddressIsTheClients()
    {
        // The proxy appends who connected to it; what the client wrote before that is not believed.
        var actual = await Address(Proxy, "198.51.100.66, 203.0.113.9", Proxies(knownProxies: "proxy"));

        Assert.Equal(Browser, actual);
    }

    [Fact]
    public async Task FromAKnownProxyByAddressOrNetwork_TheForwardedAddressIsTheClients()
    {
        Assert.Equal(Browser, await Address(Proxy, "203.0.113.9", Proxies(knownProxies: "172.18.0.9")));
        Assert.Equal(Browser, await Address(Proxy, "203.0.113.9", Proxies(knownNetworks: "172.18.0.0/16")));
    }

    // A client connecting directly says whatever it likes, and keeps its own address.
    [Fact]
    public async Task FromAnyoneElse_TheHeaderIsIgnored()
    {
        var stranger = IPAddress.Parse("192.0.2.10");

        var actual = await Address(stranger, "203.0.113.9", Proxies(knownProxies: "proxy", knownNetworks: "172.18.0.0/16"));

        Assert.Equal(stranger, actual);
    }

    [Fact]
    public async Task WithNoForwardedAddress_TheConnectionsIsKept()
    {
        var actual = await Address(Proxy, null, Proxies(knownProxies: "proxy"));

        Assert.Equal(Proxy, actual);
    }

    [Theory]
    [InlineData("<script>")]
    [InlineData("203.0.113.9, unknown")]
    public async Task AForwardedAddressThatIsNotOne_IsNotTaken(string forwarded)
    {
        var actual = await Address(Proxy, forwarded, Proxies(knownProxies: "proxy"));

        Assert.Equal(Proxy, actual);
    }

    [Theory]
    [InlineData("203.0.113.9:51234", "203.0.113.9")]
    [InlineData("[2001:db8::7]:443", "2001:db8::7")]
    [InlineData("::ffff:203.0.113.9", "203.0.113.9")]
    public async Task AForwardedAddress_WithAPortOrCarriedInIPv6_IsTheAddress(string forwarded, string expected)
    {
        var actual = await Address(Proxy, forwarded, Proxies(knownProxies: "proxy"));

        Assert.Equal(IPAddress.Parse(expected), actual);
    }

    // The scheme too, from X-Forwarded-Proto: behind a proxy that terminates TLS the
    // request is https, so a SameAsRequest cookie is Secure.
    [Theory]
    [InlineData("https", "https")]
    [InlineData("HTTPS", "https")]
    [InlineData("http", "http")]
    [InlineData("http, https", "https")]
    public async Task FromAKnownProxy_TheForwardedSchemeIsTheRequests(string forwarded, string expected)
    {
        Assert.Equal(expected, await Scheme(Proxy, forwarded, Proxies(knownProxies: "proxy")));
        Assert.Equal(expected, await Scheme(IPAddress.Loopback, forwarded, Proxies()));
    }

    // A client connecting directly can't make its plain request https.
    [Fact]
    public async Task FromAnyoneElse_TheForwardedSchemeIsIgnored()
    {
        var stranger = IPAddress.Parse("192.0.2.10");

        Assert.Equal("http", await Scheme(stranger, "https", Proxies(knownProxies: "proxy", knownNetworks: "172.18.0.0/16")));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("ftp")]
    [InlineData("https, javascript")]
    [InlineData("<script>")]
    public async Task AForwardedSchemeThatIsNotHttpOrHttps_IsNotTaken(string? forwarded)
    {
        Assert.Equal("http", await Scheme(Proxy, forwarded, Proxies(knownProxies: "proxy")));
    }

    // A proxy that starts after the service is trusted once it can be found, without a restart.
    [Fact]
    public async Task AProxyThatCouldNotBeFound_IsLookedForAgainLater()
    {
        var time = new Clock();
        var found = false;
        var proxies = new TrustedProxies(
            Configuration(knownProxies: "proxy", knownNetworks: null),
            time,
            (_, _) => found ? Task.FromResult(new[] { Proxy }) : throw new System.Net.Sockets.SocketException());

        Assert.False(await proxies.TrustsAsync(Proxy, CancellationToken.None));

        found = true;
        Assert.False(await proxies.TrustsAsync(Proxy, CancellationToken.None));

        time.Now += TrustedProxies.Refresh;
        Assert.True(await proxies.TrustsAsync(Proxy, CancellationToken.None));
    }

    private static TrustedProxies Proxies(string? knownProxies = null, string? knownNetworks = null) =>
        new(
            Configuration(knownProxies, knownNetworks),
            TimeProvider.System,
            (name, _) => Task.FromResult(name == "proxy" ? new[] { Proxy } : Array.Empty<IPAddress>()));

    private static IConfiguration Configuration(string? knownProxies, string? knownNetworks) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{TrustedProxies.Section}:KnownProxies"] = knownProxies,
                [$"{TrustedProxies.Section}:KnownNetworks"] = knownNetworks,
            })
            .Build();

    /// <summary>
    /// The connection's address as the rest of the pipeline sees it.
    /// </summary>
    private static async Task<IPAddress?> Address(IPAddress peer, string? forwarded, TrustedProxies proxies)
    {
        var http = new DefaultHttpContext();
        http.Connection.RemoteIpAddress = peer;
        if (forwarded is not null)
        {
            http.Request.Headers[ForwardedClientAddress.ForwardedFor] = forwarded;
        }

        IPAddress? seen = null;
        var middleware = new ForwardedClientAddress(
            context =>
            {
                seen = context.Connection.RemoteIpAddress;
                return Task.CompletedTask;
            },
            proxies);

        await middleware.InvokeAsync(http);

        return seen;
    }

    /// <summary>
    /// The request's scheme as the rest of the pipeline sees it, over a plain connection.
    /// </summary>
    private static async Task<string?> Scheme(IPAddress peer, string? forwarded, TrustedProxies proxies)
    {
        var http = new DefaultHttpContext();
        http.Connection.RemoteIpAddress = peer;
        http.Request.Scheme = "http";
        if (forwarded is not null)
        {
            http.Request.Headers[ForwardedClientAddress.ForwardedProto] = forwarded;
        }

        string? seen = null;
        var middleware = new ForwardedClientAddress(
            context =>
            {
                seen = context.Request.Scheme;
                return Task.CompletedTask;
            },
            proxies);

        await middleware.InvokeAsync(http);

        return seen;
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 16, 9, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
