using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Bitwarden.Server.Sdk.HealthChecks;
using Bitwarden.Server.Sdk.Service.Observability;
using Bitwarden.Server.Sdk.Service.Tests.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace Bitwarden.Server.Sdk.Service.Tests.Observability;

// The checks readiness leaves out are seen somewhere: a gauge of each one's status, and a log line
// when it changes, at a level an alert can be set on.
public sealed class HealthStatusPublisherTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Publish_StatusChanges_LogsEachChangeOnceAtItsLevel()
    {
        var (publisher, logger, _) = Publisher();

        await publisher.PublishAsync(Report(HealthStatus.Healthy), Token);
        await publisher.PublishAsync(Report(HealthStatus.Degraded, "3 event(s) wait."), Token);
        await publisher.PublishAsync(Report(HealthStatus.Degraded, "4 event(s) wait."), Token);
        await publisher.PublishAsync(Report(HealthStatus.Unhealthy, "5 event(s) wait."), Token);
        await publisher.PublishAsync(Report(HealthStatus.Healthy), Token);

        Assert.Equal(
            [
                (LogLevel.Warning, "Health check Outbox is Degraded (was Healthy): 3 event(s) wait."),
                (LogLevel.Error, "Health check Outbox is Unhealthy (was Degraded): 5 event(s) wait."),
                (LogLevel.Information, "Health check Outbox is Healthy (was Unhealthy): no description"),
            ],
            logger.Entries);
    }

    // The checks readiness asks are reported by readiness; only the others are this publisher's.
    [Fact]
    public async Task Publish_ACheckReadinessAsks_Ignored()
    {
        var (publisher, logger, measurements) = Publisher();

        await publisher.PublishAsync(Report(HealthStatus.Unhealthy, "not subscribed", monitoring: false), Token);
        measurements.Observe();

        Assert.Empty(logger.Entries);
        Assert.Empty(measurements.Values);
    }

    [Theory]
    [InlineData(HealthStatus.Healthy, 0)]
    [InlineData(HealthStatus.Degraded, 1)]
    [InlineData(HealthStatus.Unhealthy, 2)]
    public async Task Gauge_ReportsTheLastStatusByCheck(HealthStatus status, int expected)
    {
        var (publisher, _, measurements) = Publisher();

        await publisher.PublishAsync(Report(status), Token);
        measurements.Observe();

        var (value, check) = Assert.Single(measurements.Values);
        Assert.Equal(expected, value);
        Assert.Equal("Outbox", check);
    }

    // Registered by the platform for every service.
    [Fact]
    public async Task Service_RegistersThePublisher()
    {
        await using var service = await SessionHost.StartAsync(productService: false);

        Assert.Contains(
            service.Resolve<IEnumerable<IHealthCheckPublisher>>(),
            publisher => publisher is HealthStatusPublisher);
    }

    private static HealthReport Report(HealthStatus status, string? description = null, bool monitoring = true) =>
        new(
            new Dictionary<string, HealthReportEntry>
            {
                [monitoring ? "Outbox" : "Subscriptions"] = new(
                    status,
                    description,
                    TimeSpan.Zero,
                    exception: null,
                    data: null,
                    tags: monitoring ? [HealthCheckTags.Monitoring] : []),
            },
            TimeSpan.Zero);

    private static (HealthStatusPublisher Publisher, CapturedLogger Logger, Gauge Measurements) Publisher()
    {
        var meters = new ServiceCollection().AddMetrics().BuildServiceProvider().GetRequiredService<IMeterFactory>();
        var logger = new CapturedLogger();
        var gauge = new Gauge(meters);

        return (new HealthStatusPublisher(meters, logger), logger, gauge);
    }

    private sealed class Gauge : IDisposable
    {
        private readonly MeterListener _listener = new();
        private readonly ConcurrentQueue<(int Value, string? Check)> _values = new();

        public Gauge(IMeterFactory scope)
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Scope == scope && instrument.Name == "bitwarden.health.status")
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };
            _listener.SetMeasurementEventCallback<int>((_, value, tags, _) =>
                _values.Enqueue((value, tags.ToArray().FirstOrDefault(tag => tag.Key == "bitwarden.health.check").Value as string)));
            _listener.Start();
        }

        public IReadOnlyCollection<(int Value, string? Check)> Values => _values;

        public void Observe() => _listener.RecordObservableInstruments();

        public void Dispose() => _listener.Dispose();
    }

    private sealed class CapturedLogger : ILogger<HealthStatusPublisher>
    {
        private readonly ConcurrentQueue<(LogLevel Level, string Message)> _entries = new();

        public IReadOnlyCollection<(LogLevel Level, string Message)> Entries => _entries;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            _entries.Enqueue((logLevel, formatter(state, exception)));
    }
}
