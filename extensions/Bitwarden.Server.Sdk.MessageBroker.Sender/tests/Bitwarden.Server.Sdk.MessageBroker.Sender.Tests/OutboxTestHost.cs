using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Bitwarden.Server.Sdk.MessageBroker.Sender.Tests;

/// <summary>
/// Instances of a service with an outbox, all on one SQLite database in memory and the in-memory
/// broker unless the settings name another: events to <see cref="Topic"/>, commands to
/// <see cref="Queue"/>, and <see cref="ReserveStock"/> handled in the service itself. Each test
/// class owns one, and the hosts it built are stopped with it.
/// </summary>
internal sealed class OutboxTestHost : IAsyncDisposable
{
    public const string Topic = "shop.events";
    public const string Queue = "mail.commands";

    private readonly List<IHost> _hosts = [];

    public static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>
    /// The database every instance shares.
    /// </summary>
    public SqliteConnection Connection { get; } = new("DataSource=:memory:");

    /// <summary>
    /// Builds an instance and creates the database's tables. Its dispatcher polls every 50 ms
    /// once started; settings under <c>MessageOutbox</c> bind <see cref="MessageOutboxOptions"/>,
    /// and the rest <see cref="MessagingOptions"/>.
    /// </summary>
    public async Task<IHost> BuildAsync(
        Action<IServiceCollection>? configure = null,
        bool start = false,
        Dictionary<string, string?>? settings = null)
    {
        if (Connection.State != System.Data.ConnectionState.Open)
            await Connection.OpenAsync(Token);

        var host = new HostBuilder()
            .ConfigureAppConfiguration(c => c.AddInMemoryCollection(settings ?? []))
            .ConfigureServices((context, services) =>
            {
                services.AddDbContext<ShopContext>(o => o.UseSqlite(Connection));
                services.AddSingleton<Received>();
                services.AddSingleton<StockReservations>();
                services.AddOutboxPublisher<OrderPlacedPayload, OrderPlaced, ShopContext>(Topic);
                services.AddOutboxCommandSender<SendReceiptPayload, SendReceipt, ShopContext>(Queue);
                services.AddOutboxLocalHandler<ReserveStockPayload, ReserveStock, ShopContext, ReserveStockHandler>();
                services.AddMessageConsumer<OrderPlacedPayload, OrderPlaced, OrderPlacedConsumer>(Topic, "billing");
                services.AddCommandConsumer<SendReceiptPayload, SendReceipt, SendReceiptConsumer>(Queue);
                services.AddOptions<MessageOutboxOptions>().Bind(context.Configuration.GetSection("MessageOutbox"));
                services.Configure<MessageOutboxOptions>(o => o.PollInterval = TimeSpan.FromMilliseconds(50));
                services.AddOptions<MessagingOptions>().BindConfiguration("");
                configure?.Invoke(services);
            })
            .Build();
        _hosts.Add(host);
        await using (var scope = host.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<ShopContext>().Database.EnsureCreatedAsync(Token);
        if (start)
            await host.StartAsync(Token);
        return host;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var host in _hosts)
        {
            await host.StopAsync();
            host.Dispose();
        }
        await Connection.DisposeAsync();
    }

    public static OutboxDispatcher<ShopContext> Dispatcher(IHost host) =>
        host.Services.GetServices<IHostedService>().OfType<OutboxDispatcher<ShopContext>>().Single();

    /// <summary>
    /// The keyed publisher a service injects for <paramref name="destination"/>.
    /// </summary>
    public static Publisher<TPayload, TCeiling> Sender<TPayload, TCeiling>(IServiceProvider scope, string destination)
        where TPayload : PayloadCeiling<TPayload, TCeiling>, IPayloadVariants<TPayload>, IPayloadWireName<TPayload>
        where TCeiling : Payload<TPayload>.ICeiling
        => scope.GetRequiredKeyedService<Publisher<TPayload, TCeiling>>(MessageBrokerKeys.Publisher<TPayload>(destination));

    /// <summary>
    /// The outbox's rows, by message identifier.
    /// </summary>
    public static async Task<List<OutboxMessage>> RowsAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ShopContext>().Set<OutboxMessage>().AsNoTracking()
            .OrderBy(row => row.MessageId).ToListAsync(Token);
    }

    /// <summary>
    /// Places an order and publishes it with the keyed publisher, in one save.
    /// </summary>
    public static async Task SendOrderAsync(IHost host, int orderId, string? messageId = null)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ShopContext>();
        database.Orders.Add(new Order { Id = orderId });
        var send = Sender<OrderPlacedPayload, OrderPlaced>(scope.ServiceProvider, Topic).Publish(new OrderPlaced(orderId));
        await (messageId is null ? send.SendAsync(Token) : send.SendAsync(messageId, Token));
        await database.SaveChangesAsync(Token);
    }

    /// <summary>
    /// Sends a <see cref="ReserveStock"/> to the service itself and saves.
    /// </summary>
    public static async Task ReserveStockAsync(IHost host, int orderId, string messageId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        await Sender<ReserveStockPayload, ReserveStock>(scope.ServiceProvider, MessageOutbox.Local).Publish(new ReserveStock(orderId))
            .SendAsync(messageId, Token);
        await scope.ServiceProvider.GetRequiredService<ShopContext>().SaveChangesAsync(Token);
    }

    /// <summary>
    /// Sends <paramref name="message"/> with <see cref="IOutboxSender"/> and saves.
    /// </summary>
    public static async Task<string> SendAsync<TPayload, TCeiling>(
        IHost host, TCeiling message, string? messageId = null, DateTimeOffset? notBefore = null)
        where TPayload : PayloadCeiling<TPayload, TCeiling>, IPayloadVariants<TPayload>, IPayloadWireName<TPayload>
        where TCeiling : Payload<TPayload>.ICeiling, Payload<TPayload>.IReachesFloor
    {
        await using var scope = host.Services.CreateAsyncScope();
        var id = await scope.ServiceProvider.GetRequiredService<IOutboxSender>()
            .SendAsync<TPayload, TCeiling>(message, messageId, notBefore, Token);
        await scope.ServiceProvider.GetRequiredService<ShopContext>().SaveChangesAsync(Token);
        return id;
    }

    public static async Task InsertAsync(IHost host, OutboxMessage row)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ShopContext>();
        database.Set<OutboxMessage>().Add(row);
        await database.SaveChangesAsync(Token);
    }

    public static async Task UpdateAsync(IHost host, Action<Microsoft.EntityFrameworkCore.Query.UpdateSettersBuilder<OutboxMessage>> set)
    {
        await using var scope = host.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ShopContext>().Set<OutboxMessage>().ExecuteUpdateAsync(set, Token);
    }

    public static Task MakeDueAsync(IHost host) => UpdateAsync(host, set => set.SetProperty(r => r.AvailableAt, 0L));

    public static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!await condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "The condition never held.");
            await Task.Delay(50, Token);
        }
    }
}
