using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RabbitMQ.Client;

namespace Bitwarden.Server.Sdk.MessageBroker.Tests;

public sealed class ItemRecorder
{
    public ConcurrentBag<int> Received { get; } = [];
}

public sealed class RecordingItemConsumer(ItemRecorder recorder) : IMessageConsumer<MyItemPayload, MyItem>
{
    public Task HandleAsync(Envelope<MyItemPayload, MyItem> envelope, CancellationToken cancellationToken)
    {
        recorder.Received.Add(envelope.Payload.Id);
        return Task.CompletedTask;
    }
}

/// <summary>
/// A RabbitMQ broker that goes away and comes back. Hosted consumers subscribe again and senders
/// reconnect, whether the broker restarts under a running host or is down when the host starts.
/// The broker keeps one host port across restarts, as a real broker keeps its address.
/// </summary>
public sealed class RabbitOutageTests : IAsyncLifetime
{
    private static readonly TimeSpan RecoveryTimeout = TimeSpan.FromSeconds(60);

    private readonly int _port = FreePort();
    private readonly string _topic = $"{Guid.NewGuid():N}"[..8];
    private readonly List<IHost> _hosts = [];
    private IContainer? _broker;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        foreach (var host in _hosts)
        {
            await host.StopAsync();
            host.Dispose();
        }
        if (_broker is not null) await _broker.DisposeAsync();
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private async Task StartBrokerAsync()
    {
        _broker ??= new ContainerBuilder()
            .WithImage("rabbitmq")
            .WithPortBinding(_port, 5672)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Server startup complete"))
            .Build();
        await _broker.StartAsync(TestContext.Current.CancellationToken);
    }

    private IHost BuildHost() => new HostBuilder()
        .ConfigureAppConfiguration(b => b.AddInMemoryCollection(
            new Dictionary<string, string?> { ["RabbitUri"] = $"amqp://guest:guest@localhost:{_port}/" }))
        .ConfigureServices(services =>
        {
            services.AddMetrics();
            services.AddSingleton<ItemRecorder>();
            services.AddPublisher<MyItemPayload, MyItem>(_topic);
            services.AddMessageConsumer<MyItemPayload, MyItem, RecordingItemConsumer>(_topic, "outage");
            services.AddOptions<MessagingOptions>().BindConfiguration("");
        })
        .Build();

    // The topology a provisioning step would declare: the topic, and its subscription queue and
    // binding. Durable, so it survives a broker restart; declaring it again is harmless.
    private async Task DeclareTopologyAsync()
    {
        var factory = new ConnectionFactory { Uri = new Uri($"amqp://guest:guest@localhost:{_port}/") };
        await using var conn = await factory.CreateConnectionAsync(TestContext.Current.CancellationToken);
        await using var channel = await conn.CreateChannelAsync(cancellationToken: TestContext.Current.CancellationToken);
        var quorum = new Dictionary<string, object?> { ["x-queue-type"] = "quorum" };
        await channel.ExchangeDeclareAsync(_topic, ExchangeType.Topic, durable: true, cancellationToken: TestContext.Current.CancellationToken);
        await channel.QueueDeclareAsync($"{_topic}.outage", durable: true, exclusive: false, autoDelete: false, arguments: quorum,
            cancellationToken: TestContext.Current.CancellationToken);
        await channel.QueueBindAsync($"{_topic}.outage", _topic, routingKey: MyItemPayload.WireName,
            cancellationToken: TestContext.Current.CancellationToken);
    }

    // Sends an item, retrying while the broker is still coming back.
    private static async Task SendAsync(IHost host, string topic, int id)
    {
        var item = host.Services.GetRequiredKeyedService<Publisher<MyItemPayload, MyItem>>(MessageBrokerKeys.Publisher<MyItemPayload>(topic));
        var deadline = DateTime.UtcNow + RecoveryTimeout;
        while (true)
        {
            try
            {
                await item.Publish(new MyItem(id)).SendAsync(TestContext.Current.CancellationToken);
                return;
            }
            catch (BrokerUnavailableException) when (DateTime.UtcNow < deadline)
            {
                await Task.Delay(500, TestContext.Current.CancellationToken);
            }
        }
    }

    private static async Task WaitForAsync(IHost host, int id)
    {
        var items = host.Services.GetRequiredService<ItemRecorder>();
        var deadline = DateTime.UtcNow + RecoveryTimeout;
        while (!items.Received.Contains(id))
        {
            Assert.True(DateTime.UtcNow < deadline, $"Item {id} wasn't received.");
            await Task.Delay(100, TestContext.Current.CancellationToken);
        }
    }

    private async Task<IHost> StartHostAsync()
    {
        var host = BuildHost();
        await host.StartAsync(TestContext.Current.CancellationToken);
        _hosts.Add(host);
        return host;
    }

    [Fact(Timeout = 180 * 1000)]
    public async Task ConsumersAndSendersCarryOnAfterTheBrokerRestarts()
    {
        await StartBrokerAsync();
        await DeclareTopologyAsync();
        var host = await StartHostAsync();

        await SendAsync(host, _topic, 1);
        await WaitForAsync(host, 1);

        await _broker!.StopAsync(TestContext.Current.CancellationToken);
        await Task.Delay(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        await _broker.StartAsync(TestContext.Current.CancellationToken);
        await DeclareTopologyAsync();

        await SendAsync(host, _topic, 2);
        await WaitForAsync(host, 2);
    }

    [Fact(Timeout = 120 * 1000)]
    public async Task SendsDuringAnOutageShareOneAttemptToConnect()
    {
        // An address that doesn't answer, as in a network partition: each attempt to connect runs
        // until its timeout rather than being refused. Concurrent sends must wait for one shared
        // attempt, not one each in turn.
        var host = new HostBuilder()
            .ConfigureAppConfiguration(b => b.AddInMemoryCollection(
                new Dictionary<string, string?> { ["RabbitUri"] = "amqp://guest:guest@10.255.255.1:5672/" }))
            .ConfigureServices(services =>
            {
                services.AddMetrics();
                services.AddPublisher<MyItemPayload, MyItem>(_topic);
                services.AddOptions<MessagingOptions>().BindConfiguration("");
            })
            .Build();
        await host.StartAsync(TestContext.Current.CancellationToken);
        _hosts.Add(host);
        var publisher = host.Services.GetRequiredKeyedService<Publisher<MyItemPayload, MyItem>>(
            MessageBrokerKeys.Publisher<MyItemPayload>(_topic));

        var started = DateTime.UtcNow;
        var sends = Enumerable.Range(0, 5)
            .Select(i => Assert.ThrowsAsync<BrokerUnavailableException>(() =>
                publisher.Publish(new MyItem(i)).SendAsync(TestContext.Current.CancellationToken)))
            .ToList();
        await Task.WhenAll(sends);

        // One attempt (or two, if a send arrives just after one fails), never five in a row.
        Assert.True(DateTime.UtcNow - started < RabbitConnection.ConnectTimeout * 2.5,
            $"Five concurrent sends took {DateTime.UtcNow - started} to fail.");
    }

    [Fact(Timeout = 180 * 1000)]
    public async Task HostThatStartsWhileTheBrokerIsDownCatchesUpWhenItComesBack()
    {
        // No broker yet: the host still starts, and a send fails at once rather than waiting.
        var host = await StartHostAsync();
        var item = host.Services.GetRequiredKeyedService<Publisher<MyItemPayload, MyItem>>(
            MessageBrokerKeys.Publisher<MyItemPayload>(_topic));
        await Assert.ThrowsAsync<BrokerUnavailableException>(() =>
            item.Publish(new MyItem(0)).SendAsync(TestContext.Current.CancellationToken)
                .WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken));

        await StartBrokerAsync();
        await DeclareTopologyAsync();

        await SendAsync(host, _topic, 1);
        await WaitForAsync(host, 1);
    }
}
