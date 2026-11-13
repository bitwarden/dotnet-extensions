using System.Net;

namespace Bitwarden.Server.Sdk.Http.Tests;

/// <summary>
/// The connector opens a connection only to the address the guard checked for the request asking for it, or to the
/// proxy a checked request goes through, and refuses every other.
/// </summary>
public sealed class SsrfConnectorTests
{
    private static readonly IPAddress _checked = IPAddress.Parse("93.184.216.34");

    [Fact]
    public async Task ARequestWithoutAPin_IsRefused()
    {
        using var server = new ScriptedServer();
        var network = new FakeNetwork(server);

        await AssertRefused(network, Send(network.Sender(), "http://example.test/", pin: null));
    }

    [Fact]
    public async Task TheCheckedHost_IsConnectedToTheCheckedAddress()
    {
        using var server = new ScriptedServer();
        var network = new FakeNetwork(server);

        using var response = await Send(network.Sender(), "http://example.test/", new SsrfPin("example.test", 80, _checked, false));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal([new IPEndPoint(_checked, 80)], network.Opened);
    }

    // A connection for a host or a port the pin wasn't checked for (a handler below the guard readdressed the request)
    // is refused.
    [Theory]
    [InlineData("http://other.test/")]
    [InlineData("http://example.test:8080/")]
    public async Task AnotherHostOrPort_IsRefused(string url)
    {
        using var server = new ScriptedServer();
        var network = new FakeNetwork(server);

        await AssertRefused(network, Send(network.Sender(), url, new SsrfPin("example.test", 80, _checked, false)));
    }

    // An IPv4 address written as IPv6 is the address it maps.
    [Fact]
    public async Task AnIPv4MappedAddress_IsTheAddressItMaps()
    {
        using var server = new ScriptedServer();
        var network = new FakeNetwork(server);

        using var response = await Send(
            network.Sender(), "http://[::ffff:93.184.216.34]/", new SsrfPin("::ffff:93.184.216.34", 80, _checked, false));

        Assert.Equal([new IPEndPoint(_checked, 80)], network.Opened);
    }

    [Fact]
    public async Task AProxiedRequestNamingTheCheckedAddress_GoesToTheProxy()
    {
        using var server = new ScriptedServer();
        var network = new FakeNetwork(server);

        using var response = await Send(
            network.Sender(Proxy()), "http://93.184.216.34/", new SsrfPin("example.test", 80, _checked, true));

        Assert.Equal([new DnsEndPoint("proxy.test", 3128)], network.Opened);
    }

    // A proxied request a handler below the guard readdressed names an address nobody checked.
    [Fact]
    public async Task AProxiedRequestNamingAnotherAddress_IsRefused()
    {
        using var server = new ScriptedServer();
        var network = new FakeNetwork(server);

        await AssertRefused(
            network, Send(network.Sender(Proxy()), "http://203.0.113.9/", new SsrfPin("example.test", 80, _checked, true)));
    }

    // A proxied request for another port than the checked one is refused.
    [Fact]
    public async Task AProxiedRequestForAnotherPort_IsRefused()
    {
        using var server = new ScriptedServer();
        var network = new FakeNetwork(server);

        await AssertRefused(
            network, Send(network.Sender(Proxy()), "http://93.184.216.34:8080/", new SsrfPin("example.test", 80, _checked, true)));
    }

    // A request without a pin is refused whatever it names in its Host header: only the handler's own CONNECT for a
    // tunnel is let through unpinned.
    [Fact]
    public async Task ARequestWithoutAPinNamingAPublicAddress_IsRefused()
    {
        using var server = new ScriptedServer();
        var network = new FakeNetwork(server);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.test/");
        request.Headers.Host = "93.184.216.34";

        await AssertRefused(network, Send(network.Sender(Proxy()), request));
    }

    // A CONNECT is a tunnel only to the handler's proxy: a caller's own, sent straight to a host nobody checked, is
    // refused whatever address it names.
    [Fact]
    public async Task ACallersOwnConnectToAnotherHost_IsRefused()
    {
        using var server = new ScriptedServer();
        var network = new FakeNetwork(server);
        var request = new HttpRequestMessage(HttpMethod.Connect, "http://internal.test/");
        request.Headers.Host = "93.184.216.34:80";

        await AssertRefused(network, Send(network.Sender(), request));
    }

    // A CONNECT the proxy bypasses goes straight to its host, which isn't the handler's proxy: refused.
    [Fact]
    public async Task AConnectThatBypassesTheProxy_IsRefused()
    {
        using var server = new ScriptedServer();
        var network = new FakeNetwork(server);
        var proxy = new WebProxy("http://proxy.test:3128", BypassOnLocal: false, BypassList: [@"internal\.test"]);
        var request = new HttpRequestMessage(HttpMethod.Connect, "http://internal.test/");
        request.Headers.Host = "93.184.216.34:80";

        await AssertRefused(network, Send(network.Sender(proxy), request));
    }

    // A tunnel for a name, rather than the checked address, would leave the proxy the name to resolve.
    [Fact]
    public async Task ATunnelForAName_IsRefused()
    {
        using var server = new ScriptedServer();
        var network = new FakeNetwork(server);

        await AssertRefused(network, Send(network.Sender(Proxy()), "https://example.test/", pin: null));
    }

    // A request the guard didn't expect to go through a proxy would leave the proxy the name to resolve.
    [Fact]
    public async Task AnUnproxiedRequestGoingToAProxy_IsRefused()
    {
        using var server = new ScriptedServer();
        var network = new FakeNetwork(server);

        await AssertRefused(
            network, Send(network.Sender(Proxy()), "http://example.test/", new SsrfPin("example.test", 80, _checked, false)));
    }

    // A tunnel to an internal address is refused, however it is written.
    [Theory]
    [InlineData("https://10.0.0.1/", "10.0.0.1")]
    [InlineData("https://[::ffff:10.0.0.1]/", "10.0.0.1")]
    public async Task ATunnelToAnInternalAddress_IsRefused(string url, string address)
    {
        using var server = new ScriptedServer();
        var network = new FakeNetwork(server);

        await AssertRefused(
            network, Send(network.Sender(Proxy()), url, new SsrfPin("example.test", 443, IPAddress.Parse(address), true)));
    }

    // Without a fake, a connection is a TCP connection to the endpoint, and a name is resolved by the system.
    [Fact]
    public async Task TheNetwork_ConnectsOverTcp()
    {
        using var server = new ScriptedServer();
        var connector = new SsrfConnector();
        using var sender = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            UseProxy = false,
            ConnectCallback = new SsrfConnection(connector, null).ConnectAsync,
        };

        using var response = await Send(
            sender, $"http://127.0.0.1:{server.Port}/", new SsrfPin("127.0.0.1", server.Port, IPAddress.Loopback, false));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(IPAddress.Loopback, await connector.ResolveAsync("localhost", TestContext.Current.CancellationToken));
    }

    private static WebProxy Proxy() => new("http://proxy.test:3128");

    private static Task<HttpResponseMessage> Send(SocketsHttpHandler sender, string url, SsrfPin? pin)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (pin is not null)
        {
            request.Options.Set(SsrfConnector.PinKey, pin);
        }

        return Send(sender, request);
    }

    private static async Task<HttpResponseMessage> Send(SocketsHttpHandler sender, HttpRequestMessage request)
    {
        using var invoker = new HttpMessageInvoker(sender, disposeHandler: false);
        return await invoker.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static async Task AssertRefused(FakeNetwork network, Task<HttpResponseMessage> sending)
    {
        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => sending);

        Assert.IsType<SsrfProtectionException>(exception.InnerException);
        Assert.Empty(network.Opened);
    }
}
