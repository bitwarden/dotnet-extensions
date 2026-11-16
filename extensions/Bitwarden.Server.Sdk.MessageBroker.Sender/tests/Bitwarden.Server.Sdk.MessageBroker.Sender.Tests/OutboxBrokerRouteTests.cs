using Microsoft.Extensions.DependencyInjection;
using static Bitwarden.Server.Sdk.MessageBroker.Sender.Tests.OutboxTestHost;

namespace Bitwarden.Server.Sdk.MessageBroker.Sender.Tests;

/// <summary>
/// Sending a stored message to the broker: to a command queue, and exactly as it was written.
/// </summary>
public sealed class OutboxBrokerRouteTests : IAsyncDisposable
{
    private readonly OutboxTestHost _outbox = new();

    public ValueTask DisposeAsync() => _outbox.DisposeAsync();

    [Fact(Timeout = 60 * 1000)]
    public async Task ACommandIsDeliveredToItsQueue()
    {
        var host = await _outbox.BuildAsync(start: true);
        await using (var scope = host.Services.CreateAsyncScope())
        {
            await Sender<SendReceiptPayload, SendReceipt>(scope.ServiceProvider, Queue).Publish(new SendReceipt(9))
                .SendAsync("message-9", Token);
            await scope.ServiceProvider.GetRequiredService<ShopContext>().SaveChangesAsync(Token);
        }

        var received = await host.Services.GetRequiredService<Received>().WaitForAsync(9);
        Assert.Equal((SendReceiptPayload.WireName, "message-9"), (received.Type, received.MessageId));
    }

    [Fact]
    public async Task AStoredBodyIsSentAsWrittenEvenWithAVersionThisBuildDoesNotKnow()
    {
        // A newer build wrote v2, which this one doesn't know. Decoding and re-encoding here would
        // drop it; the body must reach the queue exactly as written.
        var host = await _outbox.BuildAsync();
        var body = """[{"$type":"v2","OrderId":11,"Currency":"EUR"},{"$type":"v1","OrderId":11}]"""u8.ToArray();
        await InsertAsync(host, new OutboxMessage
        {
            MessageId = "versions",
            Destination = Queue,
            MessageType = SendReceiptPayload.WireName,
            Body = body,
        });

        await Dispatcher(host).DispatchAsync(Token);

        var queue = host.Services.GetRequiredKeyedService<ChannelCommandQueue>(Queue);
        await foreach (var message in queue.ReceiveAsync(() => { }, Token))
        {
            Assert.Equal(body, message.Body.ToArray());
            Assert.Equal("versions", message.MessageId);
            break;
        }
        Assert.Empty(await RowsAsync(host));
    }
}
