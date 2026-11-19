using System.Collections.Concurrent;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using static Bitwarden.Server.Sdk.MessageBroker.Sender.Tests.OutboxTestHost;

namespace Bitwarden.Server.Sdk.MessageBroker.Sender.Tests;

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

    [Fact(Timeout = 60 * 1000)]
    public async Task RowsOfAnUnregisteredPairingAreNotClaimedAndDoNotKeepTheDispatcherPolling()
    {
        // Destination and type each registered, but not together: a full batch of them mustn't hold
        // back the routable row.
        var commands = new CommandCounter();
        var host = await _outbox.BuildAsync(services =>
        {
            services.Configure<MessageOutboxOptions>(o => o.BatchSize = 5);
            services.PostConfigure<MessageOutboxOptions>(o => o.PollInterval = TimeSpan.FromMinutes(1));
            services.ConfigureDbContext<ShopContext>(o => o.AddInterceptors(commands));
        });
        for (var i = 0; i < 5; i++)
        {
            await InsertAsync(host, new OutboxMessage
            {
                MessageId = $"unpaired-{i}",
                Destination = Topic,
                MessageType = SendReceiptPayload.WireName,
                Body = "[]"u8.ToArray(),
            });
        }
        await SendOrderAsync(host, 12, "routable");

        await host.StartAsync(Token);
        await host.Services.GetRequiredService<Received>().WaitForAsync(12);
        await WaitUntilAsync(async () => (await RowsAsync(host)).All(row => row.MessageId != "routable"));
        await Task.Delay(300, Token);
        var settled = commands.Count;
        await Task.Delay(TimeSpan.FromSeconds(1), Token);

        Assert.Equal(settled, commands.Count);
        var rows = await RowsAsync(host);
        Assert.Equal(5, rows.Count);
        Assert.All(rows, row => Assert.Equal((0, (Guid?)null, 0L), (row.Attempts, row.LeaseToken, row.AvailableAt)));
    }

    private sealed class CommandCounter : DbCommandInterceptor
    {
        private int _count;

        public int Count => Volatile.Read(ref _count);

        public override DbCommand CommandCreated(CommandEndEventData eventData, DbCommand result)
        {
            Interlocked.Increment(ref _count);
            return result;
        }
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

    [Fact]
    public async Task AStopDuringABatchStillDeletesWhatWasSentAndGivesBackTheRest()
    {
        // The host stops (the dispatcher's token is cancelled) once the first row has gone. That row
        // must not be left to be sent again; the others go back unclaimed and untried.
        using var stopping = new CancellationTokenSource();
        var host = await _outbox.BuildAsync(services => services.AddSingleton(sp => new OutboxRegistration(typeof(ShopContext), new StoppingRoute(stopping))));
        foreach (var id in new[] { "a", "b", "c" })
        {
            await InsertAsync(host, new OutboxMessage
            {
                MessageId = id,
                Destination = StoppingRoute.Name,
                MessageType = StoppingRoute.Name,
                Body = [],
            });
        }

        await Dispatcher(host).DispatchAsync(stopping.Token);

        var rows = await RowsAsync(host);
        Assert.Equal(["b", "c"], rows.Select(row => row.MessageId));
        Assert.All(rows, row => Assert.Equal((0, (Guid?)null, (long?)null), (row.Attempts, row.LeaseToken, row.SetAsideAt)));
    }

    private sealed class StoppingRoute(CancellationTokenSource stopping) : OutboxRoute
    {
        public const string Name = "stopping";
        public override string Destination => Name;
        public override string MessageType => Name;

        public override Task SendAsync(OutboxMessage message, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            stopping.Cancel();
            return Task.CompletedTask;
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
