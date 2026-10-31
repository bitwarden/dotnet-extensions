using System.Net;
using System.Net.Sockets;
using System.Text;
using Bitwarden.Server.Sdk.Environment;
using Bitwarden.Server.Sdk.Service.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Bitwarden.Server.Sdk.Service.Tests.Http;

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

    // A primary handler with no redirect switch of its own (a test's fake, standing in for the sites a client
    // fetches) is sent with as it is.
    [Fact]
    public async Task APrimaryHandlerWithNoRedirectSwitch_IsSentWith()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpClient("test")
            .AddSsrfProtection()
            .ConfigurePrimaryHttpMessageHandler(() => new Answering());
        var client = services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>().CreateClient("test");

        using var response = await client.GetAsync("http://8.8.8.8/start", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // A primary handler that hands requests on is followed to the one that sends them, which stops following.
    [Fact]
    public void APrimaryHandlerHandingRequestsOn_ItsSenderStopsFollowingRedirects()
    {
        var sender = new SocketsHttpHandler();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpClient("test")
            .AddSsrfProtection()
            .ConfigurePrimaryHttpMessageHandler(() => new HandingOn(sender));

        using var client = services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>().CreateClient("test");

        Assert.False(sender.AllowAutoRedirect);
    }

    // An attacker's server redirecting to the metadata address is refused however the client was put together:
    // the redirect is never followed below the guard, so nothing internal is ever asked.
    [Fact]
    public async Task ARedirectToAnInternalAddress_IsRefused_WithThePrimaryHandlerConfiguredAfterTheGuard()
    {
        using var listener = new RedirectingListener();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpClient("test")
            .AddSsrfProtection()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // Every connection reaches the listener, whatever address the request was for.
                ConnectCallback = async (_, cancellationToken) =>
                {
                    var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
                    await socket.ConnectAsync(IPAddress.Loopback, listener.Port, cancellationToken);
                    return new NetworkStream(socket, ownsSocket: true);
                },
            });
        var client = services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>().CreateClient("test");

        await Assert.ThrowsAsync<SsrfProtectionException>(
            () => client.GetAsync("http://8.8.8.8/start", TestContext.Current.CancellationToken));
        Assert.Equal(1, listener.Served);
    }

    /// <summary>
    /// Answers its first request with a redirect to the cloud metadata address, and any later one with a secret.
    /// </summary>
    private sealed class RedirectingListener : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stopping = new();
        private int _served;

        public RedirectingListener()
        {
            _listener.Start();
            _ = ServeAsync();
        }

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        public int Served => Volatile.Read(ref _served);

        private async Task ServeAsync()
        {
            try
            {
                while (!_stopping.IsCancellationRequested)
                {
                    using var connection = await _listener.AcceptTcpClientAsync(_stopping.Token);
                    var stream = connection.GetStream();
                    var reader = new StreamReader(stream, Encoding.ASCII);
                    while (!string.IsNullOrEmpty(await reader.ReadLineAsync(_stopping.Token)))
                    {
                    }

                    var answer = Interlocked.Increment(ref _served) == 1
                        ? "HTTP/1.1 302 Found\r\nLocation: http://169.254.169.254/latest/meta-data/\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"
                        : "HTTP/1.1 200 OK\r\nContent-Length: 6\r\nConnection: close\r\n\r\nsecret";
                    await stream.WriteAsync(Encoding.ASCII.GetBytes(answer), _stopping.Token);
                }
            }
            catch (Exception) when (_stopping.IsCancellationRequested)
            {
                // Stopped.
            }
        }

        public void Dispose()
        {
            _stopping.Cancel();
            _listener.Stop();
            _stopping.Dispose();
        }
    }

    /// <summary>
    /// Answers every request itself, as a test's fake site does.
    /// </summary>
    private sealed class Answering : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = request });
    }

    private sealed class HandingOn(HttpMessageHandler inner) : DelegatingHandler(inner);

    private sealed class Install(bool selfHosted) : IBitwardenEnvironment
    {
        public string Version => "0.0.0";
        public string? GitHash => null;
        public bool SelfHosted => selfHosted;
        public string? SelfHostFlavor => null;
    }
}
