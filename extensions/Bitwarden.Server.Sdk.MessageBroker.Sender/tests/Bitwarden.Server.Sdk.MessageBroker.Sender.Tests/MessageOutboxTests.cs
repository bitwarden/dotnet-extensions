using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using static Bitwarden.Server.Sdk.MessageBroker.Sender.Tests.OutboxTestHost;

namespace Bitwarden.Server.Sdk.MessageBroker.Sender.Tests;

public sealed class MessageOutboxTests : IAsyncDisposable
{
    private readonly OutboxTestHost _outbox = new();

    public ValueTask DisposeAsync() => _outbox.DisposeAsync();

    [Fact]
    public async Task AddingToAContextWritesTheRowItsPublisherWould()
    {
        var host = await _outbox.BuildAsync();
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ShopContext>();
            Assert.Equal("added-1", MessageOutbox.Add<OrderPlacedPayload, OrderPlaced>(database, Topic, new OrderPlaced(1), "added-1"));
            await Sender<OrderPlacedPayload, OrderPlaced>(scope.ServiceProvider, Topic)
                .Publish(new OrderPlaced(1)).SendAsync("published-1", Token);
            await database.SaveChangesAsync(Token);
        }

        var rows = await RowsAsync(host);
        Assert.Equal(2, rows.Count);
        Assert.Equal(rows[1].Body, rows[0].Body);
        Assert.All(rows, row => Assert.Equal((Topic, OrderPlacedPayload.WireName), (row.Destination, row.MessageType)));
    }

    [Fact]
    public async Task AddingFromAnInterceptorCommitsWithTheSave()
    {
        var host = await _outbox.BuildAsync(services => services.ConfigureDbContext<ShopContext>(o => o.AddInterceptors(new ReserveOnSave())));

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ShopContext>();
            database.Orders.Add(new Order { Id = 8 });
            await database.SaveChangesAsync(Token);
        }

        Assert.Equal(1, await Dispatcher(host).DispatchAsync(Token));
        Assert.Equal(8, Assert.Single(host.Services.GetRequiredService<StockReservations>().Handled).OrderId);
    }

    private sealed class ReserveOnSave : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            foreach (var order in eventData.Context!.ChangeTracker.Entries<Order>().Where(e => e.State == EntityState.Added).ToList())
            {
                MessageOutbox.Add<ReserveStockPayload, ReserveStock>(eventData.Context, MessageOutbox.Local, new ReserveStock(order.Entity.Id));
            }

            return ValueTask.FromResult(result);
        }
    }

    [Fact]
    public async Task AddingATypeNotRegisteredToTheDestinationIsRefused()
    {
        var host = await _outbox.BuildAsync();
        await using var scope = host.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ShopContext>();

        Assert.Throws<InvalidOperationException>(() => MessageOutbox.Add<OrderPlacedPayload, OrderPlaced>(database, "other.events", new OrderPlaced(1)));
        Assert.Throws<InvalidOperationException>(() => MessageOutbox.Add<RefundIssuedPayload, RefundIssued>(database, Topic, new RefundIssued(1)));
    }

    [Fact]
    public void AddingToAContextTheContainerDidNotMakeIsRefused()
    {
        using var database = new ShopContext(new DbContextOptionsBuilder<ShopContext>().UseSqlite(_outbox.Connection).Options);

        Assert.Throws<InvalidOperationException>(() => MessageOutbox.Add<OrderPlacedPayload, OrderPlaced>(database, Topic, new OrderPlaced(1)));
    }
}
