using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using static Bitwarden.Server.Sdk.MessageBroker.Sender.Tests.OutboxTestHost;

namespace Bitwarden.Server.Sdk.MessageBroker.Sender.Tests;

/// <summary>
/// The keyed publisher a service injects: sending adds to the outbox, and only the save commits it.
/// </summary>
public sealed class OutboxPublisherTests : IAsyncDisposable
{
    private readonly OutboxTestHost _outbox = new();

    public ValueTask DisposeAsync() => _outbox.DisposeAsync();

    [Fact]
    public async Task SendingAddsTheMessageAndSavingCommitsIt()
    {
        var host = await _outbox.BuildAsync();
        using var listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);
        using var source = new ActivitySource("test");
        using var request = source.StartActivity("request");

        await SendOrderAsync(host, 1, "message-1");

        var row = Assert.Single(await RowsAsync(host));
        Assert.Equal(("message-1", Topic, OrderPlacedPayload.WireName), (row.MessageId, row.Destination, row.MessageType));
        Assert.NotEmpty(row.Body);
        Assert.Equal(request!.Id, row.TraceParent);
        Assert.Null(row.SetAsideAt);
    }

    [Fact]
    public async Task ACancelledSendAddsNothing()
    {
        var host = await _outbox.BuildAsync();
        await using (var scope = host.Services.CreateAsyncScope())
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Sender<OrderPlacedPayload, OrderPlaced>(scope.ServiceProvider, Topic)
                .Publish(new OrderPlaced(1)).SendAsync(new CancellationToken(canceled: true)));
            await scope.ServiceProvider.GetRequiredService<ShopContext>().SaveChangesAsync(Token);
        }

        Assert.Empty(await RowsAsync(host));
    }

    [Fact]
    public async Task SendingWithoutSavingWritesNothing()
    {
        var host = await _outbox.BuildAsync();
        await using (var scope = host.Services.CreateAsyncScope())
        {
            await Sender<OrderPlacedPayload, OrderPlaced>(scope.ServiceProvider, Topic).Publish(new OrderPlaced(1))
                .SendAsync(Token);
        }

        Assert.Empty(await RowsAsync(host));
    }
}
