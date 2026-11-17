using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Bitwarden.Server.Sdk.MessageBroker.Sender.Tests.OutboxTestHost;

namespace Bitwarden.Server.Sdk.MessageBroker.Sender.Tests;

public sealed class OutboxSendingExtensionsTests : IAsyncDisposable
{
    private readonly OutboxTestHost _outbox = new();

    public ValueTask DisposeAsync() => _outbox.DisposeAsync();

    [Fact]
    public async Task AMessageSentForLaterWaitsUntilItIsDue()
    {
        var host = await _outbox.BuildAsync();
        var notBefore = DateTimeOffset.UtcNow.AddHours(1);
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var id = await Sender<OrderPlacedPayload, OrderPlaced>(scope.ServiceProvider, Topic).Publish(new OrderPlaced(1))
                .SendLaterAsync("later-1", notBefore, Token);
            Assert.Equal("later-1", id);
            await scope.ServiceProvider.GetRequiredService<ShopContext>().SaveChangesAsync(Token);
        }

        Assert.Equal(0, await Dispatcher(host).DispatchAsync(Token));
        Assert.Equal(notBefore.ToUnixTimeMilliseconds(), Assert.Single(await RowsAsync(host)).AvailableAt);

        await MakeDueAsync(host);
        Assert.Equal(1, await Dispatcher(host).DispatchAsync(Token));
        Assert.Empty(await RowsAsync(host));
    }

    [Fact]
    public async Task APublisherOutsideTheOutboxCannotSendLater()
    {
        const string Other = "other.events";
        var host = await _outbox.BuildAsync(s => s.AddPublisher<OrderPlacedPayload, OrderPlaced>(Other));
        var direct = Sender<OrderPlacedPayload, OrderPlaced>(host.Services, Other);

        await Assert.ThrowsAsync<InvalidOperationException>(() => direct.Publish(new OrderPlaced(1))
            .SendLaterAsync(DateTimeOffset.UtcNow.AddMinutes(1), Token));
    }

    [Fact]
    public async Task RetractingASavedMessageDeletesIt()
    {
        var host = await _outbox.BuildAsync();
        string id;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            id = await Sender<OrderPlacedPayload, OrderPlaced>(scope.ServiceProvider, Topic).Publish(new OrderPlaced(1))
                .SendLaterAsync(DateTimeOffset.UtcNow.AddMinutes(1), Token);
            await scope.ServiceProvider.GetRequiredService<ShopContext>().SaveChangesAsync(Token);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ShopContext>();
            Assert.True(await database.RetractMessageAsync(id, Token));
            Assert.False(await database.RetractMessageAsync(id, Token));
        }
        Assert.Empty(await RowsAsync(host));
    }

    [Fact]
    public async Task RetractingAMessageNotYetSavedDropsItAndKeepsTheChange()
    {
        var host = await _outbox.BuildAsync();
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ShopContext>();
            database.Orders.Add(new Order { Id = 1 });
            await Sender<OrderPlacedPayload, OrderPlaced>(scope.ServiceProvider, Topic).Publish(new OrderPlaced(1))
                .SendAsync("message-1", Token);

            Assert.True(await database.RetractMessageAsync("message-1", Token));
            await database.SaveChangesAsync(Token);
        }

        Assert.Empty(await RowsAsync(host));
        await using var check = host.Services.CreateAsyncScope();
        Assert.Single(await check.ServiceProvider.GetRequiredService<ShopContext>().Orders.ToListAsync(Token));
    }

    [Fact]
    public async Task RetractingBySubjectTakesBackItsEarlierMessagesSavedOrNot()
    {
        var host = await _outbox.BuildAsync();
        await SendAsync<ReserveStockPayload, ReserveStock>(host, new ReserveStock(1), "device-a.1");
        await SendAsync<ReserveStockPayload, ReserveStock>(host, new ReserveStock(2), "device-a.2");
        await SendAsync<ReserveStockPayload, ReserveStock>(host, new ReserveStock(3), "device-ab.1");
        await SendAsync<OrderPlacedPayload, OrderPlaced>(host, new OrderPlaced(4), "device-a.order");

        int retracted;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ShopContext>();
            var latest = await scope.ServiceProvider.GetRequiredService<IOutboxSender>()
                .SendAsync<ReserveStockPayload, ReserveStock>(new ReserveStock(5), "device-a.3", cancellationToken: Token);
            await scope.ServiceProvider.GetRequiredService<IOutboxSender>()
                .SendAsync<ReserveStockPayload, ReserveStock>(new ReserveStock(6), "device-a.unsaved", cancellationToken: Token);

            retracted = await database.RetractMessagesAsync(ReserveStockPayload.WireName, "device-a.", except: latest, Token);
            await database.SaveChangesAsync(Token);
        }

        Assert.Equal(3, retracted);
        Assert.Equal(["device-a.3", "device-a.order", "device-ab.1"], (await RowsAsync(host)).Select(row => row.MessageId));
    }

    [Fact]
    public async Task RetractingBySubjectWithNoneToKeepTakesBackEveryOne()
    {
        var host = await _outbox.BuildAsync();
        await SendAsync<ReserveStockPayload, ReserveStock>(host, new ReserveStock(1), "device-a.1");
        await SendAsync<ReserveStockPayload, ReserveStock>(host, new ReserveStock(2), "device-a.2");

        await using (var scope = host.Services.CreateAsyncScope())
        {
            Assert.Equal(2, await scope.ServiceProvider.GetRequiredService<ShopContext>()
                .RetractMessagesAsync(ReserveStockPayload.WireName, "device-a.", cancellationToken: Token));
        }

        Assert.Empty(await RowsAsync(host));
    }
}
