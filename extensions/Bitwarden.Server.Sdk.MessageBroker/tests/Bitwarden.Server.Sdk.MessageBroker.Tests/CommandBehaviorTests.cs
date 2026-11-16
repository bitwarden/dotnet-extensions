using System.Collections.Concurrent;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace Bitwarden.Server.Sdk.MessageBroker.Tests;

// Three command families a notification service might accept on its one command queue.

public sealed record SendMail(int Id) : SendMailPayload.ISole;

public class SendMailPayload : Payload<SendMailPayload, SendMail, SendMail>, IPayloadVariants<SendMailPayload>, IPayloadWireName<SendMailPayload>
{
    public static string WireName => "send.mail";
    public static IReadOnlyList<(Type, string)> Variants => [(typeof(SendMail), "v1")];
}

public sealed record SendPush(int Id) : SendPushPayload.ISole;

public class SendPushPayload : Payload<SendPushPayload, SendPush, SendPush>, IPayloadVariants<SendPushPayload>, IPayloadWireName<SendPushPayload>
{
    public static string WireName => "send.push";
    public static IReadOnlyList<(Type, string)> Variants => [(typeof(SendPush), "v1")];
}

public sealed record SendSms(int Id) : SendSmsPayload.ISole;

public class SendSmsPayload : Payload<SendSmsPayload, SendSms, SendSms>, IPayloadVariants<SendSmsPayload>, IPayloadWireName<SendSmsPayload>
{
    public static string WireName => "send.sms";
    public static IReadOnlyList<(Type, string)> Variants => [(typeof(SendSms), "v1")];
}

/// <summary>What a command consumer saw, for the tests to wait on.</summary>
public sealed record HandledCommand(string Family, int Id, string MessageId, int DeliveryCount);

public sealed class CommandRecorder
{
    private readonly ConcurrentQueue<HandledCommand> _handled = new();

    /// <summary>Ids whose deliveries the consumer should fail, up to the given delivery, to exercise redelivery.</summary>
    public ConcurrentDictionary<int, int> FailDeliveries { get; } = new();

    public IReadOnlyCollection<HandledCommand> Handled => _handled;

    public void Record(HandledCommand command) => _handled.Enqueue(command);

    public async Task<IReadOnlyList<HandledCommand>> WaitForAsync(Func<HandledCommand, bool> match, int count)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            var found = _handled.Where(match).ToList();
            if (found.Count >= count) return found;
            await Task.Delay(50, TestContext.Current.CancellationToken);
        }
        throw new TimeoutException($"Expected {count} matching commands; saw {_handled.Count(match)}.");
    }
}

public sealed class SendMailConsumer(CommandRecorder recorder) : IMessageConsumer<SendMailPayload, SendMail>
{
    public Task HandleAsync(Envelope<SendMailPayload, SendMail> envelope, CancellationToken cancellationToken)
    {
        recorder.Record(new(SendMailPayload.WireName, envelope.Payload.Id, envelope.MessageId, envelope.DeliveryCount));
        if (recorder.FailDeliveries.TryGetValue(envelope.Payload.Id, out var failUpTo) && envelope.DeliveryCount <= failUpTo)
            throw new InvalidOperationException("Delivery fails on purpose.");
        return Task.CompletedTask;
    }
}

public sealed class SendPushConsumer(CommandRecorder recorder) : IMessageConsumer<SendPushPayload, SendPush>
{
    public Task HandleAsync(Envelope<SendPushPayload, SendPush> envelope, CancellationToken cancellationToken)
    {
        recorder.Record(new(SendPushPayload.WireName, envelope.Payload.Id, envelope.MessageId, envelope.DeliveryCount));
        return Task.CompletedTask;
    }
}

public sealed class OtherSendMailConsumer : IMessageConsumer<SendMailPayload, SendMail>
{
    public Task HandleAsync(Envelope<SendMailPayload, SendMail> envelope, CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>
/// Command queues on every backend: one queue, owned by the receiver, carrying several command
/// families, each handed to its own consumer.
/// </summary>
public abstract class CommandBehaviorTests : IAsyncLifetime
{
    private readonly List<IHost> _hosts = [];

    protected abstract Dictionary<string, string?> CreateConfig();

    /// <summary>The receiver's command queue.</summary>
    protected abstract string QueueName { get; }

    /// <summary>Whether sending to a queue that doesn't exist fails. False for the in-memory backend, which has no broker.</summary>
    protected virtual bool DetectsMissingQueue => true;

    /// <summary>Runs before the host starts, so a backend can declare the queues the host needs.</summary>
    protected virtual Task PreStartAsync(IHost host) => Task.CompletedTask;

    public virtual ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        foreach (var host in _hosts)
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    // One host that sends all three families and consumes two of them: send.sms has no consumer.
    private async Task<IHost> CreateHostAsync(bool track = true)
    {
        var host = new HostBuilder()
            .ConfigureAppConfiguration(b => b.AddInMemoryCollection(CreateConfig()))
            .ConfigureServices(services =>
            {
                services.AddMetrics();
                services.AddSingleton<CommandRecorder>();
                services.AddCommandSender<SendMailPayload, SendMail>(QueueName);
                services.AddCommandSender<SendPushPayload, SendPush>(QueueName);
                services.AddCommandSender<SendSmsPayload, SendSms>(QueueName);
                services.AddCommandConsumer<SendMailPayload, SendMail, SendMailConsumer>(QueueName, retry => retry.MaxDeliveries = 5);
                services.AddCommandConsumer<SendPushPayload, SendPush, SendPushConsumer>(QueueName);
                services.AddOptions<MessagingOptions>().BindConfiguration("");
            })
            .Build();
        await PreStartAsync(host);
        await host.StartAsync(TestContext.Current.CancellationToken);
        if (track) _hosts.Add(host);
        return host;
    }

    private static Publisher<TPayload, TCeiling> Sender<TPayload, TCeiling>(IHost host, string queueName)
        where TPayload : PayloadCeiling<TPayload, TCeiling>, IPayloadVariants<TPayload>, IPayloadWireName<TPayload>
        where TCeiling : Payload<TPayload>.ICeiling
        => host.Services.GetRequiredKeyedService<Publisher<TPayload, TCeiling>>(MessageBrokerKeys.Publisher<TPayload>(queueName));

    [Fact(Timeout = 60 * 1000)]
    public async Task EachCommandFamilyReachesItsOwnConsumer()
    {
        var host = await CreateHostAsync();
        var recorder = host.Services.GetRequiredService<CommandRecorder>();
        var (mailId, pushId) = (Random.Shared.Next(), Random.Shared.Next());
        var mailMessageId = Guid.CreateVersion7().ToString();
        var pushMessageId = Guid.CreateVersion7().ToString();

        await Sender<SendMailPayload, SendMail>(host, QueueName).Publish(new SendMail(mailId))
            .SendAsync(mailMessageId, TestContext.Current.CancellationToken);
        await Sender<SendPushPayload, SendPush>(host, QueueName).Publish(new SendPush(pushId))
            .SendAsync(pushMessageId, TestContext.Current.CancellationToken);

        var mail = Assert.Single(await recorder.WaitForAsync(c => c.Id == mailId, 1));
        var push = Assert.Single(await recorder.WaitForAsync(c => c.Id == pushId, 1));
        Assert.Equal((SendMailPayload.WireName, mailMessageId), (mail.Family, mail.MessageId));
        Assert.Equal((SendPushPayload.WireName, pushMessageId), (push.Family, push.MessageId));
    }

    [Fact(Timeout = 60 * 1000)]
    public async Task CommandWithNoConsumerIsSetAsideAndTheQueueKeepsFlowing()
    {
        var host = await CreateHostAsync();
        var recorder = host.Services.GetRequiredService<CommandRecorder>();
        var (smsId, mailId) = (Random.Shared.Next(), Random.Shared.Next());

        // send.sms is accepted by no consumer in this host; it must not block the send.mail behind it.
        await Sender<SendSmsPayload, SendSms>(host, QueueName).Publish(new SendSms(smsId))
            .SendAsync(TestContext.Current.CancellationToken);
        await Sender<SendMailPayload, SendMail>(host, QueueName).Publish(new SendMail(mailId))
            .SendAsync(TestContext.Current.CancellationToken);

        await recorder.WaitForAsync(c => c.Id == mailId, 1);
        Assert.DoesNotContain(recorder.Handled, c => c.Id == smsId);
        await AssertDeadLetteredAsync(SendSmsPayload.WireName);
    }

    /// <summary>Asserts a message of <paramref name="family"/> reached the queue's dead-letter store, where the backend has one the test can read.</summary>
    protected virtual Task AssertDeadLetteredAsync(string family) => Task.CompletedTask;

    [Fact(Timeout = 60 * 1000)]
    public async Task ConsumerThatThrowsSeesTheSameCommandAgain()
    {
        var host = await CreateHostAsync();
        var recorder = host.Services.GetRequiredService<CommandRecorder>();
        var mailId = Random.Shared.Next();
        recorder.FailDeliveries[mailId] = 2;
        var messageId = Guid.CreateVersion7().ToString();

        await Sender<SendMailPayload, SendMail>(host, QueueName).Publish(new SendMail(mailId))
            .SendAsync(messageId, TestContext.Current.CancellationToken);

        // Each failure is counted, so the third delivery says so.
        var deliveries = await recorder.WaitForAsync(c => c.Id == mailId, 3);
        Assert.All(deliveries, d => Assert.Equal(messageId, d.MessageId));
        Assert.Equal([1, 2, 3], deliveries.Select(d => d.DeliveryCount).Order().Take(3));
    }

    [Fact(Timeout = 120 * 1000)]
    public async Task ReceiverEndsCleanlyWhenTheHostStopsWhileItConnects()
    {
        // Stop the host while the queue's receiver may still be connecting. However early that
        // is, stopping must be prompt and the receiver must end without a fault.
        foreach (var delay in new[] { 0, 1, 5, 10, 20 })
        {
            for (var attempt = 0; attempt < 4; attempt++)
            {
                var host = await CreateHostAsync(track: false);
                var service = host.Services.GetRequiredKeyedService<CommandQueueService>(QueueName);

                await Task.Delay(delay, TestContext.Current.CancellationToken);
                await host.StopAsync(TestContext.Current.CancellationToken)
                    .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
                host.Dispose();

                var execution = service.ExecuteTask;
                if (execution is null) continue;
                await Task.WhenAny(execution, Task.Delay(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
                Assert.True(execution.IsCompleted, $"The receiver was still running after the host stopped (delay {delay} ms).");
                Assert.False(execution.IsFaulted, $"The receiver faulted: {execution.Exception?.GetBaseException()}");
            }
        }
    }

    [Fact(Timeout = 60 * 1000)]
    public async Task SendingToAQueueThatDoesNotExistThrows()
    {
        Assert.SkipUnless(DetectsMissingQueue, "This backend has no broker to refuse the command.");
        var missingQueue = $"missing-{Guid.NewGuid():N}";
        // A host that only sends: nothing declares the queue it sends to.
        var host = new HostBuilder()
            .ConfigureAppConfiguration(b => b.AddInMemoryCollection(CreateConfig()))
            .ConfigureServices(services =>
            {
                services.AddMetrics();
                services.AddCommandSender<SendMailPayload, SendMail>(missingQueue);
                services.AddOptions<MessagingOptions>().BindConfiguration("");
            })
            .Build();
        await host.StartAsync(TestContext.Current.CancellationToken);
        _hosts.Add(host);

        await Assert.ThrowsAsync<BrokerUnavailableException>(() =>
            Sender<SendMailPayload, SendMail>(host, missingQueue).Publish(new SendMail(1))
                .SendAsync(TestContext.Current.CancellationToken));
    }
}

public class ChannelCommandBehaviorTests : CommandBehaviorTests
{
    private readonly string _queueName = $"{Guid.NewGuid():N}"[..8] + ".commands";
    protected override string QueueName => _queueName;
    protected override bool DetectsMissingQueue => false;
    protected override Dictionary<string, string?> CreateConfig() => [];
}

public class RabbitCommandBehaviorTests : CommandBehaviorTests, IClassFixture<RabbitBehaviorTests.RabbitFixture>
{
    private readonly RabbitBehaviorTests.RabbitFixture _fixture;
    private readonly string _queueName = $"{Guid.NewGuid():N}"[..8] + ".commands";

    public RabbitCommandBehaviorTests(RabbitBehaviorTests.RabbitFixture fixture) => _fixture = fixture;

    protected override string QueueName => _queueName;

    protected override Dictionary<string, string?> CreateConfig() => new() { ["RabbitUri"] = _fixture.GetUri(), ["VerifyTopologyAtStartup"] = "false" };

    // Declares each queue a command consumer reads, and its exchange, as the receiver's provisioning would.
    protected override async Task PreStartAsync(IHost host)
    {
        var uri = host.Services.GetRequiredService<IOptions<MessagingOptions>>().Value.RabbitUri!;
        var factory = new ConnectionFactory { Uri = new Uri(uri) };
        await using var conn = await factory.CreateConnectionAsync(TestContext.Current.CancellationToken);
        await using var channel = await conn.CreateChannelAsync(cancellationToken: TestContext.Current.CancellationToken);
        foreach (var queue in host.Services.GetServices<CommandQueueDeclaration>().Where(d => d.IsConsumer).Select(d => d.QueueName).Distinct())
        {
            // With a delivery limit, as BROKER-TOPOLOGY.md's policy gives every command queue: LavinMQ
            // counts deliveries in x-delivery-count only on a queue that has one.
            await channel.ExchangeDeclareAsync(queue, ExchangeType.Direct, durable: true,
                cancellationToken: TestContext.Current.CancellationToken);
            await channel.QueueDeclareAsync(queue, durable: true, exclusive: false, autoDelete: false,
                arguments: new Dictionary<string, object?> { ["x-queue-type"] = "quorum", ["x-delivery-limit"] = 10 },
                cancellationToken: TestContext.Current.CancellationToken);
            await channel.QueueBindAsync(queue, queue, routingKey: queue,
                cancellationToken: TestContext.Current.CancellationToken);
        }
    }

    /// <summary>
    /// A command sent before its queue is provisioned fails, whichever part is missing, and the same
    /// sender succeeds once the queue is bound to its exchange: what an outbox retrying the command sees.
    /// </summary>
    [Fact(Timeout = 60 * 1000)]
    public async Task ACommandFailsUntilItsQueueIsBoundToItsExchange()
    {
        var queue = $"late-{Guid.NewGuid():N}";
        var host = new HostBuilder()
            .ConfigureAppConfiguration(b => b.AddInMemoryCollection(CreateConfig()))
            .ConfigureServices(services =>
            {
                services.AddMetrics();
                services.AddCommandSender<SendMailPayload, SendMail>(queue);
                services.AddOptions<MessagingOptions>().BindConfiguration("");
            })
            .Build();
        await host.StartAsync(TestContext.Current.CancellationToken);
        try
        {
            var sender = host.Services.GetRequiredKeyedService<Publisher<SendMailPayload, SendMail>>(
                MessageBrokerKeys.Publisher<SendMailPayload>(queue));
            Task Send() => sender.Publish(new SendMail(1)).SendAsync(TestContext.Current.CancellationToken);

            // No exchange: the broker closes the channel.
            await Assert.ThrowsAsync<BrokerUnavailableException>(Send);

            var factory = new ConnectionFactory { Uri = new Uri(_fixture.GetUri()) };
            await using var conn = await factory.CreateConnectionAsync(TestContext.Current.CancellationToken);
            await using var channel = await conn.CreateChannelAsync(cancellationToken: TestContext.Current.CancellationToken);
            await channel.ExchangeDeclareAsync(queue, ExchangeType.Direct, durable: true,
                cancellationToken: TestContext.Current.CancellationToken);

            // An exchange with no queue bound: the broker returns the command.
            var returned = await Assert.ThrowsAsync<BrokerUnavailableException>(Send);
            Assert.True(returned.InnerException is PublishException { IsReturn: true },
                $"Expected a returned message; got {returned.InnerException?.GetType().Name}.");

            await channel.QueueDeclareAsync(queue, durable: true, exclusive: false, autoDelete: false,
                arguments: new Dictionary<string, object?> { ["x-queue-type"] = "quorum" },
                cancellationToken: TestContext.Current.CancellationToken);
            await channel.QueueBindAsync(queue, queue, routingKey: queue, cancellationToken: TestContext.Current.CancellationToken);

            await Send();
            Assert.NotNull(await channel.BasicGetAsync(queue, autoAck: true, TestContext.Current.CancellationToken));
        }
        finally
        {
            await host.StopAsync(TestContext.Current.CancellationToken);
            host.Dispose();
        }
    }
}

[Collection(AzureServiceBusCollection.Name)]
public class AzureServiceBusCommandBehaviorTests : CommandBehaviorTests
{
    private readonly AzureServiceBusFixture _fixture;

    public AzureServiceBusCommandBehaviorTests(AzureServiceBusFixture fixture) => _fixture = fixture;

    // Pre-declared in the emulator's configuration, which has no management endpoint.
    protected override string QueueName => "commands";

    protected override Dictionary<string, string?> CreateConfig() =>
        new() { ["AzureServiceBusConnectionString"] = _fixture.GetConnectionString(), ["VerifyTopologyAtStartup"] = "false" };

    // The queue is shared by every test in the class: start each one with it, and its dead-letter
    // queue, empty.
    public override async ValueTask InitializeAsync()
    {
        await using var client = new ServiceBusClient(_fixture.GetConnectionString());
        await DrainAsync(client, new ServiceBusReceiverOptions { ReceiveMode = ServiceBusReceiveMode.ReceiveAndDelete });
        await DrainAsync(client, new ServiceBusReceiverOptions
        {
            ReceiveMode = ServiceBusReceiveMode.ReceiveAndDelete,
            SubQueue = SubQueue.DeadLetter,
        });
    }

    private async Task DrainAsync(ServiceBusClient client, ServiceBusReceiverOptions options)
    {
        await using var receiver = client.CreateReceiver(QueueName, options);
        IReadOnlyList<ServiceBusReceivedMessage> batch;
        do
        {
            batch = await receiver.ReceiveMessagesAsync(maxMessages: 100, maxWaitTime: TimeSpan.FromSeconds(1));
        } while (batch.Count > 0);
    }

    protected override async Task AssertDeadLetteredAsync(string family)
    {
        await using var client = new ServiceBusClient(_fixture.GetConnectionString());
        await using var receiver = client.CreateReceiver(QueueName, new ServiceBusReceiverOptions { SubQueue = SubQueue.DeadLetter });
        var message = await receiver.ReceiveMessageAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.NotNull(message);
        Assert.Equal(family, message.ApplicationProperties[BrokerProperties.PayloadFamily]);
        Assert.Contains(family, message.DeadLetterReason);
        await receiver.CompleteMessageAsync(message, TestContext.Current.CancellationToken);
    }
}

public class CommandRegistrationTests
{
    [Fact]
    public void RegisteringTheSameConsumerTwiceIsANoOp()
    {
        var services = new ServiceCollection();
        services.AddCommandConsumer<SendMailPayload, SendMail, SendMailConsumer>("notification.commands");
        services.AddCommandConsumer<SendMailPayload, SendMail, SendMailConsumer>("notification.commands");

        Assert.Single(services, d => d.ServiceType == typeof(CommandQueueDeclaration));
    }

    [Fact]
    public void ASecondConsumerForTheSameFamilyIsRefused()
    {
        var services = new ServiceCollection();
        services.AddCommandConsumer<SendMailPayload, SendMail, SendMailConsumer>("notification.commands");

        var ex = Assert.Throws<InvalidOperationException>(() =>
            services.AddCommandConsumer<SendMailPayload, SendMail, OtherSendMailConsumer>("notification.commands"));
        Assert.Contains("already consumed", ex.Message);
    }

    [Fact]
    public void OneQueueGetsOneReceiverHoweverManyFamiliesItCarries()
    {
        var services = new ServiceCollection();
        services.AddCommandConsumer<SendMailPayload, SendMail, SendMailConsumer>("notification.commands");
        services.AddCommandConsumer<SendPushPayload, SendPush, SendPushConsumer>("notification.commands");

        Assert.Single(services, d => d.ServiceType == typeof(CommandQueueService));
    }
}
