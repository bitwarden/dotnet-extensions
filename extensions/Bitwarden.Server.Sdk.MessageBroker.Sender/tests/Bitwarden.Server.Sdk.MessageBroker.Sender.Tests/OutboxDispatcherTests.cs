using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Bitwarden.Server.Sdk.MessageBroker.Sender.Tests.OutboxTestHost;

namespace Bitwarden.Server.Sdk.MessageBroker.Sender.Tests;

/// <summary>
/// The dispatcher on SQLite in memory and the in-memory broker: claiming, sending, deleting and
/// deferring. Its batch is driven directly where a test needs to control it, and by a running host
/// end to end. The same claims on each server engine are in <see cref="OutboxEngineTests"/>.
/// </summary>
public sealed class OutboxDispatcherTests : IAsyncDisposable
{
    private readonly OutboxTestHost _outbox = new();

    public ValueTask DisposeAsync() => _outbox.DisposeAsync();

    [Fact(Timeout = 60 * 1000)]
    public async Task AnEventIsDeliveredUnderItsIdentifierAndTheRowDeleted()
    {
        var host = await _outbox.BuildAsync(start: true);

        await SendOrderAsync(host, 7, "message-7");

        var received = await host.Services.GetRequiredService<Received>().WaitForAsync(7);
        Assert.Equal((OrderPlacedPayload.WireName, "message-7"), (received.Type, received.MessageId));
        await WaitUntilAsync(async () => (await RowsAsync(host)).Count == 0);
    }

    [Fact(Timeout = 60 * 1000)]
    public async Task WithDispatchOffNothingIsSentAndTheMessageWaits()
    {
        var host = await _outbox.BuildAsync(services => services.Configure<MessageOutboxOptions>(o => o.Dispatch = false), start: true);

        await SendOrderAsync(host, 8, "message-8");
        await Task.Delay(TimeSpan.FromMilliseconds(500), Token);

        Assert.True(Dispatcher(host).ExecuteTask?.IsCompletedSuccessfully);
        Assert.Equal("message-8", Assert.Single(await RowsAsync(host)).MessageId);
        Assert.DoesNotContain(host.Services.GetRequiredService<Received>().Messages, message => message.OrderId == 8);
    }

    [Fact]
    public async Task ABrokerThatIsUnavailableIsRetriedAndNeverSetAside()
    {
        // A port nothing listens on: every send fails with BrokerUnavailableException.
        var host = await _outbox.BuildAsync(settings: new() { ["RabbitUri"] = "amqp://guest:guest@127.0.0.1:1/" });
        await SendOrderAsync(host, 3);

        await Dispatcher(host).DispatchAsync(Token);

        var row = Assert.Single(await RowsAsync(host));
        Assert.Equal(1, row.Attempts);
        Assert.Null(row.SetAsideAt);
        Assert.Equal(typeof(BrokerUnavailableException).FullName, row.LastError);
        Assert.True(row.AvailableAt > DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        Assert.Null(row.LeaseToken);
    }

    [Fact]
    public async Task AFailureOtherThanTheBrokerIsRetriedAndNeverSetAside()
    {
        // Credentials that expired or a permission not granted yet also clear in time: a committed
        // message is retried for as long as it takes.
        var host = await _outbox.BuildAsync(services => services.AddSingleton(sp => new OutboxRegistration(typeof(ShopContext), new FailingRoute())));
        await InsertAsync(host, new OutboxMessage
        {
            MessageId = "failing",
            Destination = FailingRoute.Name,
            MessageType = FailingRoute.Name,
            Body = [],
        });

        for (var attempt = 1; attempt <= 15; attempt++)
        {
            await MakeDueAsync(host);
            await Dispatcher(host).DispatchAsync(Token);
        }

        var row = Assert.Single(await RowsAsync(host));
        Assert.Equal(15, row.Attempts);
        Assert.Null(row.SetAsideAt);
        Assert.Equal(typeof(UnauthorizedAccessException).FullName, row.LastError);
    }

    private sealed class FailingRoute : OutboxRoute
    {
        public const string Name = "failing";
        public override string Destination => Name;
        public override string MessageType => Name;

        public override Task SendAsync(OutboxMessage message, CancellationToken cancellationToken) =>
            throw new UnauthorizedAccessException("The credential has expired.");
    }

    [Fact]
    public async Task AMessageTypeThisBuildCannotSendIsLeftForOneThatCan()
    {
        // As during a rolling deploy: a newer build wrote a type this one doesn't send.
        var host = await _outbox.BuildAsync();
        await InsertAsync(host, new OutboxMessage
        {
            MessageId = "newer",
            Destination = Topic,
            MessageType = "order.refunded",
            Body = "[]"u8.ToArray(),
        });

        Assert.Equal(0, await Dispatcher(host).DispatchAsync(Token));

        var row = Assert.Single(await RowsAsync(host));
        Assert.Equal((0, (long?)null, (Guid?)null), (row.Attempts, row.SetAsideAt, row.LeaseToken));
    }

    [Fact]
    public async Task AMessageAnotherInstanceHoldsIsLeftAlone()
    {
        var host = await _outbox.BuildAsync();
        await SendOrderAsync(host, 4);
        var other = Guid.NewGuid();
        await UpdateAsync(host, set => set
            .SetProperty(r => r.LeaseToken, other)
            .SetProperty(r => r.LeaseUntil, DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeMilliseconds()));

        Assert.Equal(0, await Dispatcher(host).DispatchAsync(Token));
        Assert.Equal(other, Assert.Single(await RowsAsync(host)).LeaseToken);
    }

    [Fact(Timeout = 60 * 1000)]
    public async Task AClaimThatExpiredIsTakenOver()
    {
        var host = await _outbox.BuildAsync();
        await SendOrderAsync(host, 5);
        await UpdateAsync(host, set => set
            .SetProperty(r => r.LeaseToken, Guid.NewGuid())
            .SetProperty(r => r.LeaseUntil, DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeMilliseconds()));

        Assert.Equal(1, await Dispatcher(host).DispatchAsync(Token));
        Assert.Empty(await RowsAsync(host));
    }

    [Fact]
    public async Task ASendThatOutlastsItsClaimDoesNotDeleteTheRowAnotherInstanceTookOver()
    {
        // The route stands in for a send slower than the lease: while it runs, another instance
        // takes the row over.
        var host = await _outbox.BuildAsync(services => services.AddSingleton(sp => new OutboxRegistration(typeof(ShopContext), new TakenOverRoute(sp))));
        await InsertAsync(host, new OutboxMessage
        {
            MessageId = "slow",
            Destination = TakenOverRoute.Name,
            MessageType = TakenOverRoute.Name,
            Body = [],
        });

        await Dispatcher(host).DispatchAsync(Token);

        var row = Assert.Single(await RowsAsync(host));
        Assert.Equal(TakenOverRoute.OtherInstance, row.LeaseToken);
    }

    private sealed class TakenOverRoute(IServiceProvider services) : OutboxRoute
    {
        public const string Name = "taken.over";
        public static readonly Guid OtherInstance = Guid.NewGuid();

        public override string Destination => Name;
        public override string MessageType => Name;

        public override async Task SendAsync(OutboxMessage message, CancellationToken cancellationToken)
        {
            await using var scope = services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<ShopContext>().Set<OutboxMessage>()
                .Where(r => r.MessageId == message.MessageId)
                .ExecuteUpdateAsync(set => set.SetProperty(r => r.LeaseToken, OtherInstance), cancellationToken);
        }
    }

    // The seam a test harness watches the broker through (IOutboxSendObserver)

    [Fact]
    public async Task TheSeamSeesEveryMessageForTheBrokerAndNoneHandledLocally()
    {
        var watched = new Watched();
        var host = await _outbox.BuildAsync(services => services.AddSingleton<IOutboxSendObserver>(watched));
        await SendAsync<OrderPlacedPayload, OrderPlaced>(host, new OrderPlaced(1), "placed-1");
        await SendAsync<SendReceiptPayload, SendReceipt>(host, new SendReceipt(1), "receipt-1");
        await SendAsync<ReserveStockPayload, ReserveStock>(host, new ReserveStock(1), "reserve-1");

        Assert.Equal(3, await Dispatcher(host).DispatchAsync(Token));

        Assert.Equal(["placed-1", "receipt-1"], watched.Sending.Order());
        Assert.Equal(["placed-1", "receipt-1"], watched.Sent.Order());
        Assert.Empty(await RowsAsync(host));
    }

    [Fact]
    public async Task ASeamThatRefusesLeavesTheMessageToBeTriedAgain()
    {
        var watched = new Watched { Unavailable = new BrokerUnavailableException("Down, for the test.", new TimeoutException()) };
        var host = await _outbox.BuildAsync(services => services.AddSingleton<IOutboxSendObserver>(watched));
        await SendAsync<OrderPlacedPayload, OrderPlaced>(host, new OrderPlaced(1), "placed-1");

        await Dispatcher(host).DispatchAsync(Token);

        var row = Assert.Single(await RowsAsync(host));
        Assert.Equal((1, (long?)null), (row.Attempts, row.SetAsideAt));
        Assert.Empty(watched.Sent);
    }

    private sealed class Watched : IOutboxSendObserver
    {
        public Exception? Unavailable { get; init; }

        public ConcurrentQueue<string> Sending { get; } = new();

        public ConcurrentQueue<string> Sent { get; } = new();

        void IOutboxSendObserver.Sending(OutboxMessage message)
        {
            Sending.Enqueue(message.MessageId);
            if (Unavailable is not null)
                throw Unavailable;
        }

        void IOutboxSendObserver.Sent(OutboxMessage message) => Sent.Enqueue(message.MessageId);
    }
}
