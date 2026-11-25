using System.Net;
using Microsoft.Extensions.DependencyInjection;

namespace Bitwarden.Server.Sdk.Http.Tests;

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
        /// Each request's address as it was sent, before the guard gives the request its host name back.
        /// </summary>
        public List<Uri> SentUris { get; } = [];

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
            SentUris.Add(request.RequestUri!);
            Bodies.Add(request.Content is null
                ? (null, null)
                : (await Sent(request.Content, cancellationToken), request.Content.Headers.ContentType?.ToString()));
            var response = _responses.Count > 0
                ? _responses.Dequeue()
                : new HttpResponseMessage(HttpStatusCode.OK);

            // As a connection does: the answer names the request it answers.
            response.RequestMessage ??= request;
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
    [InlineData("http://[::7f00:1]/test")]                    // IPv4-compatible 127.0.0.1
    [InlineData("http://[::ffff:0:a00:1]/test")]              // SIIT 10.0.0.1
    [InlineData("http://[fec0::1]/test")]                     // IPv6 site-local
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

    // A redirect from https to http would leave TLS, and a 307 or 308 would send the body again in the clear. As
    // SocketsHttpHandler does, the guard answers with the redirect unfollowed.
    [Theory]
    [InlineData(HttpStatusCode.MovedPermanently)]
    [InlineData(HttpStatusCode.Found)]
    [InlineData(HttpStatusCode.SeeOther)]
    [InlineData(HttpStatusCode.TemporaryRedirect)]
    [InlineData(HttpStatusCode.PermanentRedirect)]
    public async Task SendAsync_HttpsToHttpRedirect_ReturnsTheRedirectUnfollowed(HttpStatusCode redirectCode)
    {
        var (client, inner) = CreateClient();
        var redirectResponse = new HttpResponseMessage(redirectCode);
        redirectResponse.Headers.Location = new Uri("http://1.1.1.1/final");
        inner.EnqueueResponse(redirectResponse);

        var request = new HttpRequestMessage(HttpMethod.Post, "https://8.8.8.8/start") { Content = new StringContent("payload") };
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(redirectCode, response.StatusCode);
        Assert.Equal(new Uri("http://1.1.1.1/final"), response.Headers.Location);
        Assert.Single(inner.AllRequests);
    }

    // The same holds for a later hop: once a chain reaches https, it never goes back to http.
    [Fact]
    public async Task SendAsync_HttpsToHttpRedirectAfterAnUpgrade_ReturnsTheRedirectUnfollowed()
    {
        var (client, inner) = CreateClient();
        var upgrade = new HttpResponseMessage(HttpStatusCode.Found);
        upgrade.Headers.Location = new Uri("https://1.1.1.1/second");
        inner.EnqueueResponse(upgrade);
        var downgrade = new HttpResponseMessage(HttpStatusCode.Found);
        downgrade.Headers.Location = new Uri("http://9.9.9.9/final");
        inner.EnqueueResponse(downgrade);

        var response = await client.GetAsync("http://8.8.8.8/start", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal(new Uri("http://9.9.9.9/final"), response.Headers.Location);
        Assert.Equal(2, inner.AllRequests.Count);
    }

    [Theory]
    [InlineData("http://8.8.8.8/start", "https://1.1.1.1/final")]
    [InlineData("https://8.8.8.8/start", "https://1.1.1.1/final")]
    [InlineData("https://8.8.8.8/start", "/final")]
    public async Task SendAsync_RedirectThatKeepsOrGainsTls_IsFollowed(string start, string location)
    {
        var (client, inner) = CreateClient();
        var redirectResponse = new HttpResponseMessage(HttpStatusCode.Found);
        redirectResponse.Headers.Location = new Uri(location, UriKind.RelativeOrAbsolute);
        inner.EnqueueResponse(redirectResponse);

        var response = await client.GetAsync(start, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, inner.AllRequests.Count);
        Assert.Equal(Uri.UriSchemeHttps, inner.AllRequests[1].RequestUri!.Scheme);
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

    // As SocketsHttpHandler does, a 303 turns any method but GET and HEAD into GET.
    [Theory]
    [InlineData("PUT", "GET")]
    [InlineData("DELETE", "GET")]
    [InlineData("PATCH", "GET")]
    [InlineData("GET", "GET")]
    [InlineData("HEAD", "HEAD")]
    public async Task SendAsync_303_Redirect_ChangesAnyMethodButGetAndHeadToGet(string method, string redirected)
    {
        var (client, inner) = CreateClient();
        var redirectResponse = new HttpResponseMessage(HttpStatusCode.SeeOther);
        redirectResponse.Headers.Location = new Uri("http://1.1.1.1/final");
        inner.EnqueueResponse(redirectResponse);

        var request = new HttpRequestMessage(new HttpMethod(method), "http://8.8.8.8/start");
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, inner.AllRequests.Count);
        Assert.Equal(new HttpMethod(redirected), inner.AllRequests[1].Method);
    }

    // A 300 with a Location is followed, as SocketsHttpHandler follows it, and only a POST becomes a GET.
    [Theory]
    [InlineData("POST", "GET")]
    [InlineData("GET", "GET")]
    [InlineData("HEAD", "HEAD")]
    [InlineData("PUT", "PUT")]
    public async Task SendAsync_300_RedirectWithALocation_IsFollowed(string method, string redirected)
    {
        var (client, inner) = CreateClient();
        var redirectResponse = new HttpResponseMessage(HttpStatusCode.MultipleChoices);
        redirectResponse.Headers.Location = new Uri("http://1.1.1.1/final");
        inner.EnqueueResponse(redirectResponse);

        var request = new HttpRequestMessage(new HttpMethod(method), "http://8.8.8.8/start");
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new HttpMethod(redirected), inner.AllRequests[1].Method);
        Assert.Equal(new Uri("http://1.1.1.1/final"), inner.SentUris[1]);
    }

    // A 300 without one offers choices, and is answered to the caller as it came.
    [Fact]
    public async Task SendAsync_300_WithoutALocation_IsReturned()
    {
        var (client, inner) = CreateClient();
        inner.EnqueueResponse(new HttpResponseMessage(HttpStatusCode.MultipleChoices));

        var response = await client.GetAsync("http://8.8.8.8/start", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.MultipleChoices, response.StatusCode);
        Assert.Single(inner.AllRequests);
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
        // redirect targets (for example by sending each redirect as a new request
        // through the same guarded client, so SsrfProtectionHandler checks it again).
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

    // Every hop that keeps the method carries the body, however many there are, and the answer's request is the
    // last hop's, still readable.
    [Fact]
    public async Task SendAsync_307Then308_SendsTheBodyOnEveryHop()
    {
        var (client, inner) = CreateClient();
        var temporary = new HttpResponseMessage(HttpStatusCode.TemporaryRedirect);
        temporary.Headers.Location = new Uri("http://1.1.1.1/second");
        inner.EnqueueResponse(temporary);
        var permanent = new HttpResponseMessage(HttpStatusCode.PermanentRedirect);
        permanent.Headers.Location = new Uri("http://9.9.9.9/final");
        inner.EnqueueResponse(permanent);

        var request = new HttpRequestMessage(HttpMethod.Post, "http://8.8.8.8/start")
        {
            Content = new StringContent("{\"event\":1}", System.Text.Encoding.UTF8, "application/json"),
        };
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal([HttpMethod.Post, HttpMethod.Post, HttpMethod.Post], inner.AllRequests.Select(sent => sent.Method));
        Assert.All(inner.Bodies, sent => Assert.Equal(("{\"event\":1}", "application/json; charset=utf-8"), sent));
        Assert.Equal("/final", response.RequestMessage!.RequestUri!.AbsolutePath);
        Assert.Equal("{\"event\":1}", await response.RequestMessage.Content!.ReadAsStringAsync(TestContext.Current.CancellationToken));
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

    // A request with no address is refused rather than handed on.
    [Fact]
    public async Task SendAsync_NoRequestUri_ThrowsSsrfProtectionException()
    {
        using var invoker = new HttpMessageInvoker(
            new SsrfProtectionHandler(Microsoft.Extensions.Logging.Abstractions.NullLogger<SsrfProtectionHandler>.Instance)
            {
                InnerHandler = new TestInnerHandler(),
            });
        using var request = new HttpRequestMessage(HttpMethod.Get, (Uri?)null);

        var exception = await Assert.ThrowsAsync<SsrfProtectionException>(
            () => invoker.SendAsync(request, TestContext.Current.CancellationToken));

        Assert.Equal("Request URI is null.", exception.Message);
    }

    // A host that resolves to nothing (here a reserved, never-resolving name) is refused: nothing is checked, so
    // nothing is sent.
    [Fact]
    public async Task SendAsync_AHostThatDoesNotResolve_ThrowsSsrfProtectionException()
    {
        var (client, inner) = CreateClient();

        var exception = await Assert.ThrowsAsync<SsrfProtectionException>(
            () => client.GetAsync("http://no-such-host.invalid/", TestContext.Current.CancellationToken));

        Assert.Contains("no-such-host.invalid", exception.Message, StringComparison.Ordinal);
        Assert.Empty(inner.AllRequests);
    }

    // A host name longer than DNS allows can't be resolved, and is refused as any other name that can't be.
    [Fact]
    public async Task SendAsync_AHostNameLongerThanDnsAllows_ThrowsSsrfProtectionException()
    {
        var (client, inner) = CreateClient();
        var host = string.Join('.', Enumerable.Repeat(new string('a', 59), 5)) + ".test";

        var exception = await Assert.ThrowsAsync<SsrfProtectionException>(
            () => client.GetAsync($"http://{host}/", TestContext.Current.CancellationToken));

        Assert.True(host.Length > 255);
        Assert.Contains(host, exception.Message, StringComparison.Ordinal);
        Assert.Empty(inner.AllRequests);
    }

    // The request connects to the address the guard checked, never to the name, which could resolve elsewhere by then;
    // the Host header carries the name.
    [Fact]
    public async Task SendAsync_AName_IsSentToTheAddressItWasCheckedAt()
    {
        var (invoker, inner, _) = Guard(("example.test", ["93.184.216.34"]));

        using var response = await invoker.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "http://example.test/path?query=1"), TestContext.Current.CancellationToken);

        Assert.Equal(new Uri("http://93.184.216.34/path?query=1"), inner.SentUris.Single());
        Assert.Equal("example.test", inner.LastRequest!.Headers.Host);
    }

    // Every address a name resolves to is checked: one internal address among public ones refuses the request.
    [Theory]
    [InlineData("93.184.216.34", "10.0.0.1")]
    [InlineData("10.0.0.1", "93.184.216.34")]
    [InlineData("2606:4700::1111", "::1")]
    public async Task SendAsync_ANameResolvingToAnyInternalAddress_IsRefused(string first, string second)
    {
        var (invoker, inner, _) = Guard(("example.test", [first, second]));

        await Assert.ThrowsAsync<SsrfProtectionException>(() => invoker.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "http://example.test/"), TestContext.Current.CancellationToken));

        Assert.Empty(inner.AllRequests);
    }

    // IPv4 is preferred when a name resolves to both families.
    [Fact]
    public async Task SendAsync_ANameResolvingToBothFamilies_IsSentOverIPv4()
    {
        var (invoker, inner, _) = Guard(("example.test", ["2606:4700::1111", "1.1.1.1"]));

        using var response = await invoker.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "http://example.test/"), TestContext.Current.CancellationToken);

        Assert.Equal("1.1.1.1", inner.SentUris.Single().Host);
    }

    // A name resolving to IPv6 only is sent to that address.
    [Fact]
    public async Task SendAsync_ANameResolvingToIPv6Only_IsSentThere()
    {
        var (invoker, inner, _) = Guard(("example.test", ["2606:4700::1111"]));

        using var response = await invoker.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "http://example.test/"), TestContext.Current.CancellationToken);

        Assert.Equal("[2606:4700::1111]", inner.SentUris.Single().Host);
    }

    // A port that isn't the scheme's default stays in the Host header.
    [Theory]
    [InlineData("http://example.test:8080/", "example.test:8080")]
    [InlineData("https://example.test:8443/", "example.test:8443")]
    [InlineData("https://example.test/", "example.test")]
    public async Task SendAsync_TheHostHeader_KeepsAPortThatIsNotTheDefault(string url, string hostHeader)
    {
        var (invoker, inner, _) = Guard(("example.test", ["93.184.216.34"]));

        using var response = await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, url), TestContext.Current.CancellationToken);

        Assert.Equal(hostHeader, inner.LastRequest!.Headers.Host);
        Assert.Equal(new Uri(url).Port, inner.SentUris.Single().Port);
    }

    // A Host header the caller set is sent as it is; the address the request connects to is still the checked one.
    [Fact]
    public async Task SendAsync_AHostHeaderTheCallerSet_IsKept()
    {
        var (invoker, inner, _) = Guard(("example.test", ["93.184.216.34"]));
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.test/");
        request.Headers.Host = "virtual.example";

        using var response = await invoker.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal("virtual.example", inner.LastRequest!.Headers.Host);
        Assert.Equal("93.184.216.34", inner.SentUris.Single().Host);
    }

    // An internationalized name is resolved, and sent in the Host header, in its ASCII form, which is all a connection
    // accepts: SocketsHttpHandler refuses a header that isn't ASCII.
    [Fact]
    public async Task SendAsync_AnInternationalizedName_IsSentInItsAsciiForm()
    {
        using var listener = new RecordingListener();
        var connectedTo = new List<string>();
        var (invoker, _, resolved) = Guard(
            ("xn--bcher-kva.de", ["93.184.216.34"]),
            new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                // Every connection reaches the listener, whatever address the request was for.
                ConnectCallback = async (context, cancellationToken) =>
                {
                    connectedTo.Add(context.DnsEndPoint.Host);
                    var socket = new System.Net.Sockets.Socket(System.Net.Sockets.SocketType.Stream, System.Net.Sockets.ProtocolType.Tcp);
                    await socket.ConnectAsync(IPAddress.Loopback, listener.Port, cancellationToken);
                    return new System.Net.Sockets.NetworkStream(socket, ownsSocket: true);
                },
            });

        using var response = await invoker.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "http://bücher.de/"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["xn--bcher-kva.de"], resolved);
        Assert.Contains("Host: xn--bcher-kva.de", listener.Requests.Single());
        Assert.Equal(["93.184.216.34"], connectedTo);
    }

    // The connection goes to the checked address, but the caller's request, and the response's, name the host it
    // asked for, so a Location the caller resolves against them reaches that host.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SendAsync_TheRequestAndTheResponse_NameTheHostNotTheAddress(bool followRedirects)
    {
        var (invoker, inner, _) = Guard(("example.test", ["93.184.216.34"]), followRedirects: followRedirects);
        var redirect = new HttpResponseMessage(HttpStatusCode.Found);
        redirect.Headers.Location = new Uri("/next", UriKind.Relative);
        inner.EnqueueResponse(redirect);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.test/start");

        using var response = await invoker.SendAsync(request, TestContext.Current.CancellationToken);

        var last = followRedirects ? "http://example.test/next" : "http://example.test/start";
        Assert.Equal(new Uri("http://example.test/start"), request.RequestUri);
        Assert.Equal(new Uri(last), response.RequestMessage!.RequestUri);
        Assert.All(inner.SentUris, sent => Assert.Equal("93.184.216.34", sent.Host));
    }

    // A send that fails still leaves the caller's request naming the host it asked for.
    [Fact]
    public async Task SendAsync_ASendThatFails_LeavesTheRequestNamingTheHost()
    {
        var (invoker, _, _) = Guard(("example.test", ["93.184.216.34"]), new Failing());
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.test/start");

        await Assert.ThrowsAsync<HttpRequestException>(() => invoker.SendAsync(request, TestContext.Current.CancellationToken));

        Assert.Equal(new Uri("http://example.test/start"), request.RequestUri);
    }

    // A relative Location resolves against the host name the request was for, so the next hop is resolved and checked
    // by that name and carries it in its Host header.
    [Fact]
    public async Task SendAsync_ARelativeRedirect_ResolvesAgainstTheHostName()
    {
        var (invoker, inner, resolved) = Guard(("example.test", ["93.184.216.34"]));
        var redirect = new HttpResponseMessage(HttpStatusCode.Found);
        redirect.Headers.Location = new Uri("/next", UriKind.Relative);
        inner.EnqueueResponse(redirect);

        using var response = await invoker.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "http://example.test/start"), TestContext.Current.CancellationToken);

        Assert.Equal(["example.test", "example.test"], resolved);
        Assert.Equal("example.test", inner.AllRequests[1].Headers.Host);
        Assert.Equal(new Uri("http://93.184.216.34/next"), inner.SentUris[1]);
    }

    // A handler below that readdresses the request is refused even when the response it gives names the checked
    // address.
    [Fact]
    public async Task SendAsync_AHandlerBelowReaddressingTheRequest_IsRefused()
    {
        var (invoker, _, _) = Guard(("example.test", ["93.184.216.34"]), new Readdressing(new Uri("http://169.254.169.254/")));

        await Assert.ThrowsAsync<SsrfProtectionException>(() => invoker.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "http://example.test/"), TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// A guard whose resolver answers each name in <paramref name="names"/> with its addresses, in front of
    /// <paramref name="below"/>, or else a <see cref="TestInnerHandler"/>; and the names it was asked to resolve.
    /// </summary>
    private static (HttpMessageInvoker Invoker, TestInnerHandler Inner, List<string> Resolved) Guard(
        (string Name, string[] Addresses) names,
        HttpMessageHandler? below = null,
        bool followRedirects = true)
    {
        var inner = new TestInnerHandler();
        var resolved = new List<string>();
        var handler = new SsrfProtectionHandler(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<SsrfProtectionHandler>.Instance,
            (host, _) =>
            {
                resolved.Add(host);
                return host == names.Name
                    ? Task.FromResult(names.Addresses.Select(IPAddress.Parse).ToArray())
                    : throw new System.Net.Sockets.SocketException((int)System.Net.Sockets.SocketError.HostNotFound);
            })
        {
            FollowRedirects = followRedirects,
            InnerHandler = below ?? inner,
        };

        return (new HttpMessageInvoker(handler), inner, resolved);
    }

    /// <summary>
    /// Fails every send, as a connection that can't be made does.
    /// </summary>
    private sealed class Failing : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("Connection refused.");
    }

    /// <summary>
    /// Readdresses the request to <paramref name="to"/> and answers with a response whose request still names the
    /// address it was sent to.
    /// </summary>
    private sealed class Readdressing(Uri to) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var sent = request.RequestUri;
            request.RequestUri = to;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = new HttpRequestMessage(request.Method, sent) });
        }
    }

    /// <summary>
    /// Answers every request with 200 and keeps each request's head as it arrived.
    /// </summary>
    private sealed class RecordingListener : IDisposable
    {
        private readonly System.Net.Sockets.TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stopping = new();
        private readonly System.Collections.Concurrent.ConcurrentQueue<string> _requests = new();

        public RecordingListener()
        {
            _listener.Start();
            _ = ServeAsync();
        }

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        public IReadOnlyCollection<string> Requests => _requests;

        private async Task ServeAsync()
        {
            try
            {
                while (!_stopping.IsCancellationRequested)
                {
                    using var connection = await _listener.AcceptTcpClientAsync(_stopping.Token);
                    var stream = connection.GetStream();
                    var reader = new StreamReader(stream, System.Text.Encoding.ASCII);
                    var head = new System.Text.StringBuilder();
                    string? line;
                    while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync(_stopping.Token)))
                    {
                        head.Append(line).Append('\n');
                    }

                    _requests.Enqueue(head.ToString());
                    await stream.WriteAsync(
                        System.Text.Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"),
                        _stopping.Token);
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
