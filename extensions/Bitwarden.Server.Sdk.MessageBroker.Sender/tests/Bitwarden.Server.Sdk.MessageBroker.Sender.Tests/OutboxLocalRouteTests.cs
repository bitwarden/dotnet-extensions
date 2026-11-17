using Microsoft.Extensions.DependencyInjection;
using static Bitwarden.Server.Sdk.MessageBroker.Sender.Tests.OutboxTestHost;

namespace Bitwarden.Server.Sdk.MessageBroker.Sender.Tests;

public sealed class OutboxLocalRouteTests : IAsyncDisposable
{
    private readonly OutboxTestHost _outbox = new();

    public ValueTask DisposeAsync() => _outbox.DisposeAsync();

    private async Task<(Microsoft.Extensions.Hosting.IHost Host, StockReservations Reservations)> BuildLocalHostAsync()
    {
        var host = await _outbox.BuildAsync();
        return (host, host.Services.GetRequiredService<StockReservations>());
    }

    [Fact]
    public async Task ALocalMessageIsHandledInTheServiceAndItsRowDeleted()
    {
        var (host, reservations) = await BuildLocalHostAsync();
        await ReserveStockAsync(host, 3, "reserve-3");
        Assert.Equal(MessageOutbox.Local, Assert.Single(await RowsAsync(host)).Destination);

        await Dispatcher(host).DispatchAsync(Token);

        Assert.Equal((3, "reserve-3", 1), Assert.Single(reservations.Handled));
        Assert.Empty(await RowsAsync(host));
    }

    [Fact]
    public async Task ALocalHandlerThatThrowsIsTriedAgainLater()
    {
        var (host, reservations) = await BuildLocalHostAsync();
        reservations.Next = _ => throw new TimeoutException();
        await ReserveStockAsync(host, 3, "reserve-3");

        await Dispatcher(host).DispatchAsync(Token);
        var row = Assert.Single(await RowsAsync(host));
        Assert.Equal((1, typeof(TimeoutException).FullName, null), (row.Attempts, row.LastError, row.SetAsideAt));
        Assert.True(row.AvailableAt > DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        reservations.Next = null;
        await MakeDueAsync(host);
        await Dispatcher(host).DispatchAsync(Token);
        Assert.Equal(2, reservations.Handled.Last().DeliveryCount);
        Assert.Empty(await RowsAsync(host));
    }

    [Fact]
    public async Task ALocalHandlerThatRequeuesIsTriedAgainLater()
    {
        var (host, reservations) = await BuildLocalHostAsync();
        reservations.Next = envelope => envelope.RequeueAsync(cancellationToken: Token);
        await ReserveStockAsync(host, 3, "reserve-3");

        await Dispatcher(host).DispatchAsync(Token);

        var row = Assert.Single(await RowsAsync(host));
        Assert.Equal((1, null), (row.Attempts, row.SetAsideAt));
    }

    [Fact]
    public async Task ALocalHandlerThatDeadLettersSetsTheMessageAside()
    {
        var (host, reservations) = await BuildLocalHostAsync();
        reservations.Next = envelope => envelope.DeadLetterAsync("out of stock", Token);
        await ReserveStockAsync(host, 3, "reserve-3");

        await Dispatcher(host).DispatchAsync(Token);

        Assert.NotNull(Assert.Single(await RowsAsync(host)).SetAsideAt);
    }

    [Fact]
    public async Task ALocalHandlerThatDeadLettersAndThenThrowsStillSetsTheMessageAside()
    {
        var (host, reservations) = await BuildLocalHostAsync();
        reservations.Next = async envelope =>
        {
            await envelope.DeadLetterAsync("out of stock", Token);
            throw new InvalidOperationException();
        };
        await ReserveStockAsync(host, 3, "reserve-3");

        await Dispatcher(host).DispatchAsync(Token);

        Assert.NotNull(Assert.Single(await RowsAsync(host)).SetAsideAt);
    }

    [Fact]
    public async Task ALocalHandlerThatCompletesAndThenThrowsIsDone()
    {
        var (host, reservations) = await BuildLocalHostAsync();
        reservations.Next = async envelope =>
        {
            await envelope.CompleteAsync(Token);
            throw new InvalidOperationException();
        };
        await ReserveStockAsync(host, 3, "reserve-3");

        await Dispatcher(host).DispatchAsync(Token);

        Assert.Empty(await RowsAsync(host));
    }

    [Fact]
    public async Task ALocalMessageThatCannotBeReadIsSetAsideUnhandled()
    {
        var (host, reservations) = await BuildLocalHostAsync();
        await InsertAsync(host, new OutboxMessage
        {
            MessageId = "unreadable",
            Destination = MessageOutbox.Local,
            MessageType = ReserveStockPayload.WireName,
            Body = "{"u8.ToArray(),
        });

        await Dispatcher(host).DispatchAsync(Token);

        Assert.Empty(reservations.Handled);
        Assert.NotNull(Assert.Single(await RowsAsync(host)).SetAsideAt);
    }
}
