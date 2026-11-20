using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Net;
using System.Text;
using Bitwarden.Server.Sdk.Context;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using static Bitwarden.Server.Sdk.Client.Tests.PipelineHarness;

namespace Bitwarden.Server.Sdk.Client.Tests;

/// <summary>
/// A service client answers a repeated read from the request memo while one is current: once per question asked, as
/// exactly what was sent asked it; never a write, a failure or a call that isn't a Bitwarden service's.
/// </summary>
public sealed class RequestMemoHandlerTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static Func<HttpRequestMessage, HttpResponseMessage> Body(string text) => _ =>
        new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(text, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task Get_TwiceInOneMemo_SentOnce()
    {
        var callee = new Scripted(Body("""{"n":1}"""), Body("""{"n":2}"""));
        await using var services = Services(callee);
        var probe = services.GetRequiredService<Probe>();
        using var memo = RequestMemo.Begin();

        using var first = await probe.SendAsync(HttpMethod.Get, Token);
        using var second = await probe.SendAsync(HttpMethod.Get, Token);

        Assert.Single(callee.Attempts);
        Assert.Equal("""{"n":1}""", await second.Content.ReadAsStringAsync(Token));
    }

    [Fact]
    public async Task Get_NoMemo_SentEachTime()
    {
        var callee = new Scripted(Ok);
        await using var services = Services(callee);
        var probe = services.GetRequiredService<Probe>();

        using var first = await probe.SendAsync(HttpMethod.Get, Token);
        using var second = await probe.SendAsync(HttpMethod.Get, Token);

        Assert.Equal(2, callee.Attempts.Count);
    }

    [Fact]
    public async Task Get_AfterTheMemoEnded_SentEachTime()
    {
        var callee = new Scripted(Ok);
        await using var services = Services(callee);
        var probe = services.GetRequiredService<Probe>();
        var memo = RequestMemo.Begin();
        memo.Dispose();

        using var first = await probe.SendAsync(HttpMethod.Get, Token);
        using var second = await probe.SendAsync(HttpMethod.Get, Token);

        Assert.Equal(2, callee.Attempts.Count);
    }

    [Fact]
    public async Task Get_SwitchedOff_SentEachTime()
    {
        var callee = new Scripted(Ok);
        await using var services = Services(callee, settings: new Dictionary<string, string?> { [RequestMemo.EnabledKey] = "false" });
        var probe = services.GetRequiredService<Probe>();
        using var memo = RequestMemo.Begin();

        using var first = await probe.SendAsync(HttpMethod.Get, Token);
        using var second = await probe.SendAsync(HttpMethod.Get, Token);

        Assert.Equal(2, callee.Attempts.Count);
    }

    [Theory]
    [InlineData("things?fields[thing]=name", "things?fields[thing]=size")]
    [InlineData("things?filter[owner]=a", "things?filter[owner]=b")]
    [InlineData("things?page[number]=1", "things?page[number]=2")]
    [InlineData("things/a", "things/b")]
    public async Task Get_DifferentAddresses_EachSent(string one, string other)
    {
        var callee = new Scripted(Ok);
        await using var services = Services(callee);
        var probe = services.GetRequiredService<Probe>();
        using var memo = RequestMemo.Begin();

        using var first = await probe.SendAsync(HttpMethod.Get, Token, address: one);
        using var second = await probe.SendAsync(HttpMethod.Get, Token, address: other);
        using var again = await probe.SendAsync(HttpMethod.Get, Token, address: one);

        Assert.Equal(2, callee.Attempts.Count);
    }

    [Fact]
    public async Task Get_AboutAnotherOrganization_SentAgain()
    {
        var callee = new Scripted(Ok);
        await using var services = Services(callee);
        var probe = services.GetRequiredService<Probe>();
        using var memo = RequestMemo.Begin();
        var organization = Guid.NewGuid();

        using var first = await probe.SendAsync(HttpMethod.Get, Token);
        using (OutgoingCall.AsSystem(organization))
        {
            using var second = await probe.SendAsync(HttpMethod.Get, Token);
        }

        using (OutgoingCall.AsSystem(organization))
        {
            using var third = await probe.SendAsync(HttpMethod.Get, Token);
        }

        Assert.Equal(new string?[] { null, organization.ToString() }, callee.Attempts.Select(attempt => attempt.Organization).ToArray());
    }

    [Fact]
    public async Task Get_AfterAWrite_SentAgain()
    {
        var callee = new Scripted(Ok);
        await using var services = Services(callee);
        var probe = services.GetRequiredService<Probe>();
        using var memo = RequestMemo.Begin();

        using var first = await probe.SendAsync(HttpMethod.Get, Token);
        using var write = await probe.SendAsync(HttpMethod.Post, Token, body: new { name = "x" });
        using var second = await probe.SendAsync(HttpMethod.Get, Token);
        using var third = await probe.SendAsync(HttpMethod.Get, Token);

        Assert.Equal(3, callee.Attempts.Count);
    }

    [Theory]
    [InlineData("OPTIONS")]
    [InlineData("TRACE")]
    public async Task SafeMethodThatIsntARead_ForgetsNothingAndIsntRemembered(string method)
    {
        var callee = new Scripted(Ok);
        await using var services = Services(callee);
        var probe = services.GetRequiredService<Probe>();
        using var memo = RequestMemo.Begin();

        using var first = await probe.SendAsync(HttpMethod.Get, Token);
        using var safe = await probe.SendAsync(new HttpMethod(method), Token);
        using var again = await probe.SendAsync(new HttpMethod(method), Token);
        using var second = await probe.SendAsync(HttpMethod.Get, Token);

        Assert.Equal(3, callee.Attempts.Count);
    }

    [Fact]
    public async Task Get_AfterAWriteThatFailed_SentAgain()
    {
        var callee = new Scripted(Ok, ErrorDocument(HttpStatusCode.InternalServerError), Ok);
        await using var services = Services(callee);
        var probe = services.GetRequiredService<Probe>();
        using var memo = RequestMemo.Begin();

        using var first = await probe.SendAsync(HttpMethod.Get, Token);
        await Assert.ThrowsAnyAsync<Exception>(() => probe.SendAsync(HttpMethod.Delete, Token));
        using var second = await probe.SendAsync(HttpMethod.Get, Token);

        Assert.Equal(3, callee.Attempts.Count);
    }

    [Fact]
    public async Task Get_AfterForget_SentAgain()
    {
        var callee = new Scripted(Ok);
        await using var services = Services(callee);
        var probe = services.GetRequiredService<Probe>();
        using var memo = RequestMemo.Begin();

        using var first = await probe.SendAsync(HttpMethod.Get, Token);
        RequestMemo.Forget();
        using var second = await probe.SendAsync(HttpMethod.Get, Token);

        Assert.Equal(2, callee.Attempts.Count);
    }

    [Fact]
    public async Task Get_Fresh_SentAndItsAnswerRemembered()
    {
        var callee = new Scripted(Body("1"), Body("2"), Body("3"));
        await using var services = Services(callee);
        var probe = services.GetRequiredService<Probe>();
        using var memo = RequestMemo.Begin();

        using var first = await probe.SendAsync(HttpMethod.Get, Token);
        HttpResponseMessage fresh;
        using (OutgoingCall.Fresh())
        {
            fresh = await probe.SendAsync(HttpMethod.Get, Token);
        }

        using var after = await probe.SendAsync(HttpMethod.Get, Token);

        Assert.Equal(2, callee.Attempts.Count);
        Assert.Equal("2", await fresh.Content.ReadAsStringAsync(Token));
        Assert.Equal("2", await after.Content.ReadAsStringAsync(Token));
        fresh.Dispose();
    }

    [Fact]
    public async Task Get_RemembersNotFound_AsTheSameRefusal()
    {
        var callee = new Scripted(ErrorDocument(HttpStatusCode.NotFound));
        await using var services = Services(callee);
        var probe = services.GetRequiredService<Probe>();
        using var memo = RequestMemo.Begin();

        var first = await Assert.ThrowsAnyAsync<Exception>(() => probe.SendAsync(HttpMethod.Get, Token));
        var second = await Assert.ThrowsAnyAsync<Exception>(() => probe.SendAsync(HttpMethod.Get, Token));

        Assert.Single(callee.Attempts);
        Assert.Equal(first.GetType(), second.GetType());
        Assert.Equal(first.Message, second.Message);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, 1)]
    [InlineData(HttpStatusCode.NoContent, 1)]
    [InlineData(HttpStatusCode.NotFound, 1)]
    [InlineData(HttpStatusCode.BadRequest, 2)]
    [InlineData(HttpStatusCode.Forbidden, 2)]
    [InlineData(HttpStatusCode.Conflict, 2)]
    [InlineData(HttpStatusCode.TooManyRequests, 2)]
    [InlineData(HttpStatusCode.InternalServerError, 2)]
    [InlineData(HttpStatusCode.ServiceUnavailable, 2)]
    public async Task Get_KeepsOnlySuccessAndNotFound(HttpStatusCode status, int sent)
    {
        var inner = new Counting(_ => new HttpResponseMessage(status) { Content = new StringContent("x") });
        using var invoker = Invoker(inner);
        using var memo = RequestMemo.Begin();

        (await invoker.SendAsync(Get(), Token)).Dispose();
        (await invoker.SendAsync(Get(), Token)).Dispose();

        Assert.Equal(sent, inner.Sent);
    }

    [Fact]
    public async Task Get_ATransportFailure_NotKept()
    {
        var calls = 0;
        var inner = new Counting(_ => Interlocked.Increment(ref calls) == 1
            ? throw new HttpRequestException("connection reset")
            : new HttpResponseMessage(HttpStatusCode.OK));
        using var invoker = Invoker(inner);
        using var memo = RequestMemo.Begin();

        await Assert.ThrowsAsync<HttpRequestException>(() => invoker.SendAsync(Get(), Token));
        (await invoker.SendAsync(Get(), Token)).Dispose();

        Assert.Equal(2, inner.Sent);
    }

    [Fact]
    public async Task Get_EachHitIsItsOwnResponse()
    {
        var inner = new Counting(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"name":"widget"}""", Encoding.UTF8, "application/json"),
            Headers = { ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"3\"") },
        });
        using var invoker = Invoker(inner);
        using var memo = RequestMemo.Begin();

        var first = await invoker.SendAsync(Get(), Token);
        var firstBody = await first.Content.ReadAsStringAsync(Token);
        first.Dispose();
        using var second = await invoker.SendAsync(Get(), Token);
        using var third = await invoker.SendAsync(Get(), Token);

        Assert.NotSame(second, third);
        Assert.NotSame(second.Content, third.Content);
        Assert.Equal(firstBody, await second.Content.ReadAsStringAsync(Token));
        Assert.Equal(firstBody, await third.Content.ReadAsStringAsync(Token));
        Assert.Equal("application/json", third.Content.Headers.ContentType?.MediaType);
        Assert.Equal("\"3\"", third.Headers.ETag?.Tag);
        Assert.Equal(1, inner.Sent);
    }

    [Fact]
    public async Task Get_InFlightTwice_SentOnceAndBothAnswered()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var arrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var inner = new Counting(
            _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("shared") },
            async () =>
            {
                arrived.TrySetResult();
                await gate.Task;
            });
        using var invoker = Invoker(inner);
        using var memo = RequestMemo.Begin();

        var first = invoker.SendAsync(Get(), Token);
        await arrived.Task.WaitAsync(Token);
        var second = invoker.SendAsync(Get(), Token);
        gate.SetResult();

        using var one = await first;
        using var two = await second;
        Assert.Equal(1, inner.Sent);
        Assert.Equal("shared", await one.Content.ReadAsStringAsync(Token));
        Assert.Equal("shared", await two.Content.ReadAsStringAsync(Token));
    }

    [Fact]
    public async Task Get_InFlightTwiceAndFailing_BothFailAndNothingKept()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var arrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var inner = new Counting(
            _ => Interlocked.Increment(ref calls) == 1
                ? throw new HttpRequestException("connection reset")
                : new HttpResponseMessage(HttpStatusCode.OK),
            async () =>
            {
                arrived.TrySetResult();
                await gate.Task;
            });
        using var invoker = Invoker(inner);
        using var memo = RequestMemo.Begin();

        var first = invoker.SendAsync(Get(), Token);
        await arrived.Task.WaitAsync(Token);
        var second = invoker.SendAsync(Get(), Token);
        gate.SetResult();

        await Assert.ThrowsAsync<HttpRequestException>(() => first);
        await Assert.ThrowsAsync<HttpRequestException>(() => second);
        (await invoker.SendAsync(Get(), Token)).Dispose();
        Assert.Equal(2, inner.Sent);
    }

    [Fact]
    public async Task Get_InFlightTwiceAndRefused_BothRefusedAndNothingKept()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var arrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var inner = new Counting(
            _ => new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("broken") },
            async () =>
            {
                arrived.TrySetResult();
                await gate.Task;
            });
        using var invoker = Invoker(inner);
        using var memo = RequestMemo.Begin();

        var first = invoker.SendAsync(Get(), Token);
        await arrived.Task.WaitAsync(Token);
        var second = invoker.SendAsync(Get(), Token);
        gate.SetResult();

        using var one = await first;
        using var two = await second;
        Assert.Equal(HttpStatusCode.InternalServerError, one.StatusCode);
        Assert.Equal("broken", await two.Content.ReadAsStringAsync(Token));
        Assert.Equal(1, inner.Sent);
        (await invoker.SendAsync(Get(), Token)).Dispose();
        Assert.Equal(2, inner.Sent);
    }

    [Fact]
    public async Task Get_PastTheEntryBound_SentEachTime()
    {
        var inner = new Counting(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var invoker = Invoker(inner);
        using var memo = RequestMemo.Begin(maxEntries: 1, maxBytes: RequestMemo.DefaultMaxBytes);

        (await invoker.SendAsync(Get("a"), Token)).Dispose();
        (await invoker.SendAsync(Get("b"), Token)).Dispose();
        (await invoker.SendAsync(Get("b"), Token)).Dispose();
        (await invoker.SendAsync(Get("a"), Token)).Dispose();

        Assert.Equal(3, inner.Sent);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Get_LargerThanTheByteBound_PassedOnWholeAndNotKept(bool lengthKnown)
    {
        var text = new string('x', 100_000);
        var inner = new Counting(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = lengthKnown ? new StringContent(text) : new StreamContent(new UnknownLength(Encoding.UTF8.GetBytes(text))),
        });
        using var invoker = Invoker(inner);
        using var memo = RequestMemo.Begin(maxEntries: 10, maxBytes: 1_000);

        using var first = await invoker.SendAsync(Get(), Token);
        using var second = await invoker.SendAsync(Get(), Token);

        Assert.Equal(text, await first.Content.ReadAsStringAsync(Token));
        Assert.Equal(text, await second.Content.ReadAsStringAsync(Token));
        Assert.Equal(2, inner.Sent);
    }

    [Fact]
    public async Task Get_PastTheByteBoundInTotal_LaterAnswersNotKept()
    {
        var inner = new Counting(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(new string('x', 600)) });
        using var invoker = Invoker(inner);
        using var memo = RequestMemo.Begin(maxEntries: 10, maxBytes: 1_000);

        (await invoker.SendAsync(Get("a"), Token)).Dispose();
        (await invoker.SendAsync(Get("b"), Token)).Dispose();
        (await invoker.SendAsync(Get("a"), Token)).Dispose();
        (await invoker.SendAsync(Get("b"), Token)).Dispose();

        Assert.Equal(3, inner.Sent);
    }

    [Fact]
    public async Task Get_DelegatedAndAsserted_EachSent()
    {
        var inner = new Counting(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var invoker = Invoker(inner, setAsserted: null);
        using var memo = RequestMemo.Begin();

        (await invoker.SendAsync(Get(asserted: false), Token)).Dispose();
        (await invoker.SendAsync(Get(asserted: true), Token)).Dispose();
        (await invoker.SendAsync(Get(asserted: false), Token)).Dispose();
        (await invoker.SendAsync(Get(asserted: true), Token)).Dispose();

        Assert.Equal(2, inner.Sent);
    }

    [Fact]
    public async Task Get_WithoutTheContextHandlersDecision_NeverRemembered()
    {
        var inner = new Counting(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var invoker = Invoker(inner, setAsserted: null);
        using var memo = RequestMemo.Begin();

        (await invoker.SendAsync(Get(asserted: null), Token)).Dispose();
        (await invoker.SendAsync(Get(asserted: null), Token)).Dispose();

        Assert.Equal(2, inner.Sent);
    }

    [Theory]
    [InlineData(BitwardenHeaders.User)]
    [InlineData(BitwardenHeaders.Organization)]
    [InlineData(BitwardenHeaders.Provider)]
    [InlineData(BitwardenHeaders.Context)]
    [InlineData(BitwardenHeaders.CallChain)]
    public async Task Get_DifferentContextHeader_EachSent(string header)
    {
        var inner = new Counting(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var invoker = Invoker(inner);
        using var memo = RequestMemo.Begin();

        foreach (var value in new[] { "one", "two", "one" })
        {
            var request = Get();
            request.Headers.TryAddWithoutValidation(header, value);
            (await invoker.SendAsync(request, Token)).Dispose();
        }

        Assert.Equal(2, inner.Sent);
    }

    // The credentials differ from call to call; the key carries the decision, never the token.
    [Fact]
    public async Task Get_DifferentTokensSameDecision_SentOnce()
    {
        var inner = new Counting(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var invoker = Invoker(inner);
        using var memo = RequestMemo.Begin();

        foreach (var token in new[] { "first", "second" })
        {
            var request = Get();
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            request.Headers.TryAddWithoutValidation(BitwardenHeaders.Delegation, token);
            request.Headers.TryAddWithoutValidation("traceparent", $"00-{Guid.NewGuid():N}-0000000000000001-01");
            (await invoker.SendAsync(request, Token)).Dispose();
        }

        Assert.Equal(1, inner.Sent);
    }

    [Fact]
    public async Task Get_IdentitysAddresses_NeverRemembered()
    {
        var inner = new Counting(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var invoker = Invoker(inner);
        using var memo = RequestMemo.Begin();

        (await invoker.SendAsync(Get("connect/token"), Token)).Dispose();
        (await invoker.SendAsync(Get("connect/token"), Token)).Dispose();

        Assert.Equal(2, inner.Sent);
    }

    // The token client is its own, outside any service client's pipeline: nothing it sends passes through the memo.
    [Fact]
    public async Task Get_ThroughTheTokenClient_NeverRemembered()
    {
        var identity = new Counting(_ => new HttpResponseMessage(HttpStatusCode.OK));
        await using var services = Services(new Scripted(Ok), identity: identity);
        using var tokenClient = services.GetRequiredService<IHttpClientFactory>().CreateClient(ServiceTokenProvider.TokenClientName);
        using var memo = RequestMemo.Begin();

        (await tokenClient.GetAsync("http://identity.invalid/.well-known/openid-configuration", Token)).Dispose();
        (await tokenClient.GetAsync("http://identity.invalid/.well-known/openid-configuration", Token)).Dispose();

        Assert.Equal(2, identity.Sent);
    }

    [Fact]
    public async Task Get_ThroughAnotherHttpClient_NeverRemembered()
    {
        var outside = new Counting(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var services = new ServiceCollection();
        services.AddHttpClient("stripe").ConfigurePrimaryHttpMessageHandler(() => outside);
        await using var provider = services.BuildServiceProvider();
        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("stripe");
        using var memo = RequestMemo.Begin();

        (await client.GetAsync("https://api.stripe.invalid/v1/customers/cus_1", Token)).Dispose();
        (await client.GetAsync("https://api.stripe.invalid/v1/customers/cus_1", Token)).Dispose();

        Assert.Equal(2, outside.Sent);
    }

    [Fact]
    public async Task Get_Counted()
    {
        var outcomes = new ConcurrentQueue<string>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meters) =>
        {
            if (instrument.Meter.Name == RequestMemoMetrics.MeterName)
            {
                meters.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            var named = tags.ToArray().ToDictionary(tag => tag.Key, tag => tag.Value);
            if (Equals(named[RequestMemoMetrics.ServiceTag], "counted-service"))
            {
                outcomes.Enqueue((string)named[RequestMemoMetrics.OutcomeTag]!);
            }
        });
        listener.Start();

        var inner = new Counting(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var invoker = Invoker(inner, service: "counted-service");
        using var memo = RequestMemo.Begin();

        (await invoker.SendAsync(Get(), Token)).Dispose();
        (await invoker.SendAsync(Get(), Token)).Dispose();

        Assert.Equal(["miss", "hit"], outcomes.ToArray());
    }

    [Fact]
    public async Task ProductCall_TwiceWithOneToken_SentOnce_AndWithAnother_SentAgain()
    {
        var inner = new Counting(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
        using var invoker = new HttpMessageInvoker(new ProductCallHandler
        {
            InnerHandler = new RequestMemoHandler("product-service", string.Empty, configuration: null, Metrics, product: true) { InnerHandler = inner },
        });
        using var memo = RequestMemo.Begin();

        foreach (var bearer in new[] { "person-a", "person-a", "person-b" })
        {
            var call = ProductCall.Begin(bearer);
            try
            {
                (await invoker.SendAsync(Get(), Token)).Dispose();
            }
            finally
            {
                call.End();
            }
        }

        // Not part of a product call: passed through, never remembered.
        (await invoker.SendAsync(Get(), Token)).Dispose();
        (await invoker.SendAsync(Get(), Token)).Dispose();

        Assert.Equal(4, inner.Sent);
    }

    [Fact]
    public async Task Get_AfterAProductClientsWriteOutsideAProductCall_SentAgain()
    {
        var inner = new Counting(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
        using var service = Invoker(inner, setAsserted: true);
        using var product = new HttpMessageInvoker(new ProductCallHandler
        {
            InnerHandler = new RequestMemoHandler("product-service", string.Empty, configuration: null, Metrics, product: true) { InnerHandler = inner },
        });
        using var memo = RequestMemo.Begin();

        (await service.SendAsync(Get(), Token)).Dispose();
        (await product.SendAsync(new HttpRequestMessage(HttpMethod.Post, "http://product.invalid/api/v1/things"), Token)).Dispose();
        (await service.SendAsync(Get(), Token)).Dispose();

        Assert.Equal(3, inner.Sent);
    }

    private static readonly RequestMemoMetrics Metrics =
        new(new ServiceCollection().AddMetrics().BuildServiceProvider().GetRequiredService<IMeterFactory>());

    private static HttpMessageInvoker Invoker(HttpMessageHandler inner, bool? setAsserted = false, string service = Callee) =>
        new(new AssertedAs(setAsserted)
        {
            InnerHandler = new RequestMemoHandler(service, "caller", new ConfigurationBuilder().Build(), Metrics) { InnerHandler = inner },
        });

    private static HttpRequestMessage Get(string path = "things", bool? asserted = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"http://callee.invalid/api/v1/{path}");
        if (asserted is { } decided)
        {
            request.Options.Set(ContextHandler.Asserted, decided);
        }

        return request;
    }

    /// <summary>
    /// Stands for the context handler's decision, which the memo's key needs.
    /// </summary>
    private sealed class AssertedAs(bool? asserted) : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (asserted is { } decided && !request.Options.TryGetValue(ContextHandler.Asserted, out _))
            {
                request.Options.Set(ContextHandler.Asserted, decided);
            }

            return base.SendAsync(request, cancellationToken);
        }
    }

    private sealed class Counting(Func<HttpRequestMessage, HttpResponseMessage> answer, Func<Task>? before = null) : HttpMessageHandler
    {
        private int _sent;

        public int Sent => Volatile.Read(ref _sent);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _sent);
            if (before is not null)
            {
                await before();
            }

            return answer(request);
        }
    }

    /// <summary>
    /// A body whose length isn't known until it is read, as a chunked answer's.
    /// </summary>
    private sealed class UnknownLength(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;

        public override long Length => throw new NotSupportedException();
    }
}
