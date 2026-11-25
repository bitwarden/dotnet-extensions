using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Bitwarden.Server.Sdk.MessageBroker.Sender.Tests;

public abstract class OutboxEngineTests(OutboxEngineFixture fixture) : IAsyncLifetime
{
    private const string Topic = "shop.events";

    private readonly List<IHost> _hosts = [];
    private readonly ConcurrentQueue<string> _sent = new();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await using var database = fixture.Database();
        await database.Set<OutboxMessage>().ExecuteDeleteAsync(Token);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var host in _hosts)
        {
            host.Dispose();
        }

        await Task.CompletedTask;
    }

    // An instance of the service: its own container, its own connections, the in-memory broker.
    private IHost Instance(Action<MessageOutboxOptions>? options = null)
    {
        var host = new HostBuilder()
            .ConfigureServices(services =>
            {
                services.AddDbContext<ShopContext>(fixture.Configure);
                services.AddSingleton<Received>();
                services.AddSingleton<IOutboxSendObserver>(new Recorder(_sent));
                services.AddOutboxPublisher<OrderPlacedPayload, OrderPlaced, ShopContext>(Topic);
                services.AddOutboxLocalHandler<ReserveStockPayload, ReserveStock, ShopContext, ReserveStockHandler>();
                services.AddSingleton<StockReservations>();
                services.Configure<MessageOutboxOptions>(o =>
                {
                    o.BatchSize = 10;
                    options?.Invoke(o);
                });
            })
            .Build();
        _hosts.Add(host);
        return host;
    }

    private static OutboxDispatcher<ShopContext> Dispatcher(IHost host) =>
        host.Services.GetServices<IHostedService>().OfType<OutboxDispatcher<ShopContext>>().Single();

    private async Task<List<OutboxMessage>> RowsAsync()
    {
        await using var database = fixture.Database();
        return await database.Set<OutboxMessage>().AsNoTracking().OrderBy(row => row.MessageId).ToListAsync(Token);
    }

    private static async Task SendAsync(IHost host, int count, string prefix = "order-", DateTimeOffset? notBefore = null)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var outbox = scope.ServiceProvider.GetRequiredService<IOutboxSender>();
        for (var i = 0; i < count; i++)
        {
            await outbox.SendAsync<OrderPlacedPayload, OrderPlaced>(new OrderPlaced(i), $"{prefix}{i:D3}", notBefore, Token);
        }

        await scope.ServiceProvider.GetRequiredService<ShopContext>().SaveChangesAsync(Token);
    }

    private async Task UpdateAsync(Action<Microsoft.EntityFrameworkCore.Query.UpdateSettersBuilder<OutboxMessage>> set)
    {
        await using var database = fixture.Database();
        await database.Set<OutboxMessage>().ExecuteUpdateAsync(set, Token);
    }

    [Fact]
    public async Task ASavedMessageIsSentAndItsRowDeleted()
    {
        var host = Instance();
        await SendAsync(host, 3);

        Assert.Equal(3, await Dispatcher(host).DispatchAsync(Token));

        Assert.Equal(["order-000", "order-001", "order-002"], _sent.Order());
        Assert.Empty(await RowsAsync());
    }

    [Fact]
    public async Task InstancesClaimingAtOnceSendEachMessageOnce()
    {
        var instances = Enumerable.Range(0, 4).Select(_ => Instance()).ToList();
        await SendAsync(instances[0], 40);

        // Each instance polls until the outbox is empty, all at once.
        await Task.WhenAll(instances.Select(instance => Task.Run(async () =>
        {
            while (await Dispatcher(instance).DispatchAsync(Token) > 0)
            {
            }
        }, Token)));

        Assert.Equal(Enumerable.Range(0, 40).Select(i => $"order-{i:D3}"), _sent.Order());
        Assert.Empty(await RowsAsync());
    }

    [Fact]
    public async Task AMessageAnotherInstanceHoldsIsLeftAlone()
    {
        var host = Instance();
        await SendAsync(host, 1);
        var other = Guid.NewGuid();
        await UpdateAsync(set => set
            .SetProperty(row => row.LeaseToken, other)
            .SetProperty(row => row.LeaseUntil, DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeMilliseconds()));

        Assert.Equal(0, await Dispatcher(host).DispatchAsync(Token));

        Assert.Equal(other, Assert.Single(await RowsAsync()).LeaseToken);
        Assert.Empty(_sent);
    }

    [Fact]
    public async Task AClaimThatExpiredIsTakenOver()
    {
        var host = Instance();
        await SendAsync(host, 1);
        await UpdateAsync(set => set
            .SetProperty(row => row.LeaseToken, Guid.NewGuid())
            .SetProperty(row => row.LeaseUntil, DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeMilliseconds()));

        Assert.Equal(1, await Dispatcher(host).DispatchAsync(Token));

        Assert.Empty(await RowsAsync());
    }

    [Fact]
    public async Task AFailedSendIsDeferredAndAHandlerThatDeadLettersIsSetAside()
    {
        var host = Instance();
        var reservations = host.Services.GetRequiredService<StockReservations>();
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var outbox = scope.ServiceProvider.GetRequiredService<IOutboxSender>();
            await outbox.SendAsync<ReserveStockPayload, ReserveStock>(new ReserveStock(1), "fails", cancellationToken: Token);
            await outbox.SendAsync<ReserveStockPayload, ReserveStock>(new ReserveStock(2), "refused", cancellationToken: Token);
            await scope.ServiceProvider.GetRequiredService<ShopContext>().SaveChangesAsync(Token);
        }
        reservations.Next = envelope => envelope.Payload.OrderId == 1
            ? throw new TimeoutException()
            : envelope.DeadLetterAsync("Refused, for the test.", Token);

        await Dispatcher(host).DispatchAsync(Token);

        var rows = await RowsAsync();
        Assert.Equal((1, typeof(TimeoutException).FullName, (long?)null, (Guid?)null), (rows[0].Attempts, rows[0].LastError, rows[0].SetAsideAt, rows[0].LeaseToken));
        Assert.True(rows[0].AvailableAt > DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        Assert.NotNull(rows[1].SetAsideAt);
        Assert.Null(rows[1].LeaseToken);
        Assert.Equal(0, await Dispatcher(host).DispatchAsync(Token));
    }

    [Fact]
    public async Task AMessageForLaterIsClaimedOnlyOnceDue()
    {
        var host = Instance();
        await SendAsync(host, 1, notBefore: DateTimeOffset.UtcNow.AddHours(1));

        Assert.Equal(0, await Dispatcher(host).DispatchAsync(Token));

        await UpdateAsync(set => set.SetProperty(row => row.AvailableAt, 0L));
        Assert.Equal(1, await Dispatcher(host).DispatchAsync(Token));
    }

    // LIKE's wildcards in an identifier are matched as themselves: "a_b." must not take back "axb.".
    [Fact]
    public async Task RetractingByPrefixMatchesThePrefixExactly()
    {
        var host = Instance();
        await SendAsync(host, 2, prefix: "a_b.");
        await SendAsync(host, 1, prefix: "axb.");
        await SendAsync(host, 1, prefix: "a%b.");

        await using (var scope = host.Services.CreateAsyncScope())
        {
            Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<ShopContext>()
                .RetractMessagesAsync(OrderPlacedPayload.WireName, "a_b.", except: "a_b.001", Token));
        }

        Assert.Equal(["a%b.000", "a_b.001", "axb.000"], (await RowsAsync()).Select(row => row.MessageId).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task TheBacklogCountsWhatIsDueFromWhenItWasDue()
    {
        var host = Instance();
        await SendAsync(host, 2);
        var due = DateTimeOffset.UtcNow.AddMinutes(-7).ToUnixTimeMilliseconds();
        await UpdateAsync(set => set.SetProperty(row => row.DueAt, due));
        await SendAsync(host, 1, prefix: "later-", notBefore: DateTimeOffset.UtcNow.AddHours(1));

        var sample = await host.Services.GetRequiredService<OutboxBacklog<ShopContext>>().SampleAsync(Token);

        Assert.Equal(2, sample.Due);
        Assert.InRange(sample.OldestLateness!.Value.TotalMinutes, 6.9, 8);
        Assert.Equal(0, sample.SetAside);
    }

    // Both are registered on their own: the topic with OrderPlaced, and the service's own handler
    // with ReserveStock. Paired the other way round, they are no route of this build's.
    [Fact]
    public async Task OnlyARegisteredPairingOfDestinationAndTypeIsClaimed()
    {
        var host = Instance();
        await using (var database = fixture.Database())
        {
            for (var i = 0; i < 10; i++)
            {
                database.Set<OutboxMessage>().Add(new OutboxMessage
                {
                    MessageId = $"unpaired-{i:D2}",
                    Destination = i % 2 == 0 ? Topic : MessageOutbox.Local,
                    MessageType = i % 2 == 0 ? ReserveStockPayload.WireName : OrderPlacedPayload.WireName,
                    Body = "[]"u8.ToArray(),
                });
            }
            await database.SaveChangesAsync(Token);
        }
        await SendAsync(host, 1);

        Assert.Equal(1, await Dispatcher(host).DispatchAsync(Token));

        Assert.Equal(["order-000"], _sent);
        var rows = await RowsAsync();
        Assert.Equal(10, rows.Count);
        Assert.All(rows, row => Assert.Equal((0, (Guid?)null, 0L), (row.Attempts, row.LeaseToken, row.AvailableAt)));
    }

    [Fact(Timeout = 120 * 1000)]
    public async Task ABatchThatOutlastsItsLeaseHandlesEachMessageOnce()
    {
        // Eight 600 ms handlers outlast the four-second lease: the second instance claims what the first
        // left, and nothing it is still handling.
        var instances = Enumerable.Range(0, 2).Select(_ => Instance(o => o.Lease = TimeSpan.FromSeconds(4))).ToList();
        foreach (var instance in instances)
        {
            instance.Services.GetRequiredService<StockReservations>().Next = _ => Task.Delay(600, Token);
        }

        await using (var scope = instances[0].Services.CreateAsyncScope())
        {
            var outbox = scope.ServiceProvider.GetRequiredService<IOutboxSender>();
            for (var i = 0; i < 8; i++)
            {
                await outbox.SendAsync<ReserveStockPayload, ReserveStock>(new ReserveStock(i), $"reserve-{i}", cancellationToken: Token);
            }

            await scope.ServiceProvider.GetRequiredService<ShopContext>().SaveChangesAsync(Token);
        }

        await Task.WhenAll(instances.Select(instance => Task.Run(async () =>
        {
            while ((await RowsAsync()).Count > 0)
            {
                if (await Dispatcher(instance).DispatchAsync(Token) == 0)
                {
                    await Task.Delay(100, Token);
                }
            }
        }, Token)));

        var handled = instances.SelectMany(instance => instance.Services.GetRequiredService<StockReservations>().Handled)
            .Select(each => each.OrderId).Order();
        Assert.Equal(Enumerable.Range(0, 8), handled);
    }

    private sealed class Recorder(ConcurrentQueue<string> sent) : IOutboxSendObserver
    {
        public void Sending(OutboxMessage message)
        {
        }

        public void Sent(OutboxMessage message) => sent.Enqueue(message.MessageId);
    }
}

public sealed class SqlServerOutboxTests(SqlServerOutboxFixture fixture)
    : OutboxEngineTests(fixture), IClassFixture<SqlServerOutboxFixture>;

public sealed class PostgreSqlOutboxTests(PostgreSqlOutboxFixture fixture)
    : OutboxEngineTests(fixture), IClassFixture<PostgreSqlOutboxFixture>;

public sealed class MySqlOutboxTests(MySqlOutboxFixture fixture)
    : OutboxEngineTests(fixture), IClassFixture<MySqlOutboxFixture>;

public sealed class MariaDbOutboxTests(MariaDbOutboxFixture fixture)
    : OutboxEngineTests(fixture), IClassFixture<MariaDbOutboxFixture>;
