using System.Diagnostics.Metrics;

namespace Bitwarden.Server.Sdk.MessageBroker.Tests;

/// <summary>
/// Bare <see cref="IMeterFactory"/> for unit tests that construct metrics classes directly
/// without spinning up a full DI host. Production code resolves <see cref="IMeterFactory"/>
/// via <c>AddMetrics()</c>; a test that asserts on emissions instead attaches a
/// <c>MetricCollector</c> to its own factory instance.
/// </summary>
internal sealed class TestMeterFactory : IMeterFactory
{
    public static readonly TestMeterFactory Instance = new();

    private readonly List<Meter> _meters = [];

    public Meter Create(MeterOptions options)
    {
        var meter = new Meter(options);
        _meters.Add(meter);
        return meter;
    }

    public void Dispose()
    {
        foreach (var meter in _meters) meter.Dispose();
        _meters.Clear();
    }
}
