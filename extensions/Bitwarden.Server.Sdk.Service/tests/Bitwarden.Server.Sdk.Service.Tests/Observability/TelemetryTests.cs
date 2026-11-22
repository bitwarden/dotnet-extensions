using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
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
using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

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
        var (received, _) = await Collect(settings);

        Assert.True(traces == received.ContainsKey("traces"), $"{because}: traces");
        Assert.True(metrics == received.ContainsKey("metrics"), $"{because}: metrics");
        Assert.True(logs == received.ContainsKey("logs"), $"{because}: logs");
    }

    // The raw OTLP payload, not a dashboard's rendering of it: a request's query string, which here
    // can carry a credential, reaches the collector in no signal.
    [Fact]
    public async Task Started_SendsNoQueryString()
    {
        var (received, trace) = await Collect(new Dictionary<string, string?> { [Telemetry.EnabledKey] = "true" });

        Assert.Contains(SpansIn(trace, received), span => Contains(span, "/docs/index.html"));
        Assert.All(received.Values.SelectMany(payloads => payloads), payload => Assert.False(Contains(payload, Credential)));
    }

    // The probes aren't traced: none of their spans reaches the collector. Only the service's own
    // trace is read: a provider listens process-wide, so a concurrent test's calls, a probe on
    // another host among them, can reach this collector too.
    [Fact]
    public async Task Started_SendsNoProbeSpan()
    {
        var (received, trace) = await Collect(new Dictionary<string, string?> { [Telemetry.EnabledKey] = "true" });

        var spans = SpansIn(trace, received);
        Assert.Contains(spans, span => Contains(span, "/docs/index.html"));
        Assert.DoesNotContain(spans, span => Contains(span, "/api/v1/health"));
    }

    /// <summary>
    /// Starts the service against a collector on a loopback port, over OTLP's default gRPC as the
    /// stack sends it, makes two requests, waits for their spans to end, sends their trace, logs a
    /// warning, stops it (which flushes every exporter) and returns each signal's payloads.
    /// </summary>
    /// <remarks>
    /// The test server answers a request before its span ends: it completes the response, which
    /// is all the client waits for, and only then stops the request's activity, on a thread of its
    /// own; and stopping the server doesn't wait for that. Without the wait, the tracer provider
    /// could flush and shut down before the span reached it, and the trace would never be sent.
    /// Kestrel drains requests as it stops, so a running service has no such gap.
    /// </remarks>
    private static async Task<(IReadOnlyDictionary<string, List<byte[]>> Received, ActivityTraceId Trace)> Collect(
        IReadOnlyDictionary<string, string?> settings)
    {
        await using var collector = await Collector.StartAsync();
        var configuration = new Dictionary<string, string?>(settings)
        {
            ["OTEL_EXPORTER_OTLP_ENDPOINT"] = collector.Address,
        };

        // Both requests carry this trace, so only their spans count, not another test's.
        var trace = ActivityTraceId.CreateRandom();
        await using (var service = await SessionHost.StartAsync(
            productService: false,
            configuration,
            services => services.ConfigureHttpClientDefaults(client =>
                client.ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler()))))
        {
            using var ended = new RequestSpans(trace, count: 2);

            using (await service.Client().SendAsync(Traced("/api/v1/health", trace), Token))
            using (await service.Client().SendAsync(Traced($"/docs/index.html?token={Credential}", trace), Token))
            {
            }

            await ended.Task.WaitAsync(Token);

            // Sent before the service stops: stopping gives the exporters only a few seconds, which a loaded run can
            // outlast, and the trace would then never reach the collector.
            Assert.True(service.Resolve<TracerProvider>().ForceFlush(timeoutMilliseconds: 60_000));
            service.Resolve<ILogger<TelemetryTests>>().LogWarning("A record for the log exporter.");
        }

        return (collector.Received.ToDictionary(pair => pair.Key, pair => pair.Value.ToList()), trace);
    }

    /// <summary>
    /// The encoded spans of one trace among the collected trace exports.
    /// </summary>
    private static List<byte[]> SpansIn(ActivityTraceId trace, IReadOnlyDictionary<string, List<byte[]>> received) =>
        [.. Assert.Contains("traces", received).SelectMany(Spans).Where(span => span.Trace == trace).Select(span => span.Bytes)];

    /// <summary>
    /// Each span in an OTLP/gRPC trace export, with its trace: the body's length-prefixed messages,
    /// each an ExportTraceServiceRequest whose resource_spans (1) hold scope_spans (2), which hold
    /// spans (2), each with its trace_id (1).
    /// </summary>
    private static List<(ActivityTraceId Trace, byte[] Bytes)> Spans(byte[] payload)
    {
        var spans = new List<(ActivityTraceId, byte[])>();
        for (var offset = 0; offset + 5 <= payload.Length;)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(payload.AsSpan(offset + 1, 4));
            var request = payload.AsMemory(offset + 5, length);
            offset += 5 + length;
            foreach (var resourceSpans in Fields(request, 1))
            {
                foreach (var scopeSpans in Fields(resourceSpans, 2))
                {
                    foreach (var span in Fields(scopeSpans, 2))
                    {
                        var traceId = Assert.Single(Fields(span, 1));
                        spans.Add((ActivityTraceId.CreateFromBytes(traceId.Span), span.ToArray()));
                    }
                }
            }
        }

        return spans;
    }

    /// <summary>
    /// The length-delimited fields with one number in a protobuf message.
    /// </summary>
    private static List<ReadOnlyMemory<byte>> Fields(ReadOnlyMemory<byte> message, int number)
    {
        var fields = new List<ReadOnlyMemory<byte>>();
        var bytes = message.Span;
        for (var position = 0; position < bytes.Length;)
        {
            var tag = Varint(bytes, ref position);
            switch (tag & 7)
            {
                case 0:
                    Varint(bytes, ref position);
                    break;
                case 1:
                    position += 8;
                    break;
                case 5:
                    position += 4;
                    break;
                case 2:
                    var length = (int)Varint(bytes, ref position);
                    if ((int)(tag >> 3) == number)
                    {
                        fields.Add(message.Slice(position, length));
                    }

                    position += length;
                    break;
                default:
                    throw new InvalidDataException($"Wire type {tag & 7} isn't one OTLP uses.");
            }
        }

        return fields;
    }

    private static ulong Varint(ReadOnlySpan<byte> bytes, ref int position)
    {
        ulong value = 0;
        for (var shift = 0; ; shift += 7)
        {
            var b = bytes[position++];
            value |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0)
            {
                return value;
            }
        }
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
                        Last = activity;
                        _ended.TrySetResult();
                    }
                },
            };
            ActivitySource.AddActivityListener(_listener);
        }

        public Task Task => _ended.Task;

        /// <summary>
        /// The span that ended last, once <see cref="Task"/> has completed.
        /// </summary>
        public Activity? Last { get; private set; }

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

    // An event stream's spans, from a source under Bitwarden.*, and the RabbitMQ client's beneath them,
    // are listened to: without a listener none is ever created.
    [Theory]
    [InlineData("Bitwarden.Example.Events")]
    [InlineData("RabbitMQ.Client.Publisher")]
    [InlineData("RabbitMQ.Client.Subscriber")]
    public async Task Tracing_ListensToTheEventStreamsSources(string name)
    {
        await using var service = await SessionHost.StartAsync(productService: false, NoExport);
        _ = service.Resolve<TracerProvider>();
        using var source = new ActivitySource(name);

        Assert.True(source.HasListeners(), name);
    }

    // A meter under Bitwarden.* is collected: without a listener its instruments record nothing.
    [Fact]
    public async Task Metrics_ListenToTheBitwardenMeters()
    {
        await using var service = await SessionHost.StartAsync(
            productService: false,
            NoExport,
            services => services.ConfigureOpenTelemetryMeterProvider(metrics => metrics.AddReader(new BaseExportingMetricReader(new Discarding()))));
        _ = service.Resolve<MeterProvider>();
        using var meter = new Meter("Bitwarden.Example.Widgets");

        var counter = meter.CreateCounter<long>("bitwarden.example.widgets.made");

        Assert.True(counter.Enabled);
    }

    // A failure the exception handler can no longer answer, since the response had begun, still
    // says on the request's span what failed: the exception is on it as an event.
    [Fact]
    public async Task Tracing_AFailureAfterTheResponseBegan_RecordsTheExceptionOnTheSpan()
    {
        await using var service = await SessionHost.StartAsync(
            productService: false,
            NoExport,
            endpoints: endpoints => endpoints.MapGet("/telemetry-fault", Fault).AllowAnonymous().ExcludeFromDescription());
        _ = service.Resolve<TracerProvider>();
        var trace = ActivityTraceId.CreateRandom();
        using var ended = new RequestSpans(trace, count: 1);

        // The client sees a broken answer, however the test server reports it; the span is what is asserted.
        _ = await Record.ExceptionAsync(async () =>
        {
            using var response = await service.Client().SendAsync(Traced("/telemetry-fault", trace), HttpCompletionOption.ResponseHeadersRead, Token);
            _ = await response.Content.ReadAsStringAsync(Token);
        });
        await ended.Task.WaitAsync(Token);

        var span = Assert.IsType<Activity>(ended.Last);
        Assert.Contains(span.Events, recorded => recorded.Name == "exception");

        static async Task Fault(HttpContext http)
        {
            await http.Response.WriteAsync("a beginning");
            await http.Response.Body.FlushAsync(http.RequestAborted);
            throw new InvalidOperationException("A fault after the response began, for the span to record.");
        }
    }

    // The scope a request runs in (whom it is about) goes out with each log record.
    [Fact]
    public async Task Logging_IncludesScopes()
    {
        await using var service = await SessionHost.StartAsync(productService: false, NoExport);

        Assert.True(service.Resolve<IOptionsMonitor<OpenTelemetryLoggerOptions>>().CurrentValue.IncludeScopes);
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
    /// A metrics exporter that keeps nothing, standing in for the collector a deployment sends to.
    /// </summary>
    private sealed class Discarding : BaseExporter<Metric>
    {
        public override ExportResult Export(in Batch<Metric> batch) => ExportResult.Success;
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
