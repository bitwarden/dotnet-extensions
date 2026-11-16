using Microsoft.Extensions.DependencyInjection;
using static Bitwarden.Server.Sdk.MessageBroker.Sender.Tests.OutboxTestHost;

namespace Bitwarden.Server.Sdk.MessageBroker.Sender.Tests;

/// <summary>
/// The size limit on what is written to the outbox.
/// </summary>
public sealed class OutboxRowsTests : IAsyncDisposable
{
    private readonly OutboxTestHost _outbox = new();

    public ValueTask DisposeAsync() => _outbox.DisposeAsync();

    [Fact]
    public async Task AnOversizedMessageIsRefusedWhenWritten()
    {
        var host = await _outbox.BuildAsync(services => services.Configure<MessageOutboxOptions>(o => o.MaxMessageSize = 10));
        await using var scope = host.Services.CreateAsyncScope();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Sender<OrderPlacedPayload, OrderPlaced>(scope.ServiceProvider, Topic).Publish(new OrderPlaced(1))
                .SendAsync(Token));
    }

    [Fact]
    public async Task ALocalMessageLargerThanABrokerTakesIsStillWrittenAndHandled()
    {
        // No broker's limit applies to a message that never leaves the service.
        var host = await _outbox.BuildAsync(services => services.Configure<MessageOutboxOptions>(o => o.MaxMessageSize = 10));
        await ReserveStockAsync(host, 4, "reserve-4");

        await Dispatcher(host).DispatchAsync(Token);

        Assert.Equal((4, "reserve-4", 1), Assert.Single(host.Services.GetRequiredService<StockReservations>().Handled));
    }
}
