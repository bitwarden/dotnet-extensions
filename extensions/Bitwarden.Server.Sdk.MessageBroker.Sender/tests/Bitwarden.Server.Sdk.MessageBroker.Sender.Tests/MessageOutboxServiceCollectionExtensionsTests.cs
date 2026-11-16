using Microsoft.Extensions.DependencyInjection;
using static Bitwarden.Server.Sdk.MessageBroker.Sender.Tests.OutboxTestHost;

namespace Bitwarden.Server.Sdk.MessageBroker.Sender.Tests;

/// <summary>
/// Registering senders on the outbox: the publisher a service injects, and what is refused.
/// </summary>
public sealed class MessageOutboxServiceCollectionExtensionsTests : IAsyncDisposable
{
    private readonly OutboxTestHost _outbox = new();

    public ValueTask DisposeAsync() => _outbox.DisposeAsync();

    [Fact]
    public async Task TheInjectedPublisherIsTheOutbox()
    {
        var host = await _outbox.BuildAsync();
        await using var scope = host.Services.CreateAsyncScope();

        Assert.StartsWith("OutboxPublisher", Sender<OrderPlacedPayload, OrderPlaced>(scope.ServiceProvider, Topic).GetType().Name);
    }

    [Fact]
    public void AMessageTypeAlreadySentStraightToTheBrokerIsRefused()
    {
        var services = new ServiceCollection();
        services.AddPublisher<OrderPlacedPayload, OrderPlaced>(Topic);

        Assert.Throws<InvalidOperationException>(() =>
            services.AddOutboxPublisher<OrderPlacedPayload, OrderPlaced, ShopContext>(Topic));
    }

    // One service sending to its own command queue (MessageBroker's ClaimTopicFamily allows a sender
    // and a consumer of the same family in one service when the payload type is the same).

    [Fact(Timeout = 60 * 1000)]
    public async Task ACommandTheServiceSendsItselfIsReceivedByIt()
    {
        var host = await _outbox.BuildAsync(start: true);

        await SendAsync<SendReceiptPayload, SendReceipt>(host, new SendReceipt(21), "self-21");

        Assert.Equal((SendReceiptPayload.WireName, 21, "self-21"), await host.Services.GetRequiredService<Received>().WaitForAsync(21));
    }

    [Fact]
    public void AnotherPayloadUnderTheSameNameOnTheServicesOwnQueueIsRefused()
    {
        var services = new ServiceCollection();
        services.AddOutboxCommandSender<SendReceiptPayload, SendReceipt, ShopContext>(Queue);

        Assert.Throws<InvalidOperationException>(() =>
            services.AddCommandConsumer<ImpostorReceiptPayload, ImpostorReceipt, ImpostorReceiptConsumer>(Queue));
    }
}
