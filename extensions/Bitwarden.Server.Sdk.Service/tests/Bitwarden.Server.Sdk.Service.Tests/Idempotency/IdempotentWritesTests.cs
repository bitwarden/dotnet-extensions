using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Bitwarden.Server.Sdk.Client;
using Bitwarden.Server.Sdk.Context;
using Bitwarden.Server.Sdk.Idempotency;
using Bitwarden.Server.Sdk.Service.Tests.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bitwarden.Server.Sdk.Service.Tests.Idempotency;

/// <summary>
/// A write another service sends with an <c>Idempotency-Key</c> is done at most once: a repeat is answered with the
/// first answer, a repeat with another request is refused 422, one while the first runs 409. A browser's write, and a
/// write to an operation that doesn't accept keys, run every time. Run with and without the request delegate
/// generator (the MinimalApi test project).
/// </summary>
public sealed class IdempotentWritesTests : IAsyncLifetime
{
    private static readonly string ServiceToken = SessionHost.Token(user: null, SessionHost.Scope, SessionHost.WriteScope);

    private static readonly string PersonToken = SessionHost.Token(Guid.NewGuid(), SessionHost.Scope, SessionHost.WriteScope);

    private SessionHost _service = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private IdempotencyProbeCounter Counter => _service.Resolve<IdempotencyProbeCounter>();

    public async ValueTask InitializeAsync() => _service = await SessionHost.StartAsync(productService: false);

    public async ValueTask DisposeAsync() => await _service.DisposeAsync();

    [Fact]
    public async Task ARepeat_IsAnsweredAsTheFirstWasWithoutRunningAgain()
    {
        using var first = await SendAsync("POST", "/api/v1/idempotency-probes", "key-1", "a");
        using var repeat = await SendAsync("POST", "/api/v1/idempotency-probes", "key-1", "a");

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, repeat.StatusCode);
        Assert.Equal(first.Headers.Location, repeat.Headers.Location);
        Assert.Equal(first.Content.Headers.ContentType, repeat.Content.Headers.ContentType);
        Assert.Equal(await first.Content.ReadAsStringAsync(Token), await repeat.Content.ReadAsStringAsync(Token));
        Assert.False(first.Headers.Contains(IdempotencyKeys.ReplayedHeader));
        Assert.Equal("true", Assert.Single(repeat.Headers.GetValues(IdempotencyKeys.ReplayedHeader)));
        Assert.Equal(1, Counter.Count);
    }

    [Fact]
    public async Task ARepeatOfAReplace_IsAnsweredAsTheFirstWas()
    {
        using var first = await SendAsync("PUT", "/api/v1/idempotency-probes/one", "key-put", "a");
        using var repeat = await SendAsync("PUT", "/api/v1/idempotency-probes/one", "key-put", "a");

        Assert.Equal(HttpStatusCode.OK, repeat.StatusCode);
        Assert.Equal(await first.Content.ReadAsStringAsync(Token), await repeat.Content.ReadAsStringAsync(Token));
        Assert.Equal(1, Counter.Count);
    }

    [Fact]
    public async Task TwoKeys_RunTwice()
    {
        using var first = await SendAsync("POST", "/api/v1/idempotency-probes", "key-1", "a");
        using var second = await SendAsync("POST", "/api/v1/idempotency-probes", "key-2", "a");

        Assert.Equal(2, Counter.Count);
    }

    // A key names one request: another body, path or method under it is refused, and the first stands.
    [Theory]
    [InlineData("POST", "/api/v1/idempotency-probes", "b")]
    [InlineData("PUT", "/api/v1/idempotency-probes/one", "a")]
    [InlineData("POST", "/api/v1/idempotency-probes?extra=1", "a")]
    public async Task AKeyReusedForAnotherRequest_IsRefused422(string method, string path, string note)
    {
        using var first = await SendAsync("POST", "/api/v1/idempotency-probes", "key-1", "a");
        using var other = await SendAsync(method, path, "key-1", note);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, other.StatusCode);
        Assert.Contains(IdempotencyKeys.Header, await other.Content.ReadAsStringAsync(Token), StringComparison.Ordinal);
        Assert.Equal(1, Counter.Count);
    }

    [Fact]
    public async Task ARepeatWhileTheFirstRuns_IsRefused409_ThenReplayed()
    {
        var running = SendAsync("POST", "/api/v1/idempotency-probes/slow", "key-slow", "a");
        await WaitUntilAsync(() => Counter.Count == 1);

        using (var meanwhile = await SendAsync("POST", "/api/v1/idempotency-probes/slow", "key-slow", "a"))
        {
            Assert.Equal(HttpStatusCode.Conflict, meanwhile.StatusCode);
            Assert.Equal(TimeSpan.FromSeconds(IdempotentWrites.RetryAfterSeconds), meanwhile.Headers.RetryAfter?.Delta);
            Assert.Contains("\"code\":\"conflict\"", await meanwhile.Content.ReadAsStringAsync(Token), StringComparison.Ordinal);
        }

        Counter.Gate.SetResult();
        using var first = await running;
        using var after = await SendAsync("POST", "/api/v1/idempotency-probes/slow", "key-slow", "a");

        Assert.Equal(HttpStatusCode.OK, after.StatusCode);
        Assert.Equal(await first.Content.ReadAsStringAsync(Token), await after.Content.ReadAsStringAsync(Token));
        Assert.Equal(1, Counter.Count);
    }

    // A write that failed keeps nothing: its repeat runs again.
    [Theory]
    [InlineData("/api/v1/idempotency-probes/failing")]
    [InlineData("/api/v1/idempotency-probes/throwing")]
    public async Task AFailedWrite_RunsAgainOnItsRepeat(string path)
    {
        using var first = await SendAsync("POST", path, "key-failing", null);
        using var repeat = await SendAsync("POST", path, "key-failing", null);

        Assert.Equal(HttpStatusCode.InternalServerError, first.StatusCode);
        Assert.Equal(HttpStatusCode.InternalServerError, repeat.StatusCode);
        Assert.Equal(2, Counter.Count);
    }

    [Fact]
    public async Task AWriteWithoutAKey_RunsEveryTime()
    {
        using var first = await SendAsync("POST", "/api/v1/idempotency-probes", key: null, "a");
        using var second = await SendAsync("POST", "/api/v1/idempotency-probes", key: null, "a");

        Assert.Equal(2, Counter.Count);
    }

    // A browser's write runs every time, whatever headers it carries: only another service's write is kept by its key.
    [Fact]
    public async Task APersonsWrite_IgnoresTheKey()
    {
        using var first = await SendAsync("POST", "/api/v1/idempotency-probes", "key-1", "a", PersonToken);
        using var second = await SendAsync("POST", "/api/v1/idempotency-probes", "key-1", "b", PersonToken);

        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.False(second.Headers.Contains(IdempotencyKeys.ReplayedHeader));
        Assert.Equal(2, Counter.Count);
    }

    // No [AcceptsIdempotencyKey] on the handler, which the others carry: seen alike by the generated and the runtime
    // request delegates.
    [Fact]
    public async Task AnOperationThatDoesNotAcceptKeys_RunsEveryTime()
    {
        using var first = await SendAsync("POST", "/api/v1/idempotency-probes/ignored", "key-1", "a");
        using var second = await SendAsync("POST", "/api/v1/idempotency-probes/ignored", "key-1", "a");

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.False(second.Headers.Contains(IdempotencyKeys.ReplayedHeader));
        Assert.Equal(2, Counter.Count);
    }

    [Theory]
    [InlineData("has a space")]
    [InlineData("tab\tinside")]
    public async Task AMalformedKey_IsRefused400(string key)
    {
        using var refused = await SendAsync("POST", "/api/v1/idempotency-probes", key, "a");

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal(0, Counter.Count);
    }

    [Fact]
    public async Task AKeyTooLong_IsRefused400()
    {
        using var refused = await SendAsync("POST", "/api/v1/idempotency-probes", new string('k', IdempotencyKeys.MaximumLength + 1), "a");

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
    }

    // The body the operation binds is the body sent, after the fingerprint read it.
    [Fact]
    public async Task TheOperation_StillReadsTheBody()
    {
        using var first = await SendAsync("POST", "/api/v1/idempotency-probes", "key-1", "the note");

        Assert.Equal("the note", (await first.Content.ReadFromJsonAsync<IdempotencyProbeAnswer>(Token))!.Note);
    }

    // End to end: the service's own client sends the key, its first answer is lost on the way back, and the client's
    // retry is answered from the record. The write ran once.
    [Fact]
    public async Task AClientsRetryAfterALostAnswer_IsReplayed()
    {
        var lossy = new LosesFirstAnswer(_service.Handler());
        await using var caller = Caller(lossy);

        using var response = await caller.GetRequiredService<ProbeWriter>().WriteAsync("from a service", Token);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("true", Assert.Single(response.Headers.GetValues(IdempotencyKeys.ReplayedHeader)));
        Assert.Equal(2, lossy.Attempts);
        Assert.Equal(1, Counter.Count);
    }

    // Within an operation run twice, the second run's writes are answered from the first's.
    [Fact]
    public async Task AnOperationRunAgain_IsAnsweredFromTheFirstRun()
    {
        await using var caller = Caller(_service.Handler());
        var writer = caller.GetRequiredService<ProbeWriter>();

        for (var run = 0; run < 2; run++)
        {
            using (IdempotentOperation.Begin("operation-1"))
            {
                using var one = await writer.WriteAsync("one", Token);
                using var two = await writer.WriteAsync("two", Token);
            }
        }

        Assert.Equal(2, Counter.Count);
    }

    // A message handler that wrote and then failed is run again on the redelivery, under the operation the platform
    // names for it and the envelope: the write it made is answered from the first run, in whatever order the second run
    // makes its writes, and only the write it hadn't made runs.
    [Fact]
    public async Task AMessageRedeliveredAfterAFailurePartWay_ReplaysWhatTheFirstDeliveryWrote()
    {
        await using var caller = Caller(_service.Handler());
        var writer = caller.GetRequiredService<ProbeWriter>();
        var operation = IdempotentOperation.ForMessage("Some.Service.SomeHandler", Guid.NewGuid());

        using (IdempotentOperation.Begin(operation))
        {
            using var one = await writer.WriteAsync("one", Token);
            Assert.False(one.Headers.Contains(IdempotencyKeys.ReplayedHeader));
            // The handler fails here, before its second write, and the message is delivered again.
        }

        using (IdempotentOperation.Begin(operation))
        {
            using var two = await writer.WriteAsync("two", Token);
            using var one = await writer.WriteAsync("one", Token);

            Assert.False(two.Headers.Contains(IdempotencyKeys.ReplayedHeader));
            Assert.Equal("true", Assert.Single(one.Headers.GetValues(IdempotencyKeys.ReplayedHeader)));
        }

        Assert.Equal(2, Counter.Count);
    }

    // A redelivery whose write differs from the first delivery's (it read something the first changed) is a new write,
    // never refused 422 for reusing the first's key.
    [Fact]
    public async Task AMessageRedeliveredWithAnotherBody_WritesAgainRatherThanBeingRefused()
    {
        await using var caller = Caller(_service.Handler());
        var writer = caller.GetRequiredService<ProbeWriter>();
        var operation = IdempotentOperation.ForMessage("Some.Service.SomeHandler", Guid.NewGuid());

        foreach (var note in new[] { "before", "after" })
        {
            using (IdempotentOperation.Begin(operation))
            {
                using var response = await writer.WriteAsync(note, Token);
                Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            }
        }

        Assert.Equal(2, Counter.Count);
    }

    // A write asked only to be checked changes nothing: it is answered afresh each time, never from a record.
    [Theory]
    [InlineData("?validateOnly=true", 2)]
    [InlineData("?validateOnly=True", 2)]
    [InlineData("?validateOnly=false", 1)]
    public async Task AWriteAskedOnlyToBeChecked_IsNeverRecorded(string query, int runs)
    {
        using var first = await SendAsync("POST", "/api/v1/idempotency-probes" + query, "key-check", "a");
        using var repeat = await SendAsync("POST", "/api/v1/idempotency-probes" + query, "key-check", "a");

        Assert.Equal(runs, Counter.Count);
    }

    [Fact]
    public void StorageKey_IsTheServicesOwnAndPerCaller()
    {
        var key = IdempotentWrites.StorageKey("organization-service", "internal.vault-service", "k");

        Assert.StartsWith("organization-service:idempotency:", key, StringComparison.Ordinal);
        Assert.DoesNotContain("internal.vault-service", key, StringComparison.Ordinal);
        Assert.NotEqual(key, IdempotentWrites.StorageKey("organization-service", "internal.billing-service", "k"));
    }

    private async Task<HttpResponseMessage> SendAsync(string method, string path, string? key, string? note, string? token = null)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path)
        {
            Content = JsonContent.Create(new IdempotencyProbeNote(note)),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token ?? ServiceToken);
        if (key is not null)
        {
            request.Headers.TryAddWithoutValidation(IdempotencyKeys.Header, key);
        }

        return await _service.Client().SendAsync(request, Token);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var tries = 0; !condition(); tries++)
        {
            Assert.True(tries < 500, "Timed out waiting.");
            await Task.Delay(10, Token);
        }
    }

    /// <summary>
    /// Another service, calling this one through the platform's client.
    /// </summary>
    private static ServiceProvider Caller(HttpMessageHandler toService)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"Services:{SessionHost.ServiceName}:BaseAddress"] = "http://localhost",
                [$"Services:{SessionHost.ServiceName}:IdentityUri"] = "http://identity.invalid",
                [$"Services:{SessionHost.ServiceName}:CallerName"] = "some-service",
                [$"Services:{SessionHost.ServiceName}:CallerSecret"] = "made-up-for-tests",
            })
            .Build());
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddBitwardenClient<ProbeWriter, ProbeWriter>(SessionHost.ServiceName, [SessionHost.WriteScope], options =>
                options.RetryDelay = TimeSpan.Zero)
            .ConfigurePrimaryHttpMessageHandler(() => toService);
        services.AddHttpClient(ServiceTokenProvider.TokenClientName).ConfigurePrimaryHttpMessageHandler(() => new FakeIdentity());

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// A client with one write, as a generated one would make it.
    /// </summary>
    public sealed class ProbeWriter(HttpClient http)
    {
        public Task<HttpResponseMessage> WriteAsync(string note, CancellationToken cancellationToken) =>
            http.PostAsJsonAsync("api/v1/idempotency-probes", new IdempotencyProbeNote(note), cancellationToken);
    }

    /// <summary>
    /// Passes the first write on, and loses its answer, as a connection reset after the service answered.
    /// </summary>
    private sealed class LosesFirstAnswer(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        public int Attempts { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = await base.SendAsync(request, cancellationToken);

            if (++Attempts == 1)
            {
                response.Dispose();
                throw new HttpRequestException(HttpRequestError.ResponseEnded, "The response ended prematurely.");
            }

            return response;
        }
    }

    /// <summary>
    /// Identity: the service's token, and for the exchange a context token signed as Identity signs one.
    /// </summary>
    private sealed class FakeIdentity : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var form = (await request.Content!.ReadAsStringAsync(cancellationToken)).Split('&')
                .Select(pair => pair.Split('=', 2))
                .ToDictionary(pair => Uri.UnescapeDataString(pair[0]), pair => Uri.UnescapeDataString(pair[1].Replace('+', ' ')));
            var token = form.GetValueOrDefault("requested_token_type") == ContextToken.TokenType
                ? SessionHost.SignedContext(context: form[ContextToken.ContextClaim], mode: ContextToken.Asserted)
                : ServiceToken;

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    $$"""{"access_token":"{{token}}","expires_in":300,"token_type":"Bearer"}""",
                    Encoding.UTF8,
                    "application/json"),
            };
        }
    }
}
