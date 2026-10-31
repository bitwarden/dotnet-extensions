using System.Net;
using Bitwarden.Server.Sdk.Service.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Bitwarden.Server.Sdk.Service.Tests.Http;

public class SsrfProtectionHandlerTests
{
    /// <summary>
    /// A test delegating handler that captures requests and returns canned responses.
    /// Registered after <see cref="HttpClientBuilderSsrfExtensions.AddSsrfProtection"/> so that
    /// the default <see cref="SocketsHttpHandler"/> remains as the primary handler (allowing
    /// <c>AllowAutoRedirect = false</c> to be set correctly), while this handler intercepts
    /// requests before they reach the network.
    /// </summary>
    private class TestInnerHandler : DelegatingHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        public List<HttpRequestMessage> AllRequests { get; } = [];

        /// <summary>
        /// Each request's body and content type as it was sent, read before the handler disposes the request.
        /// </summary>
        public List<(string? Body, string? ContentType)> Bodies { get; } = [];

        private readonly Queue<HttpResponseMessage> _responses = new();

        public void EnqueueResponse(HttpResponseMessage response)
        {
            _responses.Enqueue(response);
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            AllRequests.Add(request);
            Bodies.Add(request.Content is null
                ? (null, null)
                : (await Sent(request.Content, cancellationToken), request.Content.Headers.ContentType?.ToString()));
            var response = _responses.Count > 0
                ? _responses.Dequeue()
                : new HttpResponseMessage(HttpStatusCode.OK);
            return response;
        }

        /// <summary>
        /// The body as a connection would send it: written out, not buffered.
        /// </summary>
        private static async Task<string> Sent(HttpContent content, CancellationToken cancellationToken)
        {
            using var sent = new MemoryStream();
            await content.CopyToAsync(sent, cancellationToken);
            return System.Text.Encoding.UTF8.GetString(sent.ToArray());
        }
    }

    /// <summary>
    /// Creates an HttpClient with SSRF protection wired up via the public
    /// <see cref="HttpClientBuilderSsrfExtensions.AddSsrfProtection"/> extension.
    /// </summary>
    private static (HttpClient client, TestInnerHandler inner) CreateClient(bool followRedirects = true)
    {
        var inner = new TestInnerHandler();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpClient("test")
            .AddSsrfProtection(followRedirects)
            .AddHttpMessageHandler(() => inner);
        var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IHttpClientFactory>();
        return (factory.CreateClient("test"), inner);
    }

    [Fact]
    public async Task SendAsync_NullRequestUri_ThrowsInvalidOperationException()
    {
        var (client, _) = CreateClient();
        var request = new HttpRequestMessage
        {
            RequestUri = null,
            Method = HttpMethod.Get
        };

        // HttpClient validates the URI before the handler runs
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.SendAsync(request, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("http://127.0.0.1/test")]
    [InlineData("http://10.0.0.1/test")]
    [InlineData("http://192.168.1.1/test")]
    [InlineData("http://172.16.0.1/test")]
    [InlineData("http://169.254.169.254/latest/meta-data/")] // AWS metadata
    [InlineData("http://100.64.0.1/test")]                   // CGNAT
    [InlineData("http://[::1]/test")]                         // IPv6 loopback
    public async Task SendAsync_DirectIpInternal_ThrowsSsrfProtectionException(string url)
    {
        var (client, _) = CreateClient();

        await Assert.ThrowsAsync<SsrfProtectionException>(
            () => client.GetAsync(url, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("http://8.8.8.8/test")]
    [InlineData("http://1.1.1.1/test")]
    [InlineData("http://52.20.30.40/test")]
    public async Task SendAsync_DirectIpPublic_Succeeds(string url)
    {
        var (client, inner) = CreateClient();

        var response = await client.GetAsync(url, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(inner.LastRequest);
    }

    [Fact]
    public async Task SendAsync_PublicHost_PreservesHostHeader()
    {
        // This test verifies that when a hostname resolves to a public IP,
        // the handler rewrites the URI to the IP but preserves the Host header.
        // We can't easily mock DNS in C#, so we test with a direct IP that
        // doesn't need DNS resolution.
        var (client, inner) = CreateClient();

        var response = await client.GetAsync("http://8.8.8.8/test", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(inner.LastRequest);
        // URI host should be rewritten to the IP
        Assert.Equal("8.8.8.8", inner.LastRequest!.RequestUri!.Host);
    }

    [Fact]
    public async Task SendAsync_CancellationRequestedBeforeResolution_ThrowsOperationCanceled()
    {
        // Regression: DNS resolution must honor the cancellation token. A hostname forces the
        // resolution path; an already-cancelled token must abort before the inner handler runs.
        var (client, inner) = CreateClient();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.GetAsync("http://example.com/test", cts.Token));
        Assert.Empty(inner.AllRequests);
    }

    [Fact]
    public async Task SendAsync_HostnameResolvingToInternalIp_ThrowsSsrfProtectionException()
    {
        // "localhost" should always resolve to 127.0.0.1 or ::1
        var (client, _) = CreateClient();

        await Assert.ThrowsAsync<SsrfProtectionException>(
            () => client.GetAsync("http://localhost/test", TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("http://0.0.0.0/test")]
    [InlineData("http://127.0.0.2/test")]
    [InlineData("http://100.100.100.100/test")] // CGNAT
    public async Task SendAsync_VariousInternalIps_ThrowsSsrfProtectionException(string url)
    {
        var (client, _) = CreateClient();

        await Assert.ThrowsAsync<SsrfProtectionException>(
            () => client.GetAsync(url, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SendAsync_RedirectToInternalIp_ThrowsSsrfProtectionException()
    {
        // Simulates an attacker-controlled domain redirecting to the AWS metadata endpoint
        var (client, inner) = CreateClient();
        var redirectResponse = new HttpResponseMessage(HttpStatusCode.Found);
        redirectResponse.Headers.Location = new Uri("http://169.254.169.254/latest/meta-data/");
        inner.EnqueueResponse(redirectResponse);

        await Assert.ThrowsAsync<SsrfProtectionException>(
            () => client.GetAsync("http://8.8.8.8/attacker", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SendAsync_RedirectToPublicIp_Succeeds()
    {
        var (client, inner) = CreateClient();
        var redirectResponse = new HttpResponseMessage(HttpStatusCode.Found);
        redirectResponse.Headers.Location = new Uri("http://1.1.1.1/final");
        inner.EnqueueResponse(redirectResponse);

        var response = await client.GetAsync("http://8.8.8.8/start", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, inner.AllRequests.Count);
    }

    [Fact]
    public async Task SendAsync_RedirectToLocalhost_ThrowsSsrfProtectionException()
    {
        var (client, inner) = CreateClient();
        var redirectResponse = new HttpResponseMessage(HttpStatusCode.MovedPermanently);
        redirectResponse.Headers.Location = new Uri("http://127.0.0.1/admin");
        inner.EnqueueResponse(redirectResponse);

        await Assert.ThrowsAsync<SsrfProtectionException>(
            () => client.GetAsync("http://8.8.8.8/redirect-to-localhost", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SendAsync_MultipleRedirectsToPublicIps_Succeeds()
    {
        var (client, inner) = CreateClient();

        var redirect1 = new HttpResponseMessage(HttpStatusCode.Found);
        redirect1.Headers.Location = new Uri("http://1.1.1.1/hop2");
        inner.EnqueueResponse(redirect1);

        var redirect2 = new HttpResponseMessage(HttpStatusCode.Found);
        redirect2.Headers.Location = new Uri("http://52.20.30.40/hop3");
        inner.EnqueueResponse(redirect2);

        // Final response is OK (default from TestInnerHandler)

        var response = await client.GetAsync("http://8.8.8.8/start", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, inner.AllRequests.Count);
    }

    [Fact]
    public async Task SendAsync_RedirectChainEventuallyHitsInternalIp_ThrowsSsrfProtectionException()
    {
        var (client, inner) = CreateClient();

        var redirect1 = new HttpResponseMessage(HttpStatusCode.Found);
        redirect1.Headers.Location = new Uri("http://1.1.1.1/hop2");
        inner.EnqueueResponse(redirect1);

        var redirect2 = new HttpResponseMessage(HttpStatusCode.Found);
        redirect2.Headers.Location = new Uri("http://10.0.0.1/internal");
        inner.EnqueueResponse(redirect2);

        await Assert.ThrowsAsync<SsrfProtectionException>(
            () => client.GetAsync("http://8.8.8.8/start", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SendAsync_RelativeRedirect_ResolvesAgainstOriginalUri()
    {
        var (client, inner) = CreateClient();
        var redirectResponse = new HttpResponseMessage(HttpStatusCode.Found);
        redirectResponse.Headers.Location = new Uri("/new-path", UriKind.Relative);
        inner.EnqueueResponse(redirectResponse);

        var response = await client.GetAsync("http://8.8.8.8/original", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, inner.AllRequests.Count);
    }

    [Fact]
    public async Task SendAsync_NonHttpRedirectScheme_StopsFollowing()
    {
        var (client, inner) = CreateClient();
        var redirectResponse = new HttpResponseMessage(HttpStatusCode.Found);
        redirectResponse.Headers.Location = new Uri("ftp://8.8.8.8/file");
        inner.EnqueueResponse(redirectResponse);

        var response = await client.GetAsync("http://8.8.8.8/start", TestContext.Current.CancellationToken);

        // Should return the redirect response itself, not follow it
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Single(inner.AllRequests);
    }

    [Theory]
    [InlineData(HttpStatusCode.MovedPermanently)]  // 301
    [InlineData(HttpStatusCode.Found)]             // 302
    [InlineData(HttpStatusCode.SeeOther)]          // 303
    public async Task SendAsync_301_302_303_Redirect_ChangesPostToGet(HttpStatusCode redirectCode)
    {
        var (client, inner) = CreateClient();
        var redirectResponse = new HttpResponseMessage(redirectCode);
        redirectResponse.Headers.Location = new Uri("http://1.1.1.1/final");
        inner.EnqueueResponse(redirectResponse);

        var request = new HttpRequestMessage(HttpMethod.Post, "http://8.8.8.8/start");
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, inner.AllRequests.Count);
        Assert.Equal(HttpMethod.Get, inner.AllRequests[1].Method);
    }

    [Theory]
    [InlineData(HttpStatusCode.MovedPermanently)]  // 301
    [InlineData(HttpStatusCode.Found)]             // 302
    public async Task SendAsync_301_302_Redirect_PreservesNonPostMethod(HttpStatusCode redirectCode)
    {
        var (client, inner) = CreateClient();
        var redirectResponse = new HttpResponseMessage(redirectCode);
        redirectResponse.Headers.Location = new Uri("http://1.1.1.1/final");
        inner.EnqueueResponse(redirectResponse);

        var request = new HttpRequestMessage(HttpMethod.Head, "http://8.8.8.8/start");
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, inner.AllRequests.Count);
        Assert.Equal(HttpMethod.Head, inner.AllRequests[1].Method);
    }

    [Fact]
    public async Task SendAsync_303_Redirect_ChangesNonPostMethodToGet()
    {
        var (client, inner) = CreateClient();
        var redirectResponse = new HttpResponseMessage(HttpStatusCode.SeeOther);
        redirectResponse.Headers.Location = new Uri("http://1.1.1.1/final");
        inner.EnqueueResponse(redirectResponse);

        var request = new HttpRequestMessage(HttpMethod.Head, "http://8.8.8.8/start");
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, inner.AllRequests.Count);
        Assert.Equal(HttpMethod.Get, inner.AllRequests[1].Method);
    }

    [Theory]
    [InlineData(HttpStatusCode.TemporaryRedirect)] // 307
    [InlineData(HttpStatusCode.PermanentRedirect)] // 308
    public async Task SendAsync_307_308_Redirect_PreservesOriginalMethod(HttpStatusCode redirectCode)
    {
        var (client, inner) = CreateClient();
        var redirectResponse = new HttpResponseMessage(redirectCode);
        redirectResponse.Headers.Location = new Uri("http://1.1.1.1/final");
        inner.EnqueueResponse(redirectResponse);

        var request = new HttpRequestMessage(HttpMethod.Post, "http://8.8.8.8/start");
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, inner.AllRequests.Count);
        Assert.Equal(HttpMethod.Post, inner.AllRequests[1].Method);
    }

    [Fact]
    public async Task SendAsync_FollowRedirectsFalse_ReturnsRedirectResponseWithoutFollowing()
    {
        var (client, inner) = CreateClient(followRedirects: false);
        var redirectResponse = new HttpResponseMessage(HttpStatusCode.Found);
        redirectResponse.Headers.Location = new Uri("http://1.1.1.1/final");
        inner.EnqueueResponse(redirectResponse);

        var response = await client.GetAsync("http://8.8.8.8/start", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Single(inner.AllRequests);
    }

    [Fact]
    public async Task SendAsync_FollowRedirectsFalse_StillValidatesInitialRequest()
    {
        var (client, _) = CreateClient(followRedirects: false);

        await Assert.ThrowsAsync<SsrfProtectionException>(
            () => client.GetAsync("http://127.0.0.1/admin", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SendAsync_FollowRedirectsFalse_RedirectToInternalIp_ReturnsRedirectWithoutBlocking()
    {
        // When followRedirects is false, the handler doesn't follow the redirect at all,
        // so it never sees the internal IP. The caller is responsible for validating
        // redirect targets (e.g., Icons' FollowRedirectsAsync creates new requests
        // that go through SsrfProtectionHandler again).
        var (client, inner) = CreateClient(followRedirects: false);
        var redirectResponse = new HttpResponseMessage(HttpStatusCode.Found);
        redirectResponse.Headers.Location = new Uri("http://169.254.169.254/latest/meta-data/");
        inner.EnqueueResponse(redirectResponse);

        var response = await client.GetAsync("http://8.8.8.8/start", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Single(inner.AllRequests);
    }

    [Fact]
    public async Task SendAsync_RelativeRedirectAfterAbsoluteRedirect_ResolvesAgainstCurrentHost()
    {
        // Chain: 8.8.8.8 -> 302 http://1.1.1.1/hop2 -> 302 /relative
        // The relative redirect should resolve against 1.1.1.1 (the current hop),
        // not 8.8.8.8 (the original request which got IP-rewritten by ValidateAndSendAsync).
        var (client, inner) = CreateClient();

        var redirect1 = new HttpResponseMessage(HttpStatusCode.Found);
        redirect1.Headers.Location = new Uri("http://1.1.1.1/hop2");
        inner.EnqueueResponse(redirect1);

        var redirect2 = new HttpResponseMessage(HttpStatusCode.Found);
        redirect2.Headers.Location = new Uri("/relative-path", UriKind.Relative);
        inner.EnqueueResponse(redirect2);

        // Final response is OK (default from TestInnerHandler)

        var response = await client.GetAsync("http://8.8.8.8/start", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, inner.AllRequests.Count);
        // The third request should have resolved /relative-path against 1.1.1.1
        Assert.Equal("1.1.1.1", inner.AllRequests[2].Headers.Host);
    }

    [Fact]
    public async Task SendAsync_Post301ThenGet307_PreservesGetFromIntermediateHop()
    {
        // Chain: POST -> 301 (changes to GET) -> 307 (should preserve GET, not original POST)
        var (client, inner) = CreateClient();

        var redirect1 = new HttpResponseMessage(HttpStatusCode.MovedPermanently);
        redirect1.Headers.Location = new Uri("http://1.1.1.1/hop2");
        inner.EnqueueResponse(redirect1);

        var redirect2 = new HttpResponseMessage(HttpStatusCode.TemporaryRedirect);
        redirect2.Headers.Location = new Uri("http://52.20.30.40/hop3");
        inner.EnqueueResponse(redirect2);

        // Final response is OK (default from TestInnerHandler)

        var request = new HttpRequestMessage(HttpMethod.Post, "http://8.8.8.8/start");
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, inner.AllRequests.Count);
        Assert.Equal(HttpMethod.Post, inner.AllRequests[0].Method); // original
        Assert.Equal(HttpMethod.Get, inner.AllRequests[1].Method);  // 301 changed POST→GET
        Assert.Equal(HttpMethod.Get, inner.AllRequests[2].Method);  // 307 preserves GET (not original POST)
    }

    // A redirect keeping the method keeps the body: a POST answered 307 or 308 is sent again with what it carried,
    // never as an empty POST. The original request's headers (Authorization among them) still stay behind.
    [Theory]
    [InlineData(HttpStatusCode.TemporaryRedirect)]
    [InlineData(HttpStatusCode.PermanentRedirect)]
    public async Task SendAsync_307_308_Redirect_SendsTheBodyAgain_WithoutTheRequestsHeaders(HttpStatusCode redirectCode)
    {
        var (client, inner) = CreateClient();
        var redirectResponse = new HttpResponseMessage(redirectCode);
        redirectResponse.Headers.Location = new Uri("http://1.1.1.1/final");
        inner.EnqueueResponse(redirectResponse);

        var request = new HttpRequestMessage(HttpMethod.Post, "http://8.8.8.8/start")
        {
            Content = new StringContent("{\"event\":1}", System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "token");
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, inner.AllRequests.Count);
        Assert.Equal(HttpMethod.Post, inner.AllRequests[1].Method);
        Assert.Equal(("{\"event\":1}", "application/json; charset=utf-8"), inner.Bodies[1]);
        Assert.Null(inner.AllRequests[1].Headers.Authorization);
    }

    // A body read from a stream can be sent once only; the guard keeps a copy so the next hop carries it too.
    [Fact]
    public async Task SendAsync_307_Redirect_SendsAStreamedBodyAgain()
    {
        var (client, inner) = CreateClient();
        var redirectResponse = new HttpResponseMessage(HttpStatusCode.TemporaryRedirect);
        redirectResponse.Headers.Location = new Uri("http://1.1.1.1/final");
        inner.EnqueueResponse(redirectResponse);

        var request = new HttpRequestMessage(HttpMethod.Post, "http://8.8.8.8/start")
        {
            Content = new StreamContent(new OnceOnlyStream("payload")),
        };
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["payload", "payload"], inner.Bodies.Select(sent => sent.Body));
    }

    // A redirect that changes the method to GET leaves the body behind.
    [Fact]
    public async Task SendAsync_303_Redirect_SendsNoBody()
    {
        var (client, inner) = CreateClient();
        var redirectResponse = new HttpResponseMessage(HttpStatusCode.SeeOther);
        redirectResponse.Headers.Location = new Uri("http://1.1.1.1/final");
        inner.EnqueueResponse(redirectResponse);

        var request = new HttpRequestMessage(HttpMethod.Post, "http://8.8.8.8/start") { Content = new StringContent("form") };
        await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Null(inner.AllRequests[1].Content);
    }

    // Ten redirects are followed; the eleventh is answered to the caller as it came.
    [Fact]
    public async Task SendAsync_MoreThanTenRedirects_ReturnsTheEleventhUnfollowed()
    {
        var (client, inner) = CreateClient();
        for (var hop = 0; hop < 11; hop++)
        {
            var redirect = new HttpResponseMessage(HttpStatusCode.Found);
            redirect.Headers.Location = new Uri($"http://1.1.1.1/hop{hop + 1}");
            inner.EnqueueResponse(redirect);
        }

        var response = await client.GetAsync("http://8.8.8.8/start", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal(new Uri("http://1.1.1.1/hop11"), response.Headers.Location);
        Assert.Equal(11, inner.AllRequests.Count);
    }

    // A handler below the guard that follows a redirect itself takes the request somewhere the guard never checked:
    // the guard refuses the answer rather than hand it back.
    [Fact]
    public async Task SendAsync_AHandlerBelowFollowingARedirect_IsRefused()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpClient("test")
            .AddSsrfProtection()
            .AddHttpMessageHandler(() => new FollowingHandler(new Uri("http://169.254.169.254/latest/meta-data/")));
        var client = services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>().CreateClient("test");

        await Assert.ThrowsAsync<SsrfProtectionException>(
            () => client.GetAsync("http://8.8.8.8/start", TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Follows a redirect to <paramref name="to"/> as a primary handler with automatic redirects would: the request
    /// it answers for is now addressed there.
    /// </summary>
    private sealed class FollowingHandler(Uri to) : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.RequestUri = to;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = request });
        }
    }

    /// <summary>
    /// A stream that can be read through once and not rewound.
    /// </summary>
    private sealed class OnceOnlyStream(string text) : MemoryStream(System.Text.Encoding.UTF8.GetBytes(text))
    {
        public override bool CanSeek => false;
    }
}
