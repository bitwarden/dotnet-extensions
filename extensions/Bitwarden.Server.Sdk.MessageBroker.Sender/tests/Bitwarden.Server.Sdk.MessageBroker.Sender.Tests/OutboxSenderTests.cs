using Microsoft.Extensions.DependencyInjection;
using static Bitwarden.Server.Sdk.MessageBroker.Sender.Tests.OutboxTestHost;

namespace Bitwarden.Server.Sdk.MessageBroker.Sender.Tests;

public sealed class OutboxSenderTests : IAsyncDisposable
{
    private readonly OutboxTestHost _outbox = new();

    public ValueTask DisposeAsync() => _outbox.DisposeAsync();

    [Fact]
    public async Task TheSenderAddsEachTypeForItsOneDestination()
    {
        var host = await _outbox.BuildAsync();

        var placed = await SendAsync<OrderPlacedPayload, OrderPlaced>(host, new OrderPlaced(1));
        await SendAsync<SendReceiptPayload, SendReceipt>(host, new SendReceipt(1), "receipt-1");
        await SendAsync<ReserveStockPayload, ReserveStock>(host, new ReserveStock(1), "reserve-1");

        var rows = await RowsAsync(host);
        Assert.Equal(
            [("receipt-1", Queue, SendReceiptPayload.WireName), ("reserve-1", MessageOutbox.Local, ReserveStockPayload.WireName), (placed, Topic, OrderPlacedPayload.WireName)],
            rows.Select(row => (row.MessageId, row.Destination, row.MessageType)).OrderBy(row => row.MessageId == placed).ThenBy(row => row.MessageId));
        Assert.True(Guid.TryParse(placed, out var id) && id.Version == 7);
    }

    [Fact]
    public async Task TheSendersMessageIsSentAsAPublishersIs()
    {
        var host = await _outbox.BuildAsync(start: true);

        await SendAsync<OrderPlacedPayload, OrderPlaced>(host, new OrderPlaced(11), "message-11");

        Assert.Equal((OrderPlacedPayload.WireName, 11, "message-11"), await host.Services.GetRequiredService<Received>().WaitForAsync(11));
    }

    [Fact]
    public async Task TheSenderHoldsAMessageForLater()
    {
        var host = await _outbox.BuildAsync();
        var notBefore = DateTimeOffset.UtcNow.AddHours(1);

        await SendAsync<OrderPlacedPayload, OrderPlaced>(host, new OrderPlaced(1), "later-1", notBefore);

        Assert.Equal(0, await Dispatcher(host).DispatchAsync(Token));
        Assert.Equal(notBefore.ToUnixTimeMilliseconds(), Assert.Single(await RowsAsync(host)).AvailableAt);
    }

    [Fact]
    public async Task TheSenderSavesNothingItself()
    {
        var host = await _outbox.BuildAsync();
        await using (var scope = host.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IOutboxSender>().SendAsync<OrderPlacedPayload, OrderPlaced>(new OrderPlaced(1), cancellationToken: Token);
        }

        Assert.Empty(await RowsAsync(host));
    }

    [Fact]
    public async Task TheSenderRefusesATypeNotRegisteredThroughTheOutbox()
    {
        var host = await _outbox.BuildAsync();
        await using var scope = host.Services.CreateAsyncScope();

        await Assert.ThrowsAsync<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<IOutboxSender>()
            .SendAsync<RefundIssuedPayload, RefundIssued>(new RefundIssued(1), cancellationToken: Token));
    }

    [Fact]
    public async Task TheSenderRefusesATypeRegisteredToSeveralDestinations()
    {
        var host = await _outbox.BuildAsync(services => services.AddOutboxPublisher<OrderPlacedPayload, OrderPlaced, ShopContext>("audit.events"));
        await using var scope = host.Services.CreateAsyncScope();

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<IOutboxSender>()
            .SendAsync<OrderPlacedPayload, OrderPlaced>(new OrderPlaced(1), cancellationToken: Token));
        Assert.Contains("audit.events", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheSenderRefusesAnInvalidIdentifier()
    {
        var host = await _outbox.BuildAsync();
        await using var scope = host.Services.CreateAsyncScope();

        await Assert.ThrowsAsync<ArgumentException>(() => scope.ServiceProvider.GetRequiredService<IOutboxSender>()
            .SendAsync<OrderPlacedPayload, OrderPlaced>(new OrderPlaced(1), new string('m', 129), cancellationToken: Token));
    }
}
