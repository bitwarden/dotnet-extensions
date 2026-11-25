using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RabbitMQ.Client;

namespace Bitwarden.Server.Sdk.MessageBroker.Sender.Tests;

public sealed class BrokerDeliveryTests(AmqpBrokerFixture broker) : IClassFixture<AmqpBrokerFixture>, IAsyncLifetime
{
    private readonly string _topic = $"shop-{Guid.NewGuid():N}"[..13] + ".events";
    private readonly string _queue = $"mail-{Guid.NewGuid():N}"[..13] + ".commands";
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private IHost? _host;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private string Subscription => $"{_topic}.billing";

    public async ValueTask InitializeAsync() => await _connection.OpenAsync(Token);

    public async ValueTask DisposeAsync()
    {
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }
        await _connection.DisposeAsync();
    }

    [Fact(Timeout = 120 * 1000)]
    public async Task SavedMessagesAreSentThroughTheBrokerUnderTheirIdentifiers()
    {
        await DeclareTopologyAsync();
        _host = new HostBuilder()
            .ConfigureAppConfiguration(c => c.AddInMemoryCollection(new Dictionary<string, string?> { ["RabbitUri"] = broker.Uri }))
            .ConfigureServices(services =>
            {
                services.AddDbContext<ShopContext>(o => o.UseSqlite(_connection));
                services.AddSingleton<Received>();
                services.AddOutboxPublisher<OrderPlacedPayload, OrderPlaced, ShopContext>(_topic);
                services.AddOutboxCommandSender<SendReceiptPayload, SendReceipt, ShopContext>(_queue);
                services.AddMessageConsumer<OrderPlacedPayload, OrderPlaced, OrderPlacedConsumer>(_topic, "billing");
                services.AddCommandConsumer<SendReceiptPayload, SendReceipt, SendReceiptConsumer>(_queue);
                services.Configure<MessageOutboxOptions>(o => o.PollInterval = TimeSpan.FromMilliseconds(100));
                services.AddOptions<MessagingOptions>().BindConfiguration("");
            })
            .Build();
        await using (var scope = _host.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<ShopContext>().Database.EnsureCreatedAsync(Token);
        }

        await _host.StartAsync(Token);

        await using (var scope = _host.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ShopContext>();
            database.Orders.Add(new Order { Id = 41 });
            var outbox = scope.ServiceProvider.GetRequiredService<IOutboxSender>();
            await outbox.SendAsync<OrderPlacedPayload, OrderPlaced>(new OrderPlaced(41), "placed-41", cancellationToken: Token);
            await outbox.SendAsync<SendReceiptPayload, SendReceipt>(new SendReceipt(42), "receipt-42", cancellationToken: Token);
            await database.SaveChangesAsync(Token);
        }

        var received = _host.Services.GetRequiredService<Received>();
        Assert.Equal((OrderPlacedPayload.WireName, 41, "placed-41"), await received.WaitForAsync(41));
        Assert.Equal((SendReceiptPayload.WireName, 42, "receipt-42"), await received.WaitForAsync(42));

        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (true)
        {
            await using var scope = _host.Services.CreateAsyncScope();
            if (!await scope.ServiceProvider.GetRequiredService<ShopContext>().Set<OutboxMessage>().AnyAsync(Token))
            {
                break;
            }

            Assert.True(DateTime.UtcNow < deadline, "The sent messages' rows were never deleted.");
            await Task.Delay(100, Token);
        }
    }

    // The topology a provisioning step declares from the services' infrastructure requirements.
    private async Task DeclareTopologyAsync()
    {
        var factory = new ConnectionFactory { Uri = new Uri(broker.Uri) };
        await using var connection = await factory.CreateConnectionAsync(Token);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: Token);
        var quorum = new Dictionary<string, object?> { ["x-queue-type"] = "quorum" };
        await channel.ExchangeDeclareAsync(_topic, ExchangeType.Topic, durable: true, cancellationToken: Token);
        await channel.QueueDeclareAsync(Subscription, durable: true, exclusive: false, autoDelete: false, arguments: quorum, cancellationToken: Token);
        await channel.QueueBindAsync(Subscription, _topic, routingKey: OrderPlacedPayload.WireName, cancellationToken: Token);
        await channel.QueueDeclareAsync(_queue, durable: true, exclusive: false, autoDelete: false, arguments: quorum, cancellationToken: Token);
        await channel.ExchangeDeclareAsync(_queue, ExchangeType.Direct, durable: true, cancellationToken: Token);
        await channel.QueueBindAsync(_queue, _queue, routingKey: _queue, cancellationToken: Token);
    }
}
