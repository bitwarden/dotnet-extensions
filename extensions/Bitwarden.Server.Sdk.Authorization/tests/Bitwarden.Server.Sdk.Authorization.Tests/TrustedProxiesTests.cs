using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Time.Testing;

namespace Bitwarden.Server.Sdk.Authorization.Tests;

public sealed class TrustedProxiesTests
{
    private static readonly IPAddress Proxy = IPAddress.Parse("172.18.0.9");

    // The address a request is recorded under, and carries to the services it calls: an IPv4 address in IPv6 is the
    // IPv4 address, and a link-local address loses the zone that names this host's interface.
    [Theory]
    [InlineData("::ffff:203.0.113.9", "203.0.113.9")]
    [InlineData("fe80::1%2", "fe80::1")]
    [InlineData("2001:db8::1", "2001:db8::1")]
    [InlineData("203.0.113.9", "203.0.113.9")]
    public void Canonical_IsTheAddressAnotherServiceCanRead(string address, string expected) =>
        Assert.Equal(expected, TrustedProxies.Canonical(IPAddress.Parse(address)).ToString());

    // A proxy that moves gives up its old address, which another container may be given: once a lookup is stale the
    // name is looked up again before the old address is believed, however often it was seen.
    [Fact]
    public async Task AProxyThatMoved_IsNoLongerTrustedAtItsOldAddress_OnceTheLookupIsStale()
    {
        var time = new FakeTimeProvider();
        var at = Proxy;
        var proxies = new TrustedProxies(Configuration(knownProxies: "proxy"), time, (_, _) => Task.FromResult(new[] { at }));

        Assert.True(await proxies.TrustsAsync(Proxy, CancellationToken.None));

        at = IPAddress.Parse("172.18.0.20");
        time.Advance(TrustedProxies.Refresh - TimeSpan.FromSeconds(1));
        Assert.True(await proxies.TrustsAsync(Proxy, CancellationToken.None));

        time.Advance(TimeSpan.FromSeconds(1));
        Assert.False(await proxies.TrustsAsync(Proxy, CancellationToken.None));
        Assert.True(await proxies.TrustsAsync(IPAddress.Parse("172.18.0.20"), CancellationToken.None));
    }

    // A lookup taken as true is not repeated for each request.
    [Fact]
    public async Task AFreshLookup_IsNotRepeated()
    {
        var time = new FakeTimeProvider();
        var lookups = 0;
        var proxies = new TrustedProxies(
            Configuration(knownProxies: "proxy"),
            time,
            (_, _) =>
            {
                lookups++;
                return Task.FromResult(new[] { Proxy });
            });

        for (var request = 0; request < 3; request++)
        {
            Assert.True(await proxies.TrustsAsync(Proxy, CancellationToken.None));
        }

        Assert.Equal(1, lookups);
    }

    // Requests arriving together while the lookup is stale look the name up once: the second waits for the first's
    // answer rather than asking again.
    [Fact]
    public async Task ConcurrentRequests_WithAStaleLookup_LookUpOnce()
    {
        var lookups = 0;
        var answer = new TaskCompletionSource<IPAddress[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var proxies = new TrustedProxies(
            Configuration(knownProxies: "proxy"),
            new FakeTimeProvider(),
            (_, _) =>
            {
                Interlocked.Increment(ref lookups);
                return answer.Task;
            });

        var first = proxies.TrustsAsync(Proxy, CancellationToken.None).AsTask();
        var second = proxies.TrustsAsync(Proxy, CancellationToken.None).AsTask();
        answer.SetResult([Proxy]);

        var trusted = await Task.WhenAll(first, second);

        Assert.All(trusted, Assert.True);
        Assert.Equal(1, lookups);
    }

    // A name that is not one a lookup accepts trusts nobody, and the other names are still looked up.
    [Fact]
    public async Task ANameTheLookupRefuses_TrustsNobody_AndTheOtherNamesAreStillTrusted()
    {
        var proxies = new TrustedProxies(
            Configuration(knownProxies: "not a name, proxy"),
            new FakeTimeProvider(),
            (name, _) => name == "proxy"
                ? Task.FromResult(new[] { Proxy })
                : throw new ArgumentException("Not a host name.", nameof(name)));

        Assert.True(await proxies.TrustsAsync(Proxy, CancellationToken.None));
        Assert.False(await proxies.TrustsAsync(IPAddress.Parse("172.18.0.20"), CancellationToken.None));
    }

    // An entry that is not a network is ignored, and the rest are still trusted, IPv6 networks among them.
    [Theory]
    [InlineData("2001:db8::7", true)]
    [InlineData("10.1.2.3", true)]
    [InlineData("2001:db9::7", false)]
    [InlineData("192.0.2.10", false)]
    public async Task KnownNetworks_TrustsEachNetwork_AndIgnoresWhatIsNotOne(string peer, bool trusted)
    {
        var proxies = new TrustedProxies(
            Configuration(knownNetworks: "not-a-network, 10.0.0.0/40, 192.0.2.10, 2001:db8::/32, 10.0.0.0/8"),
            TimeProvider.System,
            (_, _) => Task.FromResult(Array.Empty<IPAddress>()));

        Assert.Equal(trusted, await proxies.TrustsAsync(IPAddress.Parse(peer), CancellationToken.None));
    }

    private static IConfiguration Configuration(string? knownProxies = null, string? knownNetworks = null) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{TrustedProxies.Section}:KnownProxies"] = knownProxies,
                [$"{TrustedProxies.Section}:KnownNetworks"] = knownNetworks,
            })
            .Build();
}
