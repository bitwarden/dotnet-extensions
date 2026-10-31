using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using Bitwarden.Server.Sdk.Service.Observability;
using Bitwarden.Server.Sdk.Service.Tests.TestHost;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bitwarden.Server.Sdk.Service.Tests.Observability;

/// <summary>
/// A service sends the signals its switches say, and no query string in any, and a trace
/// survives the hop from a caller, through the service, to the service it calls.
/// </summary>
public sealed class TelemetryTests
{
    /// <summary>
    /// A query value that must reach no collector.
    /// </summary>
    private const string Credential = "a-credential-carried-in-the-query";

    private static readonly Guid Person = new("a1000000-0000-4000-8000-0000000000f1");

    /// <summary>
    /// Exporters off: what is asserted is the trace, not that a collector answered.
    /// </summary>
    private static readonly Dictionary<string, string?> NoExport = new() { [Telemetry.EnabledKey] = "false" };

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    // Which exporters each configuration turns on: the defaults and their overrides.
    public static TheoryData<string, Dictionary<string, string?>, bool, bool, bool> Configurations => new()
    {
        { "cloud, by default", [], true, true, true },
        { "self-hosted, by default", new() { [SelfHosting.Key] = "true" }, false, false, false },
        { "cloud, switched off", new() { [Telemetry.EnabledKey] = "false" }, false, false, false },
        {
            "self-hosted, switched on",
            new() { [SelfHosting.Key] = "true", [Telemetry.EnabledKey] = "true" },
            true, true, true
        },
        {
            "self-hosted, traces alone switched on",
            new() { [SelfHosting.Key] = "true", [Telemetry.TracingKey] = "true" },
            true, false, false
        },
        {
            "off, metrics alone switched on",
            new() { [Telemetry.EnabledKey] = "false", [Telemetry.MetricsKey] = "true" },
            false, true, false
        },
        {
            "on, logs alone switched off",
            new() { [Telemetry.EnabledKey] = "true", [Telemetry.LoggingKey] = "false" },
            true, true, false
        },
    };

    [Theory]
    [MemberData(nameof(Configurations))]
    public void Resolve(string because, Dictionary<string, string?> settings, bool traces, bool metrics, bool logs)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        Assert.True(new Exports(traces, metrics, logs) == Telemetry.Resolve(configuration, export: true), because);
    }

    // A process that only describes itself (--emit-openapi) sends nothing, whatever the switches say.
    [Fact]
    public void Resolve_DescribingItself_ExportsNothing()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [Telemetry.EnabledKey] = "true",
            [Telemetry.TracingKey] = "true",
            [Telemetry.MetricsKey] = "true",
            [Telemetry.LoggingKey] = "true",
        }).Build();

        Assert.Equal(new Exports(false, false, false), Telemetry.Resolve(configuration, export: false));
    }

    // The exporters a started service actually has, read from what reaches a collector: each signal
    // is sent exactly when its switch says, over OTLP to the configured endpoint.
    [Theory]
    [MemberData(nameof(Configurations))]
    public async Task Started_SendsWhatItsSwitchesSay(
        string because,
        Dictionary<string, string?> settings,
        bool traces,
        bool metrics,
        bool logs)
    {
        var received = await Collect(settings);

        Assert.True(traces == received.ContainsKey("traces"), $"{because}: traces");
        Assert.True(metrics == received.ContainsKey("metrics"), $"{because}: metrics");
        Assert.True(logs == received.ContainsKey("logs"), $"{because}: logs");
    }

    // The raw OTLP payload, not a dashboard's rendering of it: a request's query string, which here
    // can carry a credential, reaches the collector in no signal.
    [Fact]
    public async Task Started_SendsNoQueryString()
    {
        var received = await Collect(new Dictionary<string, string?> { [Telemetry.EnabledKey] = "true" });

        var traces = Assert.Contains("traces", received);
        Assert.Contains(traces, payload => Contains(payload, "/docs/index.html"));
        Assert.All(received.Values.SelectMany(payloads => payloads), payload => Assert.False(Contains(payload, Credential)));
    }

    // The probes aren't traced: none of their spans reaches the collector.
    [Fact]
    public async Task Started_SendsNoProbeSpan()
    {
        var received = await Collect(new Dictionary<string, string?> { [Telemetry.EnabledKey] = "true" });

        var traces = Assert.Contains("traces", received);
        Assert.DoesNotContain(traces, payload => Contains(payload, "/api/v1/health"));
    }

    /// <summary>
    /// Starts the service against a collector on a loopback port, over OTLP's default gRPC as the
    /// stack sends it, makes two requests, waits for their spans to end, logs a warning, stops it
    /// (which flushes every exporter) and returns each signal's payloads.
    /// </summary>
    /// <remarks>
    /// The test server answers a request before its span ends: it completes the response, which
    /// is all the client waits for, and only then stops the request's activity, on a thread of its
    /// own; and stopping the server doesn't wait for that. Without the wait, the tracer provider
    /// could flush and shut down before the span reached it, and the trace would never be sent.
    /// Kestrel drains requests as it stops, so a running service has no such gap.
    /// </remarks>
    private static async Task<IReadOnlyDictionary<string, List<byte[]>>> Collect(
        IReadOnlyDictionary<string, string?> settings)
    {
        await using var collector = await Collector.StartAsync();
        var configuration = new Dictionary<string, string?>(settings)
        {
            ["OTEL_EXPORTER_OTLP_ENDPOINT"] = collector.Address,
        };

        await using (var service = await SessionHost.StartAsync(
            productService: false,
            configuration,
            services => services.ConfigureHttpClientDefaults(client =>
                client.ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler()))))
        {
            // Both requests carry this trace, so only their spans count, not another test's.
            var trace = ActivityTraceId.CreateRandom();
            using var ended = new RequestSpans(trace, count: 2);

            using (await service.Client().SendAsync(Traced("/api/v1/health", trace), Token))
            using (await service.Client().SendAsync(Traced($"/docs/index.html?token={Credential}", trace), Token))
            {
            }

            await ended.Task.WaitAsync(Token);
            service.Resolve<ILogger<TelemetryTests>>().LogWarning("A record for the log exporter.");
        }

        return collector.Received.ToDictionary(pair => pair.Key, pair => pair.Value.ToList());
    }

    private static HttpRequestMessage Traced(string path, ActivityTraceId trace)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("traceparent", $"00-{trace}-{ActivitySpanId.CreateRandom()}-01");
        return request;
    }

    /// <summary>
    /// Completes once a number of the service's request spans in one trace have ended, and the
    /// tracer provider has each.
    /// </summary>
    /// <remarks>
    /// An activity source tells its listeners in the order they were added, on the thread that
    /// stops the activity. This one is added after the service started, so after the tracer
    /// provider's: by the time it hears that a span ended, the provider's processors have it.
    /// </remarks>
    private sealed class RequestSpans : IDisposable
    {
        private readonly TaskCompletionSource _ended = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly ActivityListener _listener;
        private int _remaining;

        public RequestSpans(ActivityTraceId trace, int count)
        {
            _remaining = count;
            _listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == "Microsoft.AspNetCore",
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.PropagationData,
                ActivityStopped = activity =>
                {
                    if (activity.Kind == ActivityKind.Server
                        && activity.TraceId == trace
                        && Interlocked.Decrement(ref _remaining) == 0)
                    {
                        _ended.TrySetResult();
                    }
                },
            };
            ActivitySource.AddActivityListener(_listener);
        }

        public Task Task => _ended.Task;

        public void Dispose() => _listener.Dispose();
    }

    private static bool Contains(byte[] payload, string text) =>
        payload.AsSpan().IndexOf(System.Text.Encoding.UTF8.GetBytes(text)) >= 0;

    // The hop the trace has to survive: the caller's traceparent continues in the service, and the
    // service's call downstream carries the same trace, under the service's own span. Over a real
    // connection, since the handler that writes traceparent is the socket handler itself.
    [Fact]
    public async Task Request_WithATraceparent_CallsDownstreamInTheSameTrace()
    {
        await using var downstream = await Downstream.StartAsync();
        var configuration = new Dictionary<string, string?>(NoExport)
        {
            [$"Services:{SessionHost.Downstream}:BaseAddress"] = downstream.Address,
            [$"Services:{SessionHost.Downstream}:IdentityUri"] = downstream.Address,
        };
        await using var service = await SessionHost.StartAsync(
            productService: true,
            configuration,
            services => services.ConfigureHttpClientDefaults(client =>
                client.ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler())));

        var trace = ActivityTraceId.CreateRandom();
        var callerSpan = ActivitySpanId.CreateRandom();
        using var request = ProductServiceAttributeTests.Get("/api/v1/probes/downstream", SessionHost.Token(Person, SessionHost.Scope));
        request.Headers.Add("traceparent", $"00-{trace}-{callerSpan}-01");

        using var response = await service.Client().SendAsync(request, Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var sent = Assert.Single(downstream.TraceParents);
        Assert.True(ActivityContext.TryParse(sent, null, out var downstreamParent), sent);
        Assert.Equal(trace, downstreamParent.TraceId);
        Assert.NotEqual(callerSpan, downstreamParent.SpanId);
    }

    // An orchestrator asks these constantly; tracing each is noise.
    [Theory]
    [InlineData("GET", "/api/v1/health", true)]
    [InlineData("GET", "/api/v1/readiness", true)]
    [InlineData("GET", "/API/V1/STARTUP", true)]
    [InlineData("POST", "/api/v1/health", false)]
    [InlineData("GET", "/api/v1/probes/current", false)]
    public void IsProbe(string method, string path, bool probe)
    {
        var http = new DefaultHttpContext();
        http.Request.Method = method;
        http.Request.Path = path;

        Assert.Equal(probe, Telemetry.IsProbe(http.Request));
    }

    /// <summary>
    /// An OTLP/gRPC collector on a loopback port, keeping every payload by signal as it arrived:
    /// <c>traces</c>, <c>metrics</c> or <c>logs</c>.
    /// </summary>
    private sealed class Collector : IAsyncDisposable
    {
        private readonly WebApplication _app;

        private Collector(WebApplication app, ConcurrentDictionary<string, ConcurrentQueue<byte[]>> received)
        {
            _app = app;
            Received = received;
        }

        public ConcurrentDictionary<string, ConcurrentQueue<byte[]>> Received { get; }

        public string Address => _app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses.First();

        public static async Task<Collector> StartAsync()
        {
            var received = new ConcurrentDictionary<string, ConcurrentQueue<byte[]>>();
            var builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.UseKestrel(kestrel =>
                kestrel.ConfigureEndpointDefaults(endpoint => endpoint.Protocols = HttpProtocols.Http2));
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();
            var app = builder.Build();

            // /opentelemetry.proto.collector.{signal}.v1.{Signal}Service/Export
            app.Run(async http =>
            {
                var signal = (http.Request.Path.Value ?? "").Split('.') is [_, _, _, var name, ..]
                    ? name == "trace" ? "traces" : name
                    : "";
                using var body = new MemoryStream();
                await http.Request.Body.CopyToAsync(body);
                received.GetOrAdd(signal, _ => new()).Enqueue(body.ToArray());

                // An empty Export response, framed, and the status that says it succeeded.
                http.Response.ContentType = "application/grpc";
                await http.Response.Body.WriteAsync(new byte[5]);
                http.Response.AppendTrailer("grpc-status", "0");
            });

            await app.StartAsync(Token);
            return new Collector(app, received);
        }

        public async ValueTask DisposeAsync()
        {
            await _app.StopAsync(CancellationToken.None);
            await _app.DisposeAsync();
        }
    }

    /// <summary>
    /// A downstream service and its Identity on a loopback port, recording the traceparent of each
    /// call that isn't for a token.
    /// </summary>
    private sealed class Downstream : IAsyncDisposable
    {
        private readonly WebApplication _app;

        private Downstream(WebApplication app, ConcurrentQueue<string> traceParents)
        {
            _app = app;
            TraceParents = traceParents;
        }

        public ConcurrentQueue<string> TraceParents { get; }

        public string Address => _app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses.First();

        public static async Task<Downstream> StartAsync()
        {
            var traceParents = new ConcurrentQueue<string>();
            var builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();
            var app = builder.Build();

            app.Run(async http =>
            {
                if (http.Request.Path.Value?.EndsWith("/connect/token", StringComparison.Ordinal) == true)
                {
                    http.Response.ContentType = "application/json";
                    await http.Response.WriteAsync(
                        $$"""{"access_token":"{{SessionHost.ServiceToken}}","expires_in":300,"token_type":"Bearer"}""");
                    return;
                }

                traceParents.Enqueue(http.Request.Headers["traceparent"].ToString());
                http.Response.ContentType = "application/json";
                await http.Response.WriteAsync("{}");
            });

            await app.StartAsync(Token);
            return new Downstream(app, traceParents);
        }

        public async ValueTask DisposeAsync()
        {
            await _app.StopAsync(CancellationToken.None);
            await _app.DisposeAsync();
        }
    }
}
