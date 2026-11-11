using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using Bitwarden.Server.Sdk.Context;
using Bitwarden.Server.Sdk.Idempotency;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bitwarden.Server.Sdk.Client.Tests;

/// <summary>
/// The pipeline <c>AddBitwardenClient</c> builds, over a scripted callee, with the stand-ins the resilience tests share.
/// </summary>
internal static class PipelineHarness
{
    internal const string Callee = "callee-service";

    internal static ServiceProvider Services(
        Scripted callee,
        TimeProvider? clock = null,
        Action<BitwardenClientOptions>? configure = null,
        HttpMessageHandler? identity = null)
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
        if (clock is not null)
        {
            services.AddSingleton(clock);
        }

        services.AddBitwardenClient<Probe, Probe>(Callee, ["callee.read"], options =>
            {
                options.RetryDelay = TimeSpan.Zero;
                configure?.Invoke(options);
            })
            .ConfigurePrimaryHttpMessageHandler(() => callee);
        services.AddHttpClient(ServiceTokenProvider.TokenClientName)
            .ConfigurePrimaryHttpMessageHandler(() => identity ?? new Switchable());

        return services.BuildServiceProvider();
    }

    internal static HttpResponseMessage Ok(HttpRequestMessage request) => new(HttpStatusCode.OK);

    internal static HttpResponseMessage Refused(HttpRequestMessage request) =>
        throw new HttpRequestException(HttpRequestError.ConnectionError, "Connection refused (callee.invalid:80)");

    internal static HttpResponseMessage Hang(HttpRequestMessage request) => throw new UnreachableException();

    internal static HttpResponseMessage StillRunning(HttpRequestMessage request)
    {
        var response = new HttpResponseMessage(HttpStatusCode.Conflict)
        {
            Content = new StringContent(
                """{"errors":[{"status":"409","code":"conflict","title":"t","detail":"still running"}]}""",
                Encoding.UTF8,
                "application/json"),
        };
        response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.Zero);
        return response;
    }

    /// <summary>
    /// The callee refusing a write as a conflict: its error document, with no <c>Retry-After</c>.
    /// </summary>
    internal static HttpResponseMessage Conflict(HttpRequestMessage request) =>
        new(HttpStatusCode.Conflict)
        {
            Content = new StringContent(
                """{"errors":[{"status":"409","code":"conflict","title":"t","detail":"stale version"}]}""",
                Encoding.UTF8,
                "application/json"),
        };

    /// <summary>
    /// A status from something in front of the callee: an HTML page, not the service's error document.
    /// </summary>
    internal static Func<HttpRequestMessage, HttpResponseMessage> BareStatus(HttpStatusCode status) => _ =>
        new HttpResponseMessage(status) { Content = new StringContent("<html>bad gateway</html>", Encoding.UTF8, "text/html") };

    /// <summary>
    /// The callee's own answer: its standard error document.
    /// </summary>
    internal static Func<HttpRequestMessage, HttpResponseMessage> ErrorDocument(HttpStatusCode status) => _ =>
        new HttpResponseMessage(status)
        {
            Content = new StringContent(
                """{"errors":[{"status":"500","code":"internal-error","title":"t","detail":"d"}]}""",
                Encoding.UTF8,
                "application/json"),
        };

    /// <summary>
    /// A client with one call of each kind, as a generated one would make them.
    /// </summary>
    public sealed class Probe(HttpClient http)
    {
        public async Task<HttpResponseMessage> SendAsync(
            HttpMethod method,
            CancellationToken cancellationToken,
            object? body = null,
            string? key = null,
            string address = "things")
        {
            using var request = new HttpRequestMessage(method, address);
            if (body is not null)
            {
                request.Content = JsonContent.Create(body);
            }

            if (key is not null)
            {
                request.Headers.Add(IdempotencyKeys.Header, key);
            }

            return await http.SendAsync(request, cancellationToken);
        }
    }

    /// <summary>
    /// One attempt as the callee saw it.
    /// </summary>
    public sealed record Attempt(string? Key, string? Body, string? Organization);

    /// <summary>
    /// The callee: answers each attempt with the next step of its script, the last one repeating. <see cref="Hang"/>
    /// waits until the attempt is cancelled.
    /// </summary>
    internal sealed class Scripted(params Func<HttpRequestMessage, HttpResponseMessage>[] script) : HttpMessageHandler
    {
        public ConcurrentQueue<Attempt> Attempts { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Attempts.Enqueue(new Attempt(
                request.Headers.TryGetValues(IdempotencyKeys.Header, out var keys) ? keys.Single() : null,
                body,
                request.Headers.TryGetValues(BitwardenHeaders.Organization, out var organizations) ? organizations.Single() : null));

            var step = script[Math.Min(Attempts.Count - 1, script.Length - 1)];
            if (step == Hang)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            return step(request);
        }
    }

    /// <summary>
    /// Identity: issuing, or, while down, refusing connections.
    /// </summary>
    internal sealed class Switchable : HttpMessageHandler
    {
        public bool Down { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Down
                ? throw new HttpRequestException(HttpRequestError.ConnectionError, "Connection refused (identity.invalid:80)")
                : Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """{"access_token":"token-1","expires_in":3600,"token_type":"Bearer"}""",
                        Encoding.UTF8,
                        "application/json"),
                });
    }
}
