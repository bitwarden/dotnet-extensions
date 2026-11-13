using System.Net;
using Bitwarden.Server.Sdk.Environment;
using Microsoft.Extensions.DependencyInjection;

namespace Bitwarden.Server.Sdk.Http.Tests;

/// <summary>
/// <see cref="HttpClientBuilderSsrfExtensions.AddCloudSsrfProtection"/> guards a client in the cloud and leaves a
/// self-hosted install's as it was, so a self-hosted install can reach an identity provider on its own network.
/// </summary>
public sealed class HttpClientBuilderSsrfExtensionsTests
{
    private static HttpClient Client(bool selfHosted, HttpMessageHandler? primary = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IBitwardenEnvironment>(new Install(selfHosted));
        var builder = services.AddHttpClient("test");
        if (primary is not null)
        {
            builder.ConfigurePrimaryHttpMessageHandler(() => primary);
        }

        builder.AddCloudSsrfProtection();

        return services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>().CreateClient("test");
    }

    [Theory]
    [InlineData("https://127.0.0.1/.well-known/openid-configuration")]
    [InlineData("https://10.0.0.1/.well-known/openid-configuration")]
    [InlineData("https://169.254.169.254/latest/meta-data/")]
    [InlineData("https://168.63.129.16/metadata/instance")]
    [InlineData("https://[::1]/.well-known/openid-configuration")]
    public async Task Cloud_InternalAddress_IsRefusedBeforeConnecting(string url)
    {
        var client = Client(selfHosted: false);

        await Assert.ThrowsAsync<SsrfProtectionException>(() => client.GetAsync(url, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Cloud_HostnameResolvingToAnInternalAddress_IsRefused()
    {
        var client = Client(selfHosted: false);

        await Assert.ThrowsAsync<SsrfProtectionException>(
            () => client.GetAsync("https://localhost/.well-known/openid-configuration", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SelfHosted_InternalAddress_IsAttempted()
    {
        var client = Client(selfHosted: true);

        var exception = await Record.ExceptionAsync(
            () => client.GetAsync("https://127.0.0.1:1/.well-known/openid-configuration", TestContext.Current.CancellationToken));

        Assert.IsType<HttpRequestException>(exception);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void OnlyTheCloudsPrimaryHandler_StopsFollowingRedirects(bool selfHosted, bool followsRedirects)
    {
        var primary = new SocketsHttpHandler();

        // Creating the client builds its handlers, which configures the primary one.
        using var client = Client(selfHosted, primary);

        Assert.Equal(followsRedirects, primary.AllowAutoRedirect);
    }

    // The primary handler a client names after the guard is the one it sends with, and it stops following too.
    [Fact]
    public void APrimaryHandlerConfiguredAfterTheGuard_StopsFollowingRedirects()
    {
        var primary = new SocketsHttpHandler();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpClient("test")
            .AddSsrfProtection()
            .ConfigurePrimaryHttpMessageHandler(() => primary);

        using var client = services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>().CreateClient("test");

        Assert.False(primary.AllowAutoRedirect);
    }

    // A client left with the factory's own primary handler sends through it, set up to connect only where the guard
    // checked.
    [Fact]
    public void AClientWithTheFactorysPrimaryHandler_IsSetUp()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpClient("test").AddSsrfProtection();

        HttpMessageHandler? handler = services.BuildServiceProvider().GetRequiredService<IHttpMessageHandlerFactory>().CreateHandler("test");
        while (handler is DelegatingHandler delegating)
        {
            handler = delegating.InnerHandler;
        }

        var sender = Assert.IsType<SocketsHttpHandler>(handler);
        Assert.False(sender.AllowAutoRedirect);
        Assert.False(sender.UseCookies);
        Assert.IsType<SsrfConnector>(sender.ConnectCallback?.Target);
    }

    // An attacker's server redirecting to the metadata address is refused however the client was put together:
    // the redirect is never followed below the guard, so nothing internal is ever asked.
    [Fact]
    public async Task ARedirectToAnInternalAddress_IsRefused_WithThePrimaryHandlerConfiguredAfterTheGuard()
    {
        using var server = new ScriptedServer("HTTP/1.1 302 Found\r\nLocation: http://169.254.169.254/latest/meta-data/");
        var network = new FakeNetwork(server);
        var services = Services(network);
        services.AddHttpClient("test")
            .AddSsrfProtection()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { UseProxy = false });
        var client = services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>().CreateClient("test");

        await Assert.ThrowsAsync<SsrfProtectionException>(
            () => client.GetAsync("http://8.8.8.8/start", TestContext.Current.CancellationToken));
        Assert.Single(server.Requests);
    }

    // A handler the client adds, after the guard or under every client's guard, runs once for each request the caller
    // sends: a credential it attaches never reaches a redirect hop the remote server chose.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ACredentialHandler_NeverReachesARedirectHop(bool guardedByTheDefaults)
    {
        using var server = new ScriptedServer("HTTP/1.1 302 Found\r\nLocation: http://1.1.1.1/next");
        var network = new FakeNetwork(server);
        var services = Services(network);
        if (guardedByTheDefaults)
        {
            services.ConfigureHttpClientDefaults(defaults => defaults.AddSsrfProtection());
            services.AddHttpClient("test").AddHttpMessageHandler(() => new Bearer());
        }
        else
        {
            services.AddHttpClient("test").AddSsrfProtection().AddHttpMessageHandler(() => new Bearer());
        }

        var client = services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>().CreateClient("test");

        using var response = await client.GetAsync("http://8.8.8.8/start", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Authorization: Bearer SECRET-TOKEN\n", server.Requests[0], StringComparison.Ordinal);
        Assert.DoesNotContain("Authorization", server.Requests[1], StringComparison.Ordinal);
    }

    // Two names on one address (a shared CDN, say) keep their cookies apart: the client keeps none, so a cookie one
    // site sets is never sent to the other, even through a proxy, where both are addressed by the one address.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ACookieOneHostSets_IsNeverSentToAnotherOnTheSameAddress(bool proxied)
    {
        using var server = new ScriptedServer("HTTP/1.1 200 OK\r\nSet-Cookie: session=victim-secret");
        var network = new FakeNetwork(server, ("victim-idp.example", ["93.184.216.34"]), ("attacker.example", ["93.184.216.34"]));
        var services = Services(network, proxied ? new WebProxy("http://proxy.test:3128") : null);
        services.AddHttpClient("test").AddSsrfProtection();
        var client = services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>().CreateClient("test");

        (await client.GetAsync("http://victim-idp.example/", TestContext.Current.CancellationToken)).Dispose();
        (await client.GetAsync("http://attacker.example/", TestContext.Current.CancellationToken)).Dispose();
        (await client.GetAsync("http://victim-idp.example/", TestContext.Current.CancellationToken)).Dispose();

        Assert.Equal(3, server.Requests.Count);
        Assert.All(server.Requests, request => Assert.DoesNotContain("Cookie", request, StringComparison.Ordinal));
    }

    // A client guarded by every client's defaults and by its own registration has one guard, and follows a redirect
    // as a client guarded once does: a second guard below the first would follow it itself, and the first would refuse
    // its answer.
    [Fact]
    public async Task AClientGuardedByTheDefaultsAndItsOwnRegistration_FollowsARedirect()
    {
        using var server = new ScriptedServer("HTTP/1.1 302 Found\r\nLocation: http://1.1.1.1/final");
        var network = new FakeNetwork(server);
        var services = Services(network);
        services.ConfigureHttpClientDefaults(defaults => defaults.AddSsrfProtection());
        services.AddHttpClient("test").AddSsrfProtection();
        var client = services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>().CreateClient("test");

        using var response = await client.GetAsync("http://8.8.8.8/start", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            [new IPEndPoint(IPAddress.Parse("8.8.8.8"), 80), new IPEndPoint(IPAddress.Parse("1.1.1.1"), 80)],
            network.Opened);
    }

    // Guarded twice, a client keeps the first registration's choice of following redirects.
    [Theory]
    [InlineData(true, false, HttpStatusCode.OK, 2)]
    [InlineData(false, true, HttpStatusCode.Found, 1)]
    public async Task AClientGuardedTwice_KeepsTheFirstRegistrationsFollowRedirects(
        bool first, bool second, HttpStatusCode status, int sent)
    {
        using var server = new ScriptedServer("HTTP/1.1 302 Found\r\nLocation: http://1.1.1.1/final");
        var services = Services(new FakeNetwork(server));
        services.AddHttpClient("test")
            .AddSsrfProtection(followRedirects: first)
            .AddSsrfProtection(followRedirects: second);
        var client = services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>().CreateClient("test");

        using var response = await client.GetAsync("http://8.8.8.8/start", TestContext.Current.CancellationToken);

        Assert.Equal(status, response.StatusCode);
        Assert.Equal(sent, server.Requests.Count);
    }

    /// <summary>
    /// Services whose guards reach the network through <paramref name="network"/>, through <paramref name="proxy"/> or
    /// else directly.
    /// </summary>
    private static ServiceCollection Services(FakeNetwork network, IWebProxy? proxy = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<SsrfConnector>(network);
        services.ConfigureHttpClientDefaults(defaults => defaults.ConfigurePrimaryHttpMessageHandler(
            () => new SocketsHttpHandler { UseProxy = proxy is not null, Proxy = proxy }));
        return services;
    }

    /// <summary>
    /// Attaches a credential to every request it sees.
    /// </summary>
    private sealed class Bearer : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "SECRET-TOKEN");
            return base.SendAsync(request, cancellationToken);
        }
    }

    private sealed class Install(bool selfHosted) : IBitwardenEnvironment
    {
        public string Version => "0.0.0";
        public string? GitHash => null;
        public bool SelfHosted => selfHosted;
        public string? SelfHostFlavor => null;
    }
}
