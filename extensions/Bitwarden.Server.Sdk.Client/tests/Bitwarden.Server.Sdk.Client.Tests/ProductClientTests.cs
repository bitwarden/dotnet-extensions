using System.Collections.Concurrent;
using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Bitwarden.Server.Sdk.Client.Tests;

/// <summary>
/// A product service's client: each call made with the token it is given, never the caller's own credential, and its
/// answer kept as it came, a refusal included.
/// </summary>
public sealed class ProductClientTests
{
    private const string Name = "a-product";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ACall_CarriesTheTokenItIsGiven_AndKeepsTheAnswerAndTheDocument()
    {
        var service = new FakeService((_, _) => (200, """{"name":"one"}"""));
        var client = Client(service);

        var answer = await client.ReadAsync("person-token", Token);

        Assert.Equal(200, answer.Status);
        Assert.Equal("""{"name":"one"}""", answer.Body);
        Assert.Equal("one", answer.Document?.Name);
        Assert.Equal("Bearer person-token", Assert.Single(service.Sent).Authorization);
    }

    [Fact]
    public async Task ARefusal_IsAnAnswer_WithNoDocument()
    {
        var client = Client(new FakeService((_, _) => (422, """{"errors":[{"code":"name-taken"}]}""")));

        var answer = await client.ReadAsync("person-token", Token);

        Assert.False(answer.Succeeded);
        Assert.Equal(422, answer.Status);
        Assert.Equal("""{"errors":[{"code":"name-taken"}]}""", answer.Body);
        Assert.Null(answer.Document);
    }

    [Fact]
    public async Task AnUnreadableBody_IsStillTheAnswer()
    {
        var client = Client(new FakeService((_, _) => (200, "not json")));

        var answer = await client.ReadAsync("person-token", Token);

        Assert.Equal(200, answer.Status);
        Assert.Equal("not json", answer.Body);
        Assert.Null(answer.Document);
    }

    [Fact]
    public async Task ACallWithoutADocument_KeepsTheAnswer()
    {
        var client = Client(new FakeService((_, _) => (204, string.Empty)));

        var answer = await client.DeleteAsync("person-token", Token);

        Assert.Equal(new ProductAnswer(204, string.Empty), answer);
    }

    [Fact]
    public async Task ARelayedBody_IsSentAsItIs()
    {
        var service = new FakeService((_, _) => (201, "{}"));
        var client = Client(service);

        var answer = await client.RelayAsync("person-token", """{"name":42}""", Token);

        Assert.Equal(201, answer.Status);
        var sent = Assert.Single(service.Sent);
        Assert.Equal("""{"name":42}""", sent.Body);
        Assert.Equal("Bearer person-token", sent.Authorization);
        Assert.Equal("POST", sent.Method);
        Assert.Equal("/things", sent.Path);
    }

    [Fact]
    public async Task CallsMadeAtOnce_EachCarryTheirOwnToken()
    {
        var service = new FakeService((_, _) => (200, """{"name":"one"}"""));
        var client = Client(service);

        await Task.WhenAll(Enumerable.Range(0, 50).Select(each => client.ReadAsync($"token-{each}", Token)));

        Assert.Equal(
            Enumerable.Range(0, 50).Select(each => $"Bearer token-{each}").Order(),
            service.Sent.Select(sent => sent.Authorization).Order());
    }

    // The same named client may carry other requests, made without a product call: they go as they are.
    [Fact]
    public async Task ARequestOutsideACall_PassesThroughUntouched()
    {
        var service = new FakeService((_, _) => (200, "{}"));
        using var provider = Provider(service);
        var http = provider.GetRequiredService<IHttpClientFactory>().CreateClient(Name);

        using var response = await http.GetAsync("things/1", Token);

        Assert.Null(Assert.Single(service.Sent).Authorization);
    }

    // A token is never carried on to a second request nobody meant it for.
    [Fact]
    public async Task ACallSendingASecondRequest_Throws()
    {
        var client = Client(new FakeService((_, _) => (200, """{"name":"one"}""")));

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.TwiceAsync("person-token", Token));
    }

    [Fact]
    public async Task AServiceThatCantBeReached_IsTheClientsException()
    {
        var client = Client(new FakeService((_, _) => throw new HttpRequestException("refused")));

        await Assert.ThrowsAsync<HttpRequestException>(() => client.ReadAsync("person-token", Token));
    }

    [Fact]
    public void AnAnswer_NeverNamesItsBody()
    {
        Assert.Equal("ProductAnswer`1 { Status = 200 }", new ProductAnswer<Thing>(200, "secret", null).ToString());
        Assert.Equal("ProductAnswer { Status = 200 }", new ProductAnswer(200, "secret").ToString());
    }

    [Fact]
    public async Task WithoutANamedClient_TheConventionalAddressIsUsed()
    {
        var service = new FakeService((_, _) => (200, """{"name":"one"}"""));
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection([new($"Services:{Name}:BaseAddress", "http://conventional.invalid")])
            .Build());
        services.AddProductClient<TestClient, TestClient>(Name).ConfigurePrimaryHttpMessageHandler(() => service);
        using var provider = services.BuildServiceProvider();

        await provider.GetRequiredService<TestClient>().ReadAsync("person-token", Token);

        Assert.Equal("conventional.invalid", Assert.Single(service.Sent).Host);
    }

    // Where the deployment requires TLS, a plain address found by convention stops the host at startup, as it does
    // for every other client: the token a product client carries is a person's.
    [Theory]
    [InlineData("http://conventional.invalid", true, false)]
    [InlineData("https://conventional.invalid", true, true)]
    [InlineData("http://conventional.invalid", false, true)]
    [InlineData(null, true, true)]
    public void WithoutANamedClient_APlainAddressWhereTlsIsRequired_StopsTheHost(string? address, bool required, bool starts)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection([
                new($"Services:{Name}:BaseAddress", address),
                new("Tls:RequireHttps", required ? "true" : null),
            ])
            .Build());
        services.AddProductClient<TestClient, TestClient>(Name);
        using var provider = services.BuildServiceProvider();

        var validate = () => provider.GetRequiredService<IStartupValidator>().Validate();

        if (starts)
        {
            validate();
        }
        else
        {
            var refusal = Assert.Throws<OptionsValidationException>(validate);
            Assert.Contains("Tls:RequireHttps", refusal.Message, StringComparison.Ordinal);
        }
    }

    // A named client is the consumer's: its address and its checks are its own.
    [Fact]
    public void OverANamedClient_NoAddressIsChecked()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection([
                new($"Services:{Name}:BaseAddress", "http://conventional.invalid"),
                new("Tls:RequireHttps", "true"),
            ])
            .Build());
        services.AddProductClient<TestClient, TestClient>(Name, "my-client");
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IStartupValidator>().Validate();
    }

    // A product service's client package may register more than one client for it, each adding its own handler to the
    // same named client: a call through either is still one request, carrying the call's token.
    [Theory]
    [InlineData(null)]
    [InlineData("my-client")]
    public async Task TwoClientsForOneService_EachCallIsOneRequest(string? name)
    {
        var service = new FakeService((_, _) => (200, """{"name":"one"}"""));
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection([new($"Services:{Name}:BaseAddress", "http://conventional.invalid")])
            .Build());
        if (name is not null)
        {
            services.AddHttpClient(name, http => http.BaseAddress = new Uri("http://product.invalid/"));
        }

        services.AddProductClient<TestClient, TestClient>(Name, name).ConfigurePrimaryHttpMessageHandler(() => service);
        services.AddProductClient<OtherClient, OtherClient>(Name, name).ConfigurePrimaryHttpMessageHandler(() => service);
        using var provider = services.BuildServiceProvider();

        var first = await provider.GetRequiredService<TestClient>().ReadAsync("person-token", Token);
        var second = await provider.GetRequiredService<OtherClient>().DeleteAsync("other-token", Token);

        Assert.Equal(200, first.Status);
        Assert.Equal(200, second.Status);
        Assert.Equal(["Bearer person-token", "Bearer other-token"], service.Sent.Select(sent => sent.Authorization));
    }

    // A handler above that sends the same request again (a retry) is still a second request inside the call, with two
    // product clients' handlers stacked as with one.
    [Fact]
    public async Task TwoClientsForOneService_ARequestSentAgainFromAbove_Throws()
    {
        var service = new FakeService((_, _) => (200, """{"name":"one"}"""));
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddHttpClient("my-client", http => http.BaseAddress = new Uri("http://product.invalid/"))
            .AddHttpMessageHandler(() => new SendingTwice())
            .ConfigurePrimaryHttpMessageHandler(() => service);
        services.AddProductClient<TestClient, TestClient>(Name, "my-client");
        services.AddProductClient<OtherClient, OtherClient>(Name, "my-client");
        using var provider = services.BuildServiceProvider();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.GetRequiredService<TestClient>().ReadAsync("person-token", Token));
    }

    private static TestClient Client(FakeService service) => Provider(service).GetRequiredService<TestClient>();

    private static ServiceProvider Provider(FakeService service)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddHttpClient(Name, http => http.BaseAddress = new Uri("http://product.invalid/"))
            .ConfigurePrimaryHttpMessageHandler(() => service);
        services.AddProductClient<TestClient, TestClient>("product-service", Name);
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// A document, as a generated client reads one.
    /// </summary>
    public sealed class Thing
    {
        public string? Name { get; set; }
    }

    /// <summary>
    /// A product client whose "generated" calls throw on a refusal, as NSwag's do.
    /// </summary>
    public sealed class TestClient(HttpClient http) : ProductClient(http)
    {
        public Task<ProductAnswer<Thing>> ReadAsync(string bearer, CancellationToken cancellationToken) =>
            CallAsync(bearer, () => GeneratedReadAsync(cancellationToken));

        public Task<ProductAnswer> DeleteAsync(string bearer, CancellationToken cancellationToken) =>
            CallAsync(bearer, async () =>
            {
                using var response = await Http.DeleteAsync("things/1", cancellationToken);
                response.EnsureSuccessStatusCode();
            });

        public Task<ProductAnswer> RelayAsync(string bearer, string json, CancellationToken cancellationToken) =>
            SendAsync(bearer, HttpMethod.Post, "/things", json, cancellationToken);

        public Task<ProductAnswer<Thing>> TwiceAsync(string bearer, CancellationToken cancellationToken) =>
            CallAsync(bearer, async () =>
            {
                await GeneratedReadAsync(cancellationToken);
                return await GeneratedReadAsync(cancellationToken);
            });

        private async Task<Thing> GeneratedReadAsync(CancellationToken cancellationToken)
        {
            using var response = await Http.GetAsync("things/1", HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"The request did not succeed: {(int)response.StatusCode}.");
            }

            return Newtonsoft.Json.JsonConvert.DeserializeObject<Thing>(text)
                ?? throw new InvalidOperationException("Response was null.");
        }
    }

    /// <summary>
    /// A second client for the same product service.
    /// </summary>
    public sealed class OtherClient(HttpClient http) : ProductClient(http)
    {
        public Task<ProductAnswer> DeleteAsync(string bearer, CancellationToken cancellationToken) =>
            CallAsync(bearer, async () =>
            {
                using var response = await Http.DeleteAsync("things/1", cancellationToken);
                response.EnsureSuccessStatusCode();
            });
    }

    /// <summary>
    /// Sends each request twice, as a retry would, and returns the second answer.
    /// </summary>
    private sealed class SendingTwice : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            (await base.SendAsync(request, cancellationToken)).Dispose();
            return await base.SendAsync(request, cancellationToken);
        }
    }

    public sealed record SentRequest(string Method, string Path, string Host, string? Authorization, string Body);

    private sealed class FakeService(Func<HttpRequestMessage, string, (int Status, string Body)> answer) : HttpMessageHandler
    {
        private readonly ConcurrentQueue<SentRequest> _sent = new();

        public IReadOnlyList<SentRequest> Sent => [.. _sent];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            _sent.Enqueue(new SentRequest(
                request.Method.Method, request.RequestUri!.AbsolutePath, request.RequestUri.Host,
                request.Headers.Authorization?.ToString(), body));
            var (status, text) = answer(request, body);
            return new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(text, Encoding.UTF8, "application/json") };
        }
    }
}
