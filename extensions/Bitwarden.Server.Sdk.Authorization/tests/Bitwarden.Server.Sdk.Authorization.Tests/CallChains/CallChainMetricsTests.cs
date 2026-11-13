using System.Diagnostics.Metrics;
using Bitwarden.Server.Sdk.Authorization.CallChains;
using Microsoft.Extensions.DependencyInjection;

namespace Bitwarden.Server.Sdk.Authorization.Tests.CallChains;

/// <summary>
/// Each re-entry is counted once on the published meter, tagged with the chain, the route and whether it was accepted.
/// </summary>
public sealed class CallChainMetricsTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Reentered_CountsOneWithItsTags(bool accepted)
    {
        using var services = new ServiceCollection().AddMetrics().BuildServiceProvider();
        var factory = services.GetRequiredService<IMeterFactory>();
        using var recorded = new Recorded(factory);
        var metrics = new CallChainMetrics(factory);

        metrics.Reentered("widget-service,gadget-service,widget-service", "api/v1/widgets/{id}", accepted);

        var (value, tags) = Assert.Single(recorded.Measurements);
        Assert.Equal(1, value);
        Assert.Equal(3, tags.Count);
        Assert.Equal("widget-service,gadget-service,widget-service", tags[CallChainMetrics.ChainTag]);
        Assert.Equal("api/v1/widgets/{id}", tags[CallChainMetrics.RouteTag]);
        Assert.Equal(accepted, tags[CallChainMetrics.AcceptedTag]);
    }

    // Dashboards and alerts select the meter and the counter by name.
    [Fact]
    public void TheCounter_IsPublishedUnderItsNames()
    {
        using var services = new ServiceCollection().AddMetrics().BuildServiceProvider();
        var factory = services.GetRequiredService<IMeterFactory>();
        using var recorded = new Recorded(factory);

        _ = new CallChainMetrics(factory);

        var counter = Assert.Single(recorded.Instruments);
        Assert.Equal("Bitwarden.Server.Sdk.Service.CallChains", counter.Meter.Name);
        Assert.Equal("bitwarden.call.reentries", counter.Name);
        Assert.Equal("{reentry}", counter.Unit);
    }

    /// <summary>
    /// The re-entry counter's measurements from one meter factory.
    /// </summary>
    private sealed class Recorded : IDisposable
    {
        private readonly MeterListener _listener = new();

        public Recorded(IMeterFactory factory)
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (ReferenceEquals(instrument.Meter.Scope, factory) && instrument.Name == CallChainMetrics.ReentriesName)
                {
                    Instruments.Add(instrument);
                    listener.EnableMeasurementEvents(instrument);
                }
            };
            _listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
            {
                var recorded = new Dictionary<string, object?>();
                foreach (var tag in tags)
                {
                    recorded[tag.Key] = tag.Value;
                }

                Measurements.Add((value, recorded));
            });
            _listener.Start();
        }

        public List<Instrument> Instruments { get; } = [];

        public List<(long Value, Dictionary<string, object?> Tags)> Measurements { get; } = [];

        public void Dispose() => _listener.Dispose();
    }
}
