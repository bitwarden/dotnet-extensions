using System.Text.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.Logging;

namespace Bitwarden.Server.Sdk.IntegrationTests;

public class SdkTelemetryTests : IClassFixture<TelemetryProjectFixture>
{
    private const int TwentySeconds = 20 * 1000;

    private readonly TelemetryProjectFixture _fixture;

    public SdkTelemetryTests(TelemetryProjectFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact(Timeout = TwentySeconds)]
    public async Task CustomMetricsAndTracesWork()
    {
        using var result = await RunTelemetryAsync(new Dictionary<string, string?>
        {
            { "DD_ENV", "usprd" },
            { "Caching:Redis:Configuration", "redis" },
        });

        var resourceMetric = Assert.Single(result.Metrics!.RootElement.GetProperty("resourceMetrics").EnumerateArray());
        // Make sure that the service name is set
        var allMetricResourceAttributes = resourceMetric.GetProperty("resource").GetProperty("attributes").EnumerateArray();

        Assert.Single(
            allMetricResourceAttributes,
            a => a.GetProperty("key").GetString() == "service.name" && a.GetProperty("value").GetProperty("stringValue").GetString() == "Test"
        );

        // Check that DD_ENV environment variable became a env attribute
        Assert.Single(
            allMetricResourceAttributes,
            a => a.GetProperty("key").GetString() == "env" && a.GetProperty("value").GetProperty("stringValue").GetString() == "usprd"
        );

        // Our custom scope should exist
        var customScopeMetric = Assert.Single(
            resourceMetric.GetProperty("scopeMetrics").EnumerateArray(),
            sm => sm.GetProperty("scope").GetProperty("name").GetString() == "Bitwarden.Custom"
        );

        // Our custom metric should exist in that scope
        var customMetric = Assert.Single(
            customScopeMetric.GetProperty("metrics").EnumerateArray(),
            m => m.GetProperty("name").GetString() == "custom_counter"
        );

        var resourceSpan = Assert.Single(result.Traces!.RootElement.GetProperty("resourceSpans").EnumerateArray());

        var allSpanResourceAttributes = resourceSpan.GetProperty("resource").GetProperty("attributes").EnumerateArray();

        // Make sure that the service name is set
        Assert.Single(
            allSpanResourceAttributes,
            a => a.GetProperty("key").GetString() == "service.name" && a.GetProperty("value").GetProperty("stringValue").GetString() == "Test"
        );

        // Check that DD_ENV environment variable became a env attribute
        Assert.Single(
            allSpanResourceAttributes,
            a => a.GetProperty("key").GetString() == "env" && a.GetProperty("value").GetProperty("stringValue").GetString() == "usprd"
        );

        var allScopeSpans = resourceSpan.GetProperty("scopeSpans").EnumerateArray();

        var aspNetScope = Assert.Single(
            allScopeSpans,
            s => s.GetProperty("scope").GetProperty("name").GetString() == "Microsoft.AspNetCore"
        );

        // Check that there is a scope for redis
        var redisSpans = Assert.Single(
            allScopeSpans,
            s => s.GetProperty("scope").GetProperty("name").GetString() == "OpenTelemetry.Instrumentation.StackExchangeRedis"
        ).GetProperty("spans").EnumerateArray();

        Assert.Contains(
            redisSpans,
            s => s.GetProperty("name").GetString() == "SUBSCRIBE"
        );

        // Should have two Fusion cache spans
        Assert.Single(
            allScopeSpans,
            s => s.GetProperty("scope").GetProperty("name").GetString() == "ZiggyCreatures.Caching.Fusion.Distributed"
        );

        Assert.Single(
            allScopeSpans,
            s => s.GetProperty("scope").GetProperty("name").GetString() == "ZiggyCreatures.Caching.Fusion"
        );

        // We expect a span for our simple endpoint
        var requestSpan = Assert.Single(
            aspNetScope.GetProperty("spans").EnumerateArray(),
            s => s.GetProperty("name").GetString() == "GET /"
        );

        // Make sure our custom tag is represented as an attribute
        Assert.Single(
            requestSpan.GetProperty("attributes").EnumerateArray(),
            a => a.GetProperty("key").GetString() == "custom_tag" && a.GetProperty("value").GetProperty("stringValue").GetString() == "my_value"
        );

        // Spans prefixed with Bitwarden. should be automatically listened to
        var customScope = Assert.Single(
            allScopeSpans,
            s => s.GetProperty("scope").GetProperty("name").GetString() == "Bitwarden.MyFeature"
        );

        Assert.Single(
            customScope.GetProperty("spans").EnumerateArray(),
            s => s.GetProperty("name").GetString() == "MyOperation"
        );
    }

    [Fact(Timeout = TwentySeconds)]
    public async Task OtelEnvironmentVariableWins()
    {
        using var result = await RunTelemetryAsync(new Dictionary<string, string?>
        {
            { "OTEL_SERVICE_NAME", "SOME_NAME" },
        });

        Assert.Equal("SOME_NAME", result.GetTracesServiceName());
        Assert.Equal("SOME_NAME", result.GetMetricsServiceName());
    }

    [Fact(Timeout = TwentySeconds)]
    public async Task TelemetryCanBeDisabled()
    {
        using var result = await RunTelemetryAsync(new Dictionary<string, string?>
        {
            { "OpenTelemetry__Enabled", "false" },
        });

        Assert.Null(result.Metrics);
        Assert.Null(result.Traces);
    }

    [Fact(Timeout = TwentySeconds)]
    public async Task OnlyTracesCanBeDisabled()
    {
        using var result = await RunTelemetryAsync(new Dictionary<string, string?>
        {
            { "OpenTelemetry__Tracing__Enabled", "false" },
        });

        Assert.NotNull(result.Metrics);
        Assert.Null(result.Traces);
    }

    [Fact(Timeout = TwentySeconds)]
    public async Task OnlyMetricsCanBeDisabled()
    {
        using var result = await RunTelemetryAsync(new Dictionary<string, string?>
        {
            { "OpenTelemetry__Metrics__Enabled", "false" },
        });

        Assert.Null(result.Metrics);
        Assert.NotNull(result.Traces);
    }

    [Fact(Timeout = TwentySeconds)]
    public async Task SelfHostDoesNotDoTelemetryByDefault()
    {
        using var result = await RunTelemetryAsync(new Dictionary<string, string?>
        {
            { "GlobalSettings__SelfHosted", "true" },
        });

        Assert.Null(result.Metrics);
        Assert.Null(result.Traces);
    }

    [Fact(Timeout = TwentySeconds)]
    public async Task SelfHostCanDoTelemetryIfEnabled()
    {
        using var result = await RunTelemetryAsync(new Dictionary<string, string?>
        {
            { "GlobalSettings__SelfHosted", "true" },
            { "OpenTelemetry__Enabled", "true" },
        });

        Assert.NotNull(result.Metrics);
        Assert.NotNull(result.Traces);
    }

    private static async Task<TelemetryResult> RunTelemetryAsync(Dictionary<string, string?> environmentVariables)
    {
        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddProvider(new XUnitLoggerProvider());
        });

        await using var network = new NetworkBuilder()
            .WithLogger(loggerFactory.CreateLogger("Network"))
            .Build();

        await network.CreateAsync(TestContext.Current.CancellationToken);

        // If the operating system is windows it's not running in CI most likely and
        // likely not to have permissions issues.
        DirectoryInfo tempDir;
        if (!OperatingSystem.IsWindows())
        {
            tempDir = Directory.CreateDirectory(Path.GetTempPath(), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.OtherWrite);
        }
        else
        {
            tempDir = Directory.CreateTempSubdirectory();
        }

        await using var otelContainer = new ContainerBuilder()
            .WithImage("otel/opentelemetry-collector-contrib")
            .WithNetwork(network)
            .WithResourceMapping("""
            receivers:
              otlp:
                protocols:
                  grpc:
                    endpoint: 0.0.0.0:4317

            service:
              pipelines:
                traces:
                  receivers: [otlp]
                  exporters: [file/traces]
                metrics:
                  receivers: [otlp]
                  exporters: [file/metrics]

            exporters:
              file/traces:
                path: /data/traces.json
              file/metrics:
                path: /data/metrics.json
            """u8.ToArray(), "/etc/otelcol-contrib/config.yaml")
            .WithNetworkAliases("otelcol")
            .WithBindMount(tempDir.FullName, "/data")
            .WithPortBinding(16686, true)
            .WithLogger(loggerFactory.CreateLogger("OtelCol"))
            .Build();

        await otelContainer.StartAsync(TestContext.Current.CancellationToken);

        IContainer? redisContainer = null;

        if (environmentVariables.ContainsKey("Caching:Redis:Configuration"))
        {
            redisContainer = new ContainerBuilder()
                .WithImage("redis:latest")
                .WithNetwork(network)
                .WithNetworkAliases("redis")
                .WithLogger(loggerFactory.CreateLogger("Redis"))
                .Build();

            await redisContainer.StartAsync(TestContext.Current.CancellationToken);
        }

        await using var testContainer = new ContainerBuilder()
            .WithImage(TelemetryProjectFixture.ImageName)
            .WithNetwork(network)
            .DependsOn(otelContainer)
            .WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", $"http://otelcol:4317")
            .WithEnvironment(environmentVariables)
            .WithPortBinding(8080, true)
            // This wait strategy both ensures our container full starts up and causes a trace to get created.
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(8080)))
            .WithLogger(loggerFactory.CreateLogger("Example"))
            .Build();

        await LoggedStartAsync(testContainer, loggerFactory.CreateLogger("TestContainer"));

        await testContainer.StopAsync(TestContext.Current.CancellationToken);
        await otelContainer.StopAsync(TestContext.Current.CancellationToken);
        if (redisContainer is not null)
        {
            await redisContainer.StopAsync(TestContext.Current.CancellationToken);
        }

        var (testOutput, _) = await testContainer.GetLogsAsync(ct: TestContext.Current.CancellationToken);
        loggerFactory.CreateLogger("TestOutput").LogInformation("{Stdout}", testOutput);

        var (otelOutput, otelError) = await otelContainer.GetLogsAsync(ct: TestContext.Current.CancellationToken);
        loggerFactory.CreateLogger("OtelOutput").LogInformation("{Stdout}", otelOutput);
        loggerFactory.CreateLogger("OtelOutput").LogError("{Stderr}", otelError);

        async Task<JsonDocument?> ReadDoc(string type)
        {
            var filePath = Path.Join(tempDir.FullName, $"{type}.json");
            using var fs = File.OpenRead(filePath);

            if (fs.Length == 0)
            {
                return null;
            }

            return await JsonSerializer.DeserializeAsync<JsonDocument>(fs, cancellationToken: TestContext.Current.CancellationToken)
                    ?? throw new Exception("Should never be null");
        }

        return new TelemetryResult
        {
            Metrics = await ReadDoc("metrics"),
            Traces = await ReadDoc("traces"),
        };
    }

    // This is helpful in scenarios when the container might never pass its
    // health check and the tests will timeout. It creates its own
    // cancellation token that collects logs from the container before
    // actually cancelling the operation
    private static async Task LoggedStartAsync(IContainer container, ILogger logger)
    {
        var cancellationTokenSource = new CancellationTokenSource();
        TestContext.Current.CancellationToken.Register(() =>
        {
            var (stdout, stderr) = container.GetLogsAsync().GetAwaiter().GetResult();
            logger.LogInformation("{Out}", stdout);
            logger.LogError("{Error}", stderr);
            cancellationTokenSource.Cancel();
        });

        await container.StartAsync(cancellationTokenSource.Token);
    }

    internal sealed class TelemetryResult : IDisposable
    {
        public required JsonDocument? Metrics { get; init; }
        public required JsonDocument? Traces { get; init; }

        public string? GetTracesServiceName()
        {
            Assert.NotNull(Traces);
            return GetServiceName(Traces.RootElement.GetProperty("resourceSpans").EnumerateArray().First());
        }

        public string? GetMetricsServiceName()
        {
            Assert.NotNull(Metrics);
            return GetServiceName(Metrics.RootElement.GetProperty("resourceMetrics").EnumerateArray().First());
        }

        private static string? GetServiceName(JsonElement json)
        {
            var serviceNameAttribute = json.GetProperty("resource").GetProperty("attributes").EnumerateArray()
                .First(a => a.GetProperty("key").GetString() == "service.name");

            return serviceNameAttribute.GetProperty("value").GetProperty("stringValue").GetString();
        }

        public void Dispose()
        {
            Metrics?.Dispose();
            Traces?.Dispose();
        }
    }
}
