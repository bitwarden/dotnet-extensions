using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

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

public sealed class RabbitOutageTests : IAsyncLifetime
{
    private static readonly TimeSpan RecoveryTimeout = TimeSpan.FromSeconds(60);

    private readonly int _port = FreePort();
    private readonly string _topic = $"{Guid.NewGuid():N}"[..8];
    private readonly List<IHost> _hosts = [];
    private IContainer? _broker;

    private string Queue => $"{_topic}.commands";

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        foreach (var host in _hosts)
        {
            await host.StopAsync();
            host.Dispose();
        }
        if (_broker is not null)
        {
            await _broker.DisposeAsync();
        }
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
            .WithImage(AmqpTestBroker.Image)
            .WithPortBinding(_port, 5672)
            .WithWaitStrategy(AmqpTestBroker.Ready)
            .Build();
        await _broker.StartAsync(TestContext.Current.CancellationToken);
    }

    private IHost BuildHost() => new HostBuilder()
        .ConfigureAppConfiguration(b => b.AddInMemoryCollection(
            new Dictionary<string, string?> { ["RabbitUri"] = $"amqp://guest:guest@localhost:{_port}/" }))
        .ConfigureServices(services =>
        {
            services.AddMetrics();
            services.AddHealthChecks();
            services.AddSingleton<ItemRecorder>();
            services.AddSingleton<CommandRecorder>();
            services.AddPublisher<MyItemPayload, MyItem>(_topic);
            services.AddMessageConsumer<MyItemPayload, MyItem, RecordingItemConsumer>(_topic, "outage");
            services.AddCommandSender<SendMailPayload, SendMail>(Queue);
            services.AddCommandConsumer<SendMailPayload, SendMail, SendMailConsumer>(Queue);
            services.AddOptions<MessagingOptions>().BindConfiguration("");
        })
        .Build();

    // Durable, so it survives a broker restart.
    private async Task DeclareTopologyAsync()
    {
        var factory = new ConnectionFactory { Uri = new Uri($"amqp://guest:guest@localhost:{_port}/") };
        await using var conn = await ConnectAsync(factory);
        await using var channel = await conn.CreateChannelAsync(cancellationToken: TestContext.Current.CancellationToken);
        var quorum = new Dictionary<string, object?> { ["x-queue-type"] = "quorum" };
        await channel.ExchangeDeclareAsync(_topic, ExchangeType.Topic, durable: true, cancellationToken: TestContext.Current.CancellationToken);
        await channel.QueueDeclareAsync($"{_topic}.outage", durable: true, exclusive: false, autoDelete: false, arguments: quorum,
            cancellationToken: TestContext.Current.CancellationToken);
        await channel.QueueBindAsync($"{_topic}.outage", _topic, routingKey: MyItemPayload.WireName,
            cancellationToken: TestContext.Current.CancellationToken);
        await channel.ExchangeDeclareAsync(Queue, ExchangeType.Direct, durable: true, cancellationToken: TestContext.Current.CancellationToken);
        await channel.QueueDeclareAsync(Queue, durable: true, exclusive: false, autoDelete: false, arguments: quorum,
            cancellationToken: TestContext.Current.CancellationToken);
        await channel.QueueBindAsync(Queue, Queue, routingKey: Queue, cancellationToken: TestContext.Current.CancellationToken);
    }

    // After a restart the container's wait strategy returns before the broker accepts connections.
    private static async Task<IConnection> ConnectAsync(ConnectionFactory factory)
    {
        var deadline = DateTime.UtcNow + RecoveryTimeout;
        while (true)
        {
            try
            {
                return await factory.CreateConnectionAsync(TestContext.Current.CancellationToken);
            }
            catch (BrokerUnreachableException) when (DateTime.UtcNow < deadline)
            {
                await Task.Delay(500, TestContext.Current.CancellationToken);
            }
        }
    }

    private static async Task SendBothAsync(IHost host, string topic, string queue, int id)
    {
        var item = host.Services.GetRequiredKeyedService<Publisher<MyItemPayload, MyItem>>(MessageBrokerKeys.Publisher<MyItemPayload>(topic));
        var mail = host.Services.GetRequiredKeyedService<Publisher<SendMailPayload, SendMail>>(MessageBrokerKeys.Publisher<SendMailPayload>(queue));
        var deadline = DateTime.UtcNow + RecoveryTimeout;
        while (true)
        {
            try
            {
                await item.Publish(new MyItem(id)).SendAsync(TestContext.Current.CancellationToken);
                await mail.Publish(new SendMail(id)).SendAsync(TestContext.Current.CancellationToken);
                return;
            }
            catch (BrokerUnavailableException) when (DateTime.UtcNow < deadline)
            {
                await Task.Delay(500, TestContext.Current.CancellationToken);
            }
        }
    }

    private static async Task WaitForBothAsync(IHost host, int id)
    {
        var items = host.Services.GetRequiredService<ItemRecorder>();
        var commands = host.Services.GetRequiredService<CommandRecorder>();
        var deadline = DateTime.UtcNow + RecoveryTimeout;
        while (!(items.Received.Contains(id) && commands.Handled.Any(c => c.Id == id)))
        {
            Assert.True(DateTime.UtcNow < deadline,
                $"Item received: {items.Received.Contains(id)}; command handled: {commands.Handled.Any(c => c.Id == id)}.");
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

        await SendBothAsync(host, _topic, Queue, 1);
        await WaitForBothAsync(host, 1);

        await _broker!.StopAsync(TestContext.Current.CancellationToken);
        await Task.Delay(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        await _broker.StartAsync(TestContext.Current.CancellationToken);
        await DeclareTopologyAsync();

        await SendBothAsync(host, _topic, Queue, 2);
        await WaitForBothAsync(host, 2);
    }

    [Fact(Timeout = 120 * 1000)]
    public async Task SendsDuringAnOutageShareOneAttemptToConnect()
    {
        // An address that doesn't answer, so each attempt to connect runs until its timeout.
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

    [Fact(Timeout = 120 * 1000)]
    public async Task AConsumerThatCannotReachItsBrokerIsReportedDegraded()
    {
        // An address that doesn't answer, so each attempt to connect runs until its timeout.
        var host = new HostBuilder()
            .ConfigureAppConfiguration(b => b.AddInMemoryCollection(
                new Dictionary<string, string?> { ["RabbitUri"] = "amqp://guest:guest@10.255.255.1:5672/" }))
            .ConfigureServices(services =>
            {
                services.AddHealthChecks();
                services.AddSingleton<ItemRecorder>();
                services.AddMessageConsumer<MyItemPayload, MyItem, RecordingItemConsumer>(_topic, "outage");
                services.AddOptions<MessagingOptions>().BindConfiguration("");
            })
            .Build();
        await host.StartAsync(TestContext.Current.CancellationToken);
        _hosts.Add(host);
        var health = host.Services.GetRequiredService<HealthCheckService>();

        var until = DateTime.UtcNow + RabbitConnection.ConnectTimeout * 3;
        while (DateTime.UtcNow < until)
        {
            var report = await health.CheckHealthAsync(TestContext.Current.CancellationToken);
            Assert.Equal(HealthStatus.Degraded, report.Status);
            await Task.Delay(250, TestContext.Current.CancellationToken);
        }
    }

    [Fact(Timeout = 180 * 1000)]
    public async Task HostThatStartsWhileTheBrokerIsDownCatchesUpWhenItComesBack()
    {
        var host = await StartHostAsync();
        var item = host.Services.GetRequiredKeyedService<Publisher<MyItemPayload, MyItem>>(
            MessageBrokerKeys.Publisher<MyItemPayload>(_topic));
        await Assert.ThrowsAsync<BrokerUnavailableException>(() =>
            item.Publish(new MyItem(0)).SendAsync(TestContext.Current.CancellationToken)
                .WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken));

        // The readiness probe says which consumers can't receive, and nothing about the broker.
        var health = host.Services.GetRequiredService<HealthCheckService>();
        var down = await WaitForHealthAsync(health, HealthStatus.Degraded);
        var reason = down.Entries.Single().Value.Description!;
        Assert.Contains($"{_topic}/outage", reason);
        Assert.Contains(Queue, reason);
        Assert.DoesNotContain(_port.ToString(), reason);
        Assert.DoesNotContain("localhost", reason);

        await StartBrokerAsync();
        await DeclareTopologyAsync();

        await SendBothAsync(host, _topic, Queue, 1);
        await WaitForBothAsync(host, 1);
        await WaitForHealthAsync(health, HealthStatus.Healthy);
    }

    private static async Task<HealthReport> WaitForHealthAsync(HealthCheckService health, HealthStatus status)
    {
        var deadline = DateTime.UtcNow + RecoveryTimeout;
        while (true)
        {
            var report = await health.CheckHealthAsync(TestContext.Current.CancellationToken);
            if (report.Status == status)
            {
                return report;
            }

            Assert.True(DateTime.UtcNow < deadline, $"Health stayed {report.Status}, expected {status}.");
            await Task.Delay(200, TestContext.Current.CancellationToken);
        }
    }
}
