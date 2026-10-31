using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Time.Testing;

namespace Bitwarden.Server.Sdk.Service.Tests.Authorization;

public sealed class TrustedProxiesTests
{
    private static readonly IPAddress Proxy = IPAddress.Parse("172.18.0.9");

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
