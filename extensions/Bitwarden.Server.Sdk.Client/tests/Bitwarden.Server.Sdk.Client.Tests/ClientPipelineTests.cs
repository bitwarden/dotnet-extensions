using System.Diagnostics;
using System.Net;
using System.Text;
using Bitwarden.Server.Sdk.Errors;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bitwarden.Server.Sdk.Client.Tests;

/// <summary>
/// The pipeline <c>AddBitwardenClient</c> builds, whole: the client's deadline covers obtaining the call's tokens from
/// Identity as well as the call itself, and Identity failing is reported as Identity, not as the callee.
/// </summary>
public sealed class ClientPipelineTests
{
    private const string Callee = "callee-service";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Call_IdentityNeverAnswers_IsUnavailableWithinTheClientsDeadline()
    {
        var timeout = TimeSpan.FromMilliseconds(500);
        await using var services = Services(new Hanging(), new Answering(HttpStatusCode.OK), timeout);
        var probe = services.GetRequiredService<Probe>();
        var watch = Stopwatch.StartNew();

        var unavailable = await Assert.ThrowsAsync<ServiceUnavailableException>(() => probe.GetAsync(Token));

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), $"Took {watch.Elapsed}, past the client's deadline.");
        Assert.Equal(ServiceTokenProvider.Issuer, unavailable.Service);
        Assert.Contains(Callee, unavailable.Detail, StringComparison.Ordinal);
        Assert.Contains("0.5 seconds", unavailable.Detail, StringComparison.Ordinal);
    }

    // The call's own token is held; the RFC 8693 exchange for its context is what hangs.
    [Fact]
    public async Task Call_IdentityNeverAnswersTheContextExchange_IsUnavailableNamingIdentity()
    {
        var timeout = TimeSpan.FromMilliseconds(500);
        var callee = new Answering(HttpStatusCode.OK);
        await using var services = Services(new IssuingThenHanging(), callee, timeout);
        var probe = services.GetRequiredService<Probe>();

        var unavailable = await Assert.ThrowsAsync<ServiceUnavailableException>(() => probe.GetAsync(Token));

        Assert.Equal(ServiceTokenProvider.Issuer, unavailable.Service);
        Assert.Contains("a context token", unavailable.Detail, StringComparison.Ordinal);
        Assert.Null(callee.Authorization);
    }

    // Identity slower than the token client's own bound, inside a longer deadline: still Identity, not the callee.
    [Fact]
    public async Task Call_IdentityPastTheTokenClientsBound_IsUnavailableNamingIdentity()
    {
        await using var services = Services(
            new Hanging(), new Answering(HttpStatusCode.OK), TimeSpan.FromSeconds(30), tokenBound: TimeSpan.FromMilliseconds(200));
        var probe = services.GetRequiredService<Probe>();

        var unavailable = await Assert.ThrowsAsync<ServiceUnavailableException>(() => probe.GetAsync(Token));

        Assert.Equal(ServiceTokenProvider.Issuer, unavailable.Service);
        Assert.Contains("within 0.2 seconds", unavailable.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Call_IdentityRefusesConnections_IsUnavailableNamingIdentity()
    {
        await using var services = Services(new Refusing(), new Answering(HttpStatusCode.OK));
        var probe = services.GetRequiredService<Probe>();

        var unavailable = await Assert.ThrowsAsync<ServiceUnavailableException>(() => probe.GetAsync(Token));

        Assert.Equal(ServiceTokenProvider.Issuer, unavailable.Service);
        Assert.Contains(Callee, unavailable.Detail, StringComparison.Ordinal);
        Assert.IsType<HttpRequestException>(unavailable.InnerException);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public async Task Call_IdentityAnswersUnavailable_IsUnavailableNamingIdentity(HttpStatusCode status)
    {
        await using var services = Services(new Answering(status), new Answering(HttpStatusCode.OK));
        var probe = services.GetRequiredService<Probe>();

        var unavailable = await Assert.ThrowsAsync<ServiceUnavailableException>(() => probe.GetAsync(Token));

        Assert.Equal(ServiceTokenProvider.Issuer, unavailable.Service);
        Assert.Contains(((int)status).ToString(System.Globalization.CultureInfo.InvariantCulture), unavailable.Detail, StringComparison.Ordinal);
    }

    // Identity refusing the credential is this service's misconfiguration, never the callee's answer: no status a
    // caller reads as the callee refusing (a 404 caught as "not found", a 422 relayed as a refusal).
    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task Call_IdentityRefusesTheCredential_FailsAsThisServicesOwn(HttpStatusCode status)
    {
        await using var services = Services(new Answering(status), new Answering(HttpStatusCode.OK));
        var probe = services.GetRequiredService<Probe>();

        var failed = await Assert.ThrowsAsync<ServiceCallException>(() => probe.GetAsync(Token));

        Assert.Equal(HttpStatusCode.InternalServerError, failed.Status);
        Assert.Equal(status, Assert.IsType<HttpRequestException>(failed.InnerException).StatusCode);
        Assert.Contains($"{(int)status}", failed.Message, StringComparison.Ordinal);
        Assert.Contains("could not obtain a token", failed.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Call_TheCalleeNeverAnswers_IsUnavailableNamingTheCallee()
    {
        var timeout = TimeSpan.FromMilliseconds(500);
        await using var services = Services(new Issuing(), new Hanging(), timeout);
        var probe = services.GetRequiredService<Probe>();

        var unavailable = await Assert.ThrowsAsync<ServiceUnavailableException>(() => probe.GetAsync(Token));

        Assert.Equal(Callee, unavailable.Service);
    }

    [Fact]
    public async Task Call_TheCalleeAnswersUnavailable_IsUnavailableNamingTheCallee()
    {
        await using var services = Services(new Issuing(), new Answering(HttpStatusCode.ServiceUnavailable));
        var probe = services.GetRequiredService<Probe>();

        var unavailable = await Assert.ThrowsAsync<ServiceUnavailableException>(() => probe.GetAsync(Token));

        Assert.Equal(Callee, unavailable.Service);
    }

    [Fact]
    public async Task Call_TheCallerCancels_IsCancelledNotUnavailable()
    {
        await using var services = Services(new Hanging(), new Answering(HttpStatusCode.OK), TimeSpan.FromSeconds(30));
        var probe = services.GetRequiredService<Probe>();
        using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(Token);
        cancelled.CancelAfter(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => probe.GetAsync(cancelled.Token));
    }

    [Fact]
    public async Task Call_EverythingAnswers_PresentsTheToken()
    {
        var callee = new Answering(HttpStatusCode.OK);
        await using var services = Services(new Issuing(), callee);

        using var response = await services.GetRequiredService<Probe>().GetAsync(Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Bearer token-1", callee.Authorization);
    }

    // The token client's own bound, for a renewal no call is waiting on.
    [Fact]
    public async Task TokenClient_HasABoundedTimeout()
    {
        await using var services = Services(new Issuing(), new Answering(HttpStatusCode.OK));

        var client = services.GetRequiredService<IHttpClientFactory>().CreateClient(ServiceTokenProvider.TokenClientName);

        Assert.Equal(ServiceTokenProvider.RequestTimeout, client.Timeout);
    }

    private static ServiceProvider Services(
        HttpMessageHandler identity,
        HttpMessageHandler callee,
        TimeSpan? timeout = null,
        TimeSpan? tokenBound = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"Services:{Callee}:BaseAddress"] = "http://callee.invalid",
                [$"Services:{Callee}:IdentityUri"] = "http://identity.invalid",
                [$"Services:{Callee}:CallerName"] = "caller",
                [$"Services:{Callee}:CallerSecret"] = "made-up-for-tests",
            })
            .Build());
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        services.AddBitwardenClient<Probe, Probe>(Callee, ["callee.read"], options =>
            {
                if (timeout is { } given)
                {
                    options.Timeout = given;
                }
            })
            .ConfigurePrimaryHttpMessageHandler(() => callee);
        services.AddHttpClient(ServiceTokenProvider.TokenClientName)
            .ConfigurePrimaryHttpMessageHandler(() => identity)
            .ConfigureHttpClient(client =>
            {
                if (tokenBound is { } bound)
                {
                    client.Timeout = bound;
                }
            });

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// A client with one call, as a generated one would make it.
    /// </summary>
    public sealed class Probe(HttpClient http)
    {
        public Task<HttpResponseMessage> GetAsync(CancellationToken cancellationToken) =>
            http.GetAsync("things", cancellationToken);
    }

    /// <summary>
    /// Accepts the request and never answers.
    /// </summary>
    private sealed class Hanging : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new UnreachableException();
        }
    }

    /// <summary>
    /// Refuses the connection, as a host with nothing listening does.
    /// </summary>
    private sealed class Refusing : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException(HttpRequestError.ConnectionError, "Connection refused (identity.invalid:80)");
    }

    /// <summary>
    /// Answers every request with one status.
    /// </summary>
    private sealed class Answering(HttpStatusCode status) : HttpMessageHandler
    {
        public string? Authorization { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Authorization = request.Headers.Authorization?.ToString();
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent("{}", Encoding.UTF8, "application/json") });
        }
    }

    /// <summary>
    /// Identity issuing the service's token and never answering the exchange.
    /// </summary>
    private sealed class IssuingThenHanging : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var form = await request.Content!.ReadAsStringAsync(cancellationToken);
            if (form.Contains("client_credentials", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """{"access_token":"token-1","expires_in":3600,"token_type":"Bearer"}""",
                        Encoding.UTF8,
                        "application/json"),
                };
            }

            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new UnreachableException();
        }
    }

    /// <summary>
    /// Identity's token endpoint, issuing.
    /// </summary>
    private sealed class Issuing : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"access_token":"token-1","expires_in":3600,"token_type":"Bearer"}""",
                    Encoding.UTF8,
                    "application/json"),
            });
    }
}
