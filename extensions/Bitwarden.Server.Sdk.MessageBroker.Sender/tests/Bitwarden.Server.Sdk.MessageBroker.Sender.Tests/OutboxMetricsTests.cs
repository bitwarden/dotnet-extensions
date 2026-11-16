using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using static Bitwarden.Server.Sdk.MessageBroker.Sender.Tests.OutboxTestHost;

namespace Bitwarden.Server.Sdk.MessageBroker.Sender.Tests;

/// <summary>
/// The outbox's meter: what each sent message is counted with.
/// </summary>
public sealed class OutboxMetricsTests : IAsyncDisposable
{
    private readonly OutboxTestHost _outbox = new();

    public ValueTask DisposeAsync() => _outbox.DisposeAsync();

    [Fact]
    public async Task ASentMessageIsCountedWithItsDestinationAndType()
    {
        var host = await _outbox.BuildAsync();
        var meters = host.Services.GetRequiredService<IMeterFactory>();
        var counted = new ConcurrentBag<(string Instrument, Dictionary<string, object?> Tags)>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                // This host's meter only: other tests' hosts run alongside and share the name.
                if (instrument.Meter.Name == OutboxMetrics.MeterName && instrument.Meter.Scope == meters)
                    l.EnableMeasurementEvents(instrument);
            },
        };
        listener.SetMeasurementEventCallback<long>((instrument, _, tags, _) =>
            counted.Add((instrument.Name, tags.ToArray().ToDictionary(t => t.Key, t => t.Value))));
        listener.Start();

        await using (var scope = host.Services.CreateAsyncScope())
        {
            await Sender<OrderPlacedPayload, OrderPlaced>(scope.ServiceProvider, Topic)
                .Publish(new OrderPlaced(1)).SendAsync(Token);
            await scope.ServiceProvider.GetRequiredService<ShopContext>().SaveChangesAsync(Token);
        }
        await Dispatcher(host).DispatchAsync(Token);

        var sent = Assert.Single(counted, c => c.Instrument == "bitwarden.outbox.sent.messages");
        Assert.Equal("ShopContext", sent.Tags[OutboxMetrics.DatabaseTag]);
        Assert.Equal(Topic, sent.Tags[OutboxMetrics.DestinationTag]);
        Assert.Equal(OrderPlacedPayload.WireName, sent.Tags[OutboxMetrics.PayloadFamilyTag]);
        Assert.DoesNotContain(counted, c => c.Instrument != "bitwarden.outbox.sent.messages");
    }
}
