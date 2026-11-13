using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Bitwarden.Server.Sdk.Service.Sagas;
using Microsoft.Extensions.DependencyInjection;

namespace Bitwarden.Server.Sdk.Service.Tests.Sagas;

/// <summary>
/// The count of operations whose writes spanned services, under the names a dashboard queries it by.
/// </summary>
public sealed class WriteSpanMetricsTests
{
    // Written out rather than read from the type's constants, so renaming one is seen here as the break it is.
    [Fact]
    public void Spanned_IsCountedUnderItsPublishedNames()
    {
        using var provider = new ServiceCollection().AddMetrics().BuildServiceProvider();
        var factory = provider.GetRequiredService<IMeterFactory>();
        var counted = new ConcurrentQueue<(string Meter, string Instrument, long Value, Dictionary<string, object?> Tags)>();
        using var listener = new MeterListener
        {
            // Only this test's meter: the listener hears every meter in the process.
            InstrumentPublished = (instrument, listening) =>
            {
                if (ReferenceEquals(instrument.Meter.Scope, factory))
                {
                    listening.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            counted.Enqueue((instrument.Meter.Name, instrument.Name, value, tags.ToArray().ToDictionary(tag => tag.Key, tag => tag.Value))));
        listener.Start();

        new WriteSpanMetrics(factory).Spanned("POST /widgets", declared: true, unhandled: false);

        var (meter, instrument, value, tags) = Assert.Single(counted);
        Assert.Equal("Bitwarden.Server.Sdk.Service.Sagas", meter);
        Assert.Equal("bitwarden.operation.spans", instrument);
        Assert.Equal(1, value);
        Assert.Equal("POST /widgets", tags["bitwarden.operation"]);
        Assert.Equal(true, tags["bitwarden.operation.spans.declared"]);
        Assert.Equal(false, tags["bitwarden.operation.spans.unhandled"]);
    }
}
