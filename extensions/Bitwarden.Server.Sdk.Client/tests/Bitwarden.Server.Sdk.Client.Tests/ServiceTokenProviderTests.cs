using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bitwarden.Server.Sdk.Client.Tests;

/// <summary>
/// A service's token for its calls: fetched once, replaced before it expires, and never held past its renewal time by
/// the wall clock, whatever its timer does.
/// </summary>
public sealed class ServiceTokenProviderTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task GetAsync_TheFirstCall_FetchesATokenAndLaterCallsReuseIt()
    {
        var identity = new FakeIdentity(lifetimeSeconds: 3600);
        using var provider = Provider(identity, new SettableClock());

        var first = await provider.GetAsync(Token);
        var second = await provider.GetAsync(Token);

        Assert.Equal("token-1", first);
        Assert.Equal("token-1", second);
        Assert.Equal(1, identity.Issued);
    }

    // The renewal timer runs on the process's monotonic clock; when it fires late or not at all (a paused container, a
    // stalled timer), the next call fetches a new token itself rather than sending one the callee refuses.
    [Fact]
    public async Task GetAsync_PastTheRenewalTimeByTheWallClock_FetchesANewTokenWithoutTheTimer()
    {
        var clock = new SettableClock();
        var identity = new FakeIdentity(lifetimeSeconds: 3600);
        using var provider = Provider(identity, clock);
        await provider.GetAsync(Token);

        clock.Advance(TimeSpan.FromSeconds(3600 * 0.8 + 1));
        var renewed = await provider.GetAsync(Token);

        Assert.Equal("token-2", renewed);
        Assert.Equal(2, identity.Issued);
    }

    [Fact]
    public async Task GetAsync_BeforeTheRenewalTime_KeepsTheToken()
    {
        var clock = new SettableClock();
        var identity = new FakeIdentity(lifetimeSeconds: 3600);
        using var provider = Provider(identity, clock);
        await provider.GetAsync(Token);

        clock.Advance(TimeSpan.FromSeconds(3600 * 0.8 - 1));

        Assert.Equal("token-1", await provider.GetAsync(Token));
        Assert.Equal(1, identity.Issued);
    }

    // Many calls at the renewal time share one fetch.
    [Fact]
    public async Task GetAsync_ManyCallsPastTheRenewalTime_FetchOnce()
    {
        var clock = new SettableClock();
        var identity = new FakeIdentity(lifetimeSeconds: 3600);
        using var provider = Provider(identity, clock);
        await provider.GetAsync(Token);
        clock.Advance(TimeSpan.FromHours(2));

        var tokens = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => provider.GetAsync(Token)));

        Assert.All(tokens, token => Assert.Equal("token-2", token));
        Assert.Equal(2, identity.Issued);
    }

    // A token per callee: the request names the one service the token is for, so one leaked at one service is
    // refused at every other.
    [Fact]
    public async Task GetAsync_AsksForATokenForTheCallee()
    {
        var identity = new FakeIdentity(lifetimeSeconds: 3600);
        using var provider = Provider(identity, new SettableClock());

        await provider.GetAsync(Token);

        Assert.NotNull(identity.Form);
        Assert.Equal("client_credentials", identity.Form["grant_type"]);
        Assert.Equal("internal.test", identity.Form["client_id"]);
        Assert.Equal("callee-service", identity.Form["audience"]);
        Assert.Equal("callee.read", identity.Form["scope"]);
    }

    // The callee is the name its client package registers it under, which is the service's own name, unless
    // Services:<name>:Audience says otherwise.
    [Theory]
    [InlineData(null, "callee-service")]
    [InlineData("renamed-service", "renamed-service")]
    public void Conventions_Audience_IsTheRegisteredNameUnlessConfigured(string? configured, string expected)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Services:callee-service:Audience"] = configured })
            .Build();
        var options = new BitwardenClientOptions();

        Conventions.Apply(options, configuration, "callee-service");

        Assert.Equal(expected, options.Audience);
    }

    // --- Context tokens --------------------------------------------------------------------

    private static readonly Bitwarden.Server.Sdk.Context.ContextAssertion Assertion =
        new(new Guid("a0000000-0000-4000-8000-0000000000a1"), new Guid("b0000000-0000-4000-8000-0000000000b1"), null, """{"v":1,"credential":"USER"}""");

    // RFC 8693 at Identity, as this service, for its callee, with the person's token as the subject.
    [Fact]
    public async Task GetContextTokenAsync_ExchangesTheSubjectForTheCallee()
    {
        var identity = new FakeIdentity(lifetimeSeconds: 300);
        using var provider = Provider(identity, new SettableClock());

        var token = await provider.GetContextTokenAsync(Assertion, "a.person.token", Token);

        Assert.Equal("token-1", token);
        var form = identity.Form!;
        Assert.Equal("urn:ietf:params:oauth:grant-type:token-exchange", form["grant_type"]);
        Assert.Equal("internal.test", form["client_id"]);
        Assert.Equal("callee-service", form["audience"]);
        Assert.Equal("urn:bitwarden:params:oauth:token-type:caller-context", form["requested_token_type"]);
        Assert.Equal("a.person.token", form["subject_token"]);
        Assert.Equal("urn:ietf:params:oauth:token-type:access_token", form["subject_token_type"]);
        Assert.Equal(Assertion.Organization.ToString(), form["context_organization"]);
        Assert.Equal(Assertion.User.ToString(), form["context_user"]);
        Assert.False(form.ContainsKey("context_provider"));
        Assert.Equal(Assertion.Context, form["context"]);
    }

    // The service's own word: its own token for the callee is the subject.
    [Fact]
    public async Task GetContextTokenAsync_WithNoSubject_PresentsItsOwnToken()
    {
        var identity = new FakeIdentity(lifetimeSeconds: 300);
        using var provider = Provider(identity, new SettableClock());

        await provider.GetContextTokenAsync(Assertion, subjectToken: null, Token);

        Assert.Equal("token-1", identity.Form!["subject_token"]);
        Assert.Equal(2, identity.Issued);
    }

    // A context token the service was called with is passed on as one.
    [Fact]
    public async Task GetContextTokenAsync_WithAContextTokenAsTheSubject_SaysSo()
    {
        var identity = new FakeIdentity(lifetimeSeconds: 300);
        using var provider = Provider(identity, new SettableClock());
        var header = Convert.ToBase64String(Encoding.UTF8.GetBytes("""{"alg":"RS256","typ":"bitwarden-context+jwt"}""")).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        await provider.GetContextTokenAsync(Assertion, $"{header}.e30.c2ln", Token);

        Assert.Equal("urn:bitwarden:params:oauth:token-type:caller-context", identity.Form!["subject_token_type"]);
    }

    // Kept for the same subject and context until its renewal time; another context is another token.
    [Fact]
    public async Task GetContextTokenAsync_KeepsEachUntilItsRenewalTime()
    {
        var clock = new SettableClock();
        var identity = new FakeIdentity(lifetimeSeconds: 300);
        using var provider = Provider(identity, clock);

        var first = await provider.GetContextTokenAsync(Assertion, "a.person.token", Token);
        var again = await provider.GetContextTokenAsync(Assertion, "a.person.token", Token);
        var other = await provider.GetContextTokenAsync(Assertion with { User = Guid.NewGuid() }, "a.person.token", Token);
        clock.Advance(TimeSpan.FromSeconds(300 * 0.8 + 1));
        var renewed = await provider.GetContextTokenAsync(Assertion, "a.person.token", Token);

        Assert.Equal(first, again);
        Assert.NotEqual(first, other);
        Assert.NotEqual(first, renewed);
        Assert.Equal(3, identity.Issued);
    }

    // A token is never handed on to another subject, even for the same context: each person's proof is exchanged
    // for a token of its own, and the service's own word never reuses a person's.
    [Fact]
    public async Task GetContextTokenAsync_AnotherSubject_IsAnotherToken()
    {
        var identity = new FakeIdentity(lifetimeSeconds: 300);
        using var provider = Provider(identity, new SettableClock());

        var one = await provider.GetContextTokenAsync(Assertion, "one.person.token", Token);
        var other = await provider.GetContextTokenAsync(Assertion, "another.person.token", Token);
        var own = await provider.GetContextTokenAsync(Assertion, subjectToken: null, Token);

        Assert.NotEqual(one, other);
        Assert.NotEqual(one, own);
        Assert.NotEqual(other, own);
    }

    // Refused: the call fails rather than going without.
    [Fact]
    public async Task GetContextTokenAsync_Refused_Throws()
    {
        var identity = new FakeIdentity(lifetimeSeconds: 300) { Refuse = true };
        using var provider = Provider(identity, new SettableClock());

        await Assert.ThrowsAsync<ServiceCallException>(() => provider.GetContextTokenAsync(Assertion, "a.person.token", Token));
    }

    private static ServiceTokenProvider Provider(FakeIdentity identity, TimeProvider clock) =>
        new(
            new SingleClientFactory(identity),
            new BitwardenClientOptions
            {
                BaseAddress = "http://callee.invalid",
                IdentityUri = "http://identity.invalid",
                CallerName = "test",
                CallerSecret = "made-up-for-tests",
                Audience = "callee-service",
                Scopes = ["callee.read"],
            },
            NullLogger<ServiceTokenProvider>.Instance,
            clock);

    /// <summary>
    /// The wall clock a test moves on; its timers are the system's, which these tests never wait for.
    /// </summary>
    private sealed class SettableClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 23, 19, 0, 0, TimeSpan.Zero);

        public void Advance(TimeSpan by) => _now += by;

        public override DateTimeOffset GetUtcNow() => _now;
    }

    /// <summary>
    /// Identity's token endpoint: a new token each time, numbered, with the lifetime given.
    /// </summary>
    private sealed class FakeIdentity(int lifetimeSeconds) : HttpMessageHandler
    {
        private int _issued;

        public int Issued => _issued;

        /// <summary>
        /// Whether every request is refused.
        /// </summary>
        public bool Refuse { get; init; }

        /// <summary>
        /// The last request's form, by field.
        /// </summary>
        public Dictionary<string, string>? Form { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            Form = body.Split('&').Select(pair => pair.Split('=', 2)).ToDictionary(
                pair => Uri.UnescapeDataString(pair[0]),
                pair => Uri.UnescapeDataString(pair[1].Replace('+', ' ')));
            if (Refuse)
            {
                return new HttpResponseMessage(HttpStatusCode.BadRequest);
            }

            var issued = Interlocked.Increment(ref _issued);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    $$"""{"access_token":"token-{{issued}}","expires_in":{{lifetimeSeconds}},"token_type":"Bearer"}""",
                    Encoding.UTF8,
                    "application/json"),
            };
        }
    }

    private sealed class SingleClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
