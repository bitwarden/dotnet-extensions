using System.Net;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bitwarden.Server.Sdk.Http.Tests;

public class SsrfProtectionHandlerTests
{
    /// <summary>
    /// A test handler standing in for the guard's sender: it captures requests and returns canned responses.
    /// </summary>
    private class TestInnerHandler : DelegatingHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        public List<HttpRequestMessage> AllRequests { get; } = [];

        /// <summary>
        /// Each request's address as it was sent.
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
    /// A client sending through a guard that resolves names with the system's DNS, in front of a
    /// <see cref="TestInnerHandler"/>.
    /// </summary>
    private static (HttpClient client, TestInnerHandler inner) CreateClient(bool followRedirects = true)
    {
        var inner = new TestInnerHandler();
        var guard = new SsrfProtectionHandler(NullLogger<SsrfProtectionHandler>.Instance, new SsrfConnector())
        {
            FollowRedirects = followRedirects,
            InnerHandler = inner,
            Sender = inner,
        };

        return (new HttpClient(guard), inner);
    }

    [Theory]
    [InlineData("http://0.0.0.0/test")]
    [InlineData("http://127.0.0.2/test")]
    [InlineData("http://10.0.0.1/test")]
    [InlineData("http://169.254.169.254/latest/meta-data/")] // AWS metadata
    [InlineData("http://100.64.0.1/test")]                   // CGNAT
    [InlineData("http://[::1]/test")]                         // IPv6 loopback
    [InlineData("http://[::ffff:0:a00:1]/test")]              // SIIT 10.0.0.1
    public async Task SendAsync_DirectIpInternal_ThrowsSsrfProtectionException(string url)
    {
        // IPAddressExtensionsTests covers every range; these show an address in a URL is checked as written.
        var (client, inner) = CreateClient();

        await Assert.ThrowsAsync<SsrfProtectionException>(
            () => client.GetAsync(url, TestContext.Current.CancellationToken));
        Assert.Empty(inner.AllRequests);
    }

    // A public address in a URL is sent, pinned to itself.
    [Theory]
    [InlineData("http://8.8.8.8/test", "8.8.8.8")]
    [InlineData("http://[2606:4700::1111]/test", "2606:4700::1111")]
    public async Task SendAsync_DirectIpPublic_IsSentPinnedToIt(string url, string address)
    {
        var (client, inner) = CreateClient();

        var response = await client.GetAsync(url, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(IPAddress.Parse(address), Pinned(inner.LastRequest!).Address);
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

    // An attacker-controlled site redirecting to the AWS metadata endpoint, or to the loopback address: the hop is
    // refused before it is sent.
    [Theory]
    [InlineData(HttpStatusCode.Found, "http://169.254.169.254/latest/meta-data/")]
    [InlineData(HttpStatusCode.MovedPermanently, "http://127.0.0.1/admin")]
    public async Task SendAsync_RedirectToInternalIp_ThrowsSsrfProtectionException(HttpStatusCode status, string location)
    {
        var (client, inner) = CreateClient();
        var redirectResponse = new HttpResponseMessage(status);
        redirectResponse.Headers.Location = new Uri(location);
        inner.EnqueueResponse(redirectResponse);

        await Assert.ThrowsAsync<SsrfProtectionException>(
            () => client.GetAsync("http://8.8.8.8/attacker", TestContext.Current.CancellationToken));
        Assert.Single(inner.AllRequests);
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
        // The relative redirect should resolve against 1.1.1.1 (the current hop), not 8.8.8.8 (the original request).
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
        Assert.Equal(new Uri("http://1.1.1.1/relative-path"), inner.SentUris[2]);
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
    // the request has gone out by then, and the guard refuses the answer rather than hand it back.
    [Fact]
    public async Task SendAsync_AHandlerBelowFollowingARedirect_IsRefused()
    {
        var (invoker, _, _) = Guard(new FakeNetwork(null), new FollowingHandler(new Uri("http://169.254.169.254/latest/meta-data/")));

        await Assert.ThrowsAsync<SsrfProtectionException>(() => invoker.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "http://8.8.8.8/start"), TestContext.Current.CancellationToken));
    }

    // A handler below that readdresses the request is refused even when the response it gives names the checked
    // address.
    [Fact]
    public async Task SendAsync_AHandlerBelowReaddressingTheRequest_IsRefused()
    {
        var (invoker, _, _) = Guard(Names(), new Readdressing(new Uri("http://169.254.169.254/")));

        await Assert.ThrowsAsync<SsrfProtectionException>(() => invoker.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "http://example.test/"), TestContext.Current.CancellationToken));
    }

    // A request with no address is refused rather than handed on.
    [Fact]
    public async Task SendAsync_NoRequestUri_ThrowsSsrfProtectionException()
    {
        var (invoker, inner, _) = Guard(Names());
        using var request = new HttpRequestMessage(HttpMethod.Get, (Uri?)null);

        var exception = await Assert.ThrowsAsync<SsrfProtectionException>(
            () => invoker.SendAsync(request, TestContext.Current.CancellationToken));

        Assert.Equal("Request URI is null.", exception.Message);
        Assert.Empty(inner.AllRequests);
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

    // A body is read only once the first hop is checked: a refused request reads none of it.
    [Fact]
    public async Task SendAsync_ARefusedRequest_ReadsNoBody()
    {
        var (invoker, _, _) = Guard(Names(("example.test", ["10.0.0.1"])));
        var body = new CountingStream();

        await Assert.ThrowsAsync<SsrfProtectionException>(() => invoker.SendAsync(
            new HttpRequestMessage(HttpMethod.Post, "http://example.test/") { Content = new StreamContent(body) },
            TestContext.Current.CancellationToken));

        Assert.Equal(0, body.Reads);
    }

    // A request that may use HTTP/3 is refused: QUIC connections don't go through the connector, so they couldn't
    // be pinned to the checked address.
    [Theory]
    [InlineData(3, HttpVersionPolicy.RequestVersionOrLower)]
    [InlineData(1, HttpVersionPolicy.RequestVersionOrHigher)]
    public async Task SendAsync_ARequestThatMayUseHttp3_IsRefused(int major, HttpVersionPolicy policy)
    {
        var (invoker, inner, _) = Guard(Names());

        await Assert.ThrowsAsync<SsrfProtectionException>(() => invoker.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "http://8.8.8.8/") { Version = new Version(major, major == 1 ? 1 : 0), VersionPolicy = policy },
            TestContext.Current.CancellationToken));
        Assert.Empty(inner.AllRequests);
    }

    // A guard that wasn't set up by AddSsrfProtection, or whose sender was swapped or changed since, sends nothing.
    [Fact]
    public async Task SendAsync_WithoutASender_Throws()
    {
        var inner = new TestInnerHandler();
        using var invoker = new HttpMessageInvoker(
            new SsrfProtectionHandler(NullLogger<SsrfProtectionHandler>.Instance, new FakeNetwork(null)) { InnerHandler = inner });

        await Assert.ThrowsAsync<InvalidOperationException>(() => invoker.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "http://8.8.8.8/"), TestContext.Current.CancellationToken));
        Assert.Empty(inner.AllRequests);
    }

    [Fact]
    public async Task SendAsync_ASenderThatIsNotBelowTheGuard_Throws()
    {
        var inner = new TestInnerHandler();
        using var invoker = new HttpMessageInvoker(
            new SsrfProtectionHandler(NullLogger<SsrfProtectionHandler>.Instance, new FakeNetwork(null))
            {
                InnerHandler = inner,
                Sender = new TestInnerHandler(),
            });

        await Assert.ThrowsAsync<InvalidOperationException>(() => invoker.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "http://8.8.8.8/"), TestContext.Current.CancellationToken));
        Assert.Empty(inner.AllRequests);
    }

    [Theory]
    [InlineData("redirects")]
    [InlineData("cookies")]
    [InlineData("connect")]
    [InlineData("proxy")]
    public async Task SendAsync_ASenderChangedAfterItWasSetUp_Throws(string change)
    {
        var network = new FakeNetwork(null);
        var sender = network.Sender();
        switch (change)
        {
            case "redirects":
                sender.AllowAutoRedirect = true;
                break;
            case "cookies":
                sender.UseCookies = true;
                break;
            case "connect":
                sender.ConnectCallback = (_, _) => throw new InvalidOperationException("Never connects.");
                break;
            case "proxy":
                // The process's default proxy, left to be read later, rather than one fixed.
                sender.UseProxy = true;
                break;
        }

        var (invoker, _, _) = Guard(network, sender);

        await Assert.ThrowsAsync<InvalidOperationException>(() => invoker.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "http://8.8.8.8/"), TestContext.Current.CancellationToken));
        Assert.Empty(network.Opened);
    }

    // The request keeps its host, for TLS, cookies and telemetry; the guard records the address it checked, and the
    // connection is made to that address, never to the name, which could resolve elsewhere by then.
    [Fact]
    public async Task SendAsync_AName_IsConnectedToTheAddressItWasCheckedAt()
    {
        using var server = new ScriptedServer();
        var network = new FakeNetwork(server, ("example.test", ["93.184.216.34"]));
        var (invoker, _, _) = Guard(network, network.Sender());

        using var response = await invoker.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "http://example.test/path?query=1"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal([new IPEndPoint(IPAddress.Parse("93.184.216.34"), 80)], network.Opened);
        Assert.Equal(["example.test"], network.Resolved);
        Assert.StartsWith("GET /path?query=1 HTTP/1.1\n", server.Requests.Single(), StringComparison.Ordinal);
        Assert.Contains("Host: example.test\n", server.Requests.Single(), StringComparison.Ordinal);
        Assert.Equal(new Uri("http://example.test/path?query=1"), response.RequestMessage!.RequestUri);
    }

    // Through a proxy, the request is addressed to the checked address, with the host in the Host header, so the proxy
    // has no name to resolve differently; the caller's request and the response's name the host again.
    [Fact]
    public async Task SendAsync_ThroughAProxy_IsAddressedToTheCheckedAddress()
    {
        using var server = new ScriptedServer();
        var network = new FakeNetwork(server, ("example.test", ["93.184.216.34"]));
        var (invoker, _, _) = Guard(network, network.Sender(new WebProxy("http://proxy.test:3128")));
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.test/path");

        using var response = await invoker.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal([new DnsEndPoint("proxy.test", 3128)], network.Opened);
        Assert.StartsWith("GET http://93.184.216.34/path HTTP/1.1\n", server.Requests.Single(), StringComparison.Ordinal);
        Assert.Contains("Host: example.test\n", server.Requests.Single(), StringComparison.Ordinal);
        Assert.Equal(new Uri("http://example.test/path"), request.RequestUri);
        Assert.Equal(new Uri("http://example.test/path"), response.RequestMessage!.RequestUri);
    }

    // Through a proxy, an https request's tunnel is opened to the checked address, so the proxy connects there.
    [Fact]
    public async Task SendAsync_ThroughAProxy_AnHttpsRequestTunnelsToTheCheckedAddress()
    {
        using var server = new ScriptedServer();
        var network = new FakeNetwork(server, ("example.test", ["93.184.216.34"]));
        var (invoker, _, _) = Guard(network, network.Sender(new WebProxy("http://proxy.test:3128")));

        // The server can't speak TLS, so the request fails once the tunnel is open.
        await Record.ExceptionAsync(() => invoker.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "https://example.test/"), TestContext.Current.CancellationToken));

        Assert.Equal([new DnsEndPoint("proxy.test", 3128)], network.Opened);
        Assert.StartsWith("CONNECT 93.184.216.34:443 HTTP/1.1\n", server.Requests[0], StringComparison.Ordinal);
    }

    // A host the proxy bypasses is connected to directly, pinned to the checked address.
    [Fact]
    public async Task SendAsync_AHostTheProxyBypasses_IsConnectedToDirectly()
    {
        using var server = new ScriptedServer();
        var network = new FakeNetwork(server, ("example.test", ["93.184.216.34"]));
        var proxy = new WebProxy("http://proxy.test:3128", BypassOnLocal: false, BypassList: [@"example\.test"]);
        var (invoker, _, _) = Guard(network, network.Sender(proxy));

        using var response = await invoker.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "http://example.test/path"), TestContext.Current.CancellationToken);

        Assert.Equal([new IPEndPoint(IPAddress.Parse("93.184.216.34"), 80)], network.Opened);
        Assert.StartsWith("GET /path HTTP/1.1\n", server.Requests.Single(), StringComparison.Ordinal);
    }

    // Through a proxy, a port that isn't the scheme's default stays in the Host header.
    [Theory]
    [InlineData("http://example.test:8080/", "example.test:8080", "http://93.184.216.34:8080/")]
    [InlineData("http://example.test/", "example.test", "http://93.184.216.34/")]
    public async Task SendAsync_ThroughAProxy_TheHostHeaderKeepsAPortThatIsNotTheDefault(string url, string hostHeader, string addressed)
    {
        using var server = new ScriptedServer();
        var network = new FakeNetwork(server, ("example.test", ["93.184.216.34"]));
        var (invoker, _, _) = Guard(network, network.Sender(new WebProxy("http://proxy.test:3128")));

        using var response = await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, url), TestContext.Current.CancellationToken);

        Assert.StartsWith($"GET {addressed} HTTP/1.1\n", server.Requests.Single(), StringComparison.Ordinal);
        Assert.Contains($"Host: {hostHeader}\n", server.Requests.Single(), StringComparison.Ordinal);
    }

    // Through a proxy, a Host header the caller set is sent as it is; the request is still addressed to the checked
    // address.
    [Fact]
    public async Task SendAsync_ThroughAProxy_AHostHeaderTheCallerSet_IsKept()
    {
        using var server = new ScriptedServer();
        var network = new FakeNetwork(server, ("example.test", ["93.184.216.34"]));
        var (invoker, _, _) = Guard(network, network.Sender(new WebProxy("http://proxy.test:3128")));
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.test/");
        request.Headers.Host = "virtual.example";

        using var response = await invoker.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.StartsWith("GET http://93.184.216.34/ HTTP/1.1\n", server.Requests.Single(), StringComparison.Ordinal);
        Assert.Contains("Host: virtual.example\n", server.Requests.Single(), StringComparison.Ordinal);
    }

    // Through a proxy, a redirect's relative Location resolves against the host the caller asked for, and the next
    // hop is resolved, checked and addressed by that name's address again.
    [Fact]
    public async Task SendAsync_ThroughAProxy_ARelativeRedirectResolvesAgainstTheHost()
    {
        using var server = new ScriptedServer("HTTP/1.1 302 Found\r\nLocation: /next");
        var network = new FakeNetwork(server, ("example.test", ["93.184.216.34"]));
        var (invoker, _, _) = Guard(network, network.Sender(new WebProxy("http://proxy.test:3128")));

        using var response = await invoker.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "http://example.test/start"), TestContext.Current.CancellationToken);

        Assert.Equal(["example.test", "example.test"], network.Resolved);
        Assert.StartsWith("GET http://93.184.216.34/next HTTP/1.1\n", server.Requests[1], StringComparison.Ordinal);
        Assert.Equal(new Uri("http://example.test/next"), response.RequestMessage!.RequestUri);
    }

    // Through a proxy, a send that fails still leaves the caller's request naming the host it asked for.
    [Fact]
    public async Task SendAsync_ThroughAProxy_ASendThatFails_LeavesTheRequestNamingTheHost()
    {
        var network = new FakeNetwork(null, ("example.test", ["93.184.216.34"]));
        var (invoker, _, _) = Guard(network, network.Sender(new WebProxy("http://proxy.test:3128")));
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.test/start");

        await Assert.ThrowsAsync<HttpRequestException>(() => invoker.SendAsync(request, TestContext.Current.CancellationToken));

        Assert.Equal(new Uri("http://example.test/start"), request.RequestUri);
    }

    // Every address a name resolves to is checked: one internal address among public ones refuses the request.
    [Theory]
    [InlineData("93.184.216.34", "10.0.0.1")]
    [InlineData("10.0.0.1", "93.184.216.34")]
    [InlineData("2606:4700::1111", "::1")]
    public async Task SendAsync_ANameResolvingToAnyInternalAddress_IsRefused(string first, string second)
    {
        var (invoker, inner, _) = Guard(Names(("example.test", [first, second])));

        await Assert.ThrowsAsync<SsrfProtectionException>(() => invoker.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "http://example.test/"), TestContext.Current.CancellationToken));

        Assert.Empty(inner.AllRequests);
    }

    // IPv4 is preferred when a name resolves to both families; a name resolving to IPv6 only is pinned to it.
    [Theory]
    [InlineData(new[] { "2606:4700::1111", "1.1.1.1" }, "1.1.1.1")]
    [InlineData(new[] { "2606:4700::1111" }, "2606:4700::1111")]
    public async Task SendAsync_AName_IsPinnedToIPv4WhenItHasOne(string[] addresses, string pinned)
    {
        var (invoker, inner, _) = Guard(Names(("example.test", addresses)));

        using var response = await invoker.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "http://example.test/"), TestContext.Current.CancellationToken);

        Assert.Equal(IPAddress.Parse(pinned), Pinned(inner.LastRequest!).Address);
    }

    // An internationalized name is resolved, and sent in the Host header, in its ASCII form, which is all a connection
    // accepts: SocketsHttpHandler refuses a header that isn't ASCII.
    [Fact]
    public async Task SendAsync_AnInternationalizedName_IsSentInItsAsciiForm()
    {
        using var server = new ScriptedServer();
        var network = new FakeNetwork(server, ("xn--bcher-kva.de", ["93.184.216.34"]));
        var (invoker, _, _) = Guard(network, network.Sender());

        using var response = await invoker.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "http://bücher.de/"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["xn--bcher-kva.de"], network.Resolved);
        Assert.Contains("Host: xn--bcher-kva.de\n", server.Requests.Single(), StringComparison.Ordinal);
        Assert.Equal([new IPEndPoint(IPAddress.Parse("93.184.216.34"), 80)], network.Opened);
    }

    // A relative Location resolves against the host name the request was for, so the next hop is resolved and checked
    // by that name and keeps it.
    [Fact]
    public async Task SendAsync_ARelativeRedirect_ResolvesAgainstTheHostName()
    {
        var (invoker, inner, network) = Guard(Names(("example.test", ["93.184.216.34"])));
        var redirect = new HttpResponseMessage(HttpStatusCode.Found);
        redirect.Headers.Location = new Uri("/next", UriKind.Relative);
        inner.EnqueueResponse(redirect);

        using var response = await invoker.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "http://example.test/start"), TestContext.Current.CancellationToken);

        Assert.Equal(["example.test", "example.test"], network.Resolved);
        Assert.Equal(new Uri("http://example.test/next"), inner.SentUris[1]);
        Assert.Equal(IPAddress.Parse("93.184.216.34"), Pinned(inner.AllRequests[1]).Address);
    }

    /// <summary>
    /// A network resolving each of <paramref name="names"/> to its addresses, with nothing to connect to.
    /// </summary>
    private static FakeNetwork Names(params (string Name, string[] Addresses)[] names) => new(null, names);

    /// <summary>
    /// The address the guard checked for <paramref name="request"/>.
    /// </summary>
    private static SsrfPin Pinned(HttpRequestMessage request) =>
        request.Options.TryGetValue(SsrfConnector.PinKey, out var pin) ? pin : throw new InvalidOperationException("Not pinned.");

    /// <summary>
    /// A guard resolving through <paramref name="network"/>, set up in front of <paramref name="below"/>, or else a
    /// <see cref="TestInnerHandler"/>, as its sender.
    /// </summary>
    private static (HttpMessageInvoker Invoker, TestInnerHandler Inner, FakeNetwork Network) Guard(
        FakeNetwork network,
        HttpMessageHandler? below = null,
        bool followRedirects = true)
    {
        var inner = new TestInnerHandler();
        var handler = new SsrfProtectionHandler(NullLogger<SsrfProtectionHandler>.Instance, network)
        {
            FollowRedirects = followRedirects,
            InnerHandler = below ?? inner,
            Sender = below ?? inner,
        };

        return (new HttpMessageInvoker(handler), inner, network);
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
    /// Follows a redirect to <paramref name="to"/> as a primary handler with automatic redirects would: the request
    /// it answers for is now addressed there.
    /// </summary>
    private sealed class FollowingHandler(Uri to) : HttpMessageHandler
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

    /// <summary>
    /// A stream counting the reads made of it.
    /// </summary>
    private sealed class CountingStream() : MemoryStream("body"u8.ToArray())
    {
        public int Reads { get; private set; }

        public override int Read(byte[] buffer, int offset, int count)
        {
            Reads++;
            return base.Read(buffer, offset, count);
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Reads++;
            return base.ReadAsync(buffer, cancellationToken);
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            Reads++;
            return base.ReadAsync(buffer, offset, count, cancellationToken);
        }
    }
}
