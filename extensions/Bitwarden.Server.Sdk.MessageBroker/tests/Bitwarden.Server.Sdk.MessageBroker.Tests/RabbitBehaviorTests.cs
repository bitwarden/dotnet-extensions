using Bitwarden.Server.Sdk.Caching;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace Bitwarden.Server.Sdk.MessageBroker.Tests;

public class RabbitBehaviorTests : BehaviorTests, IClassFixture<RabbitFixture>
{
    // Unique per test instance so each test declares its own exchange and queues.
    // Fresh names mean there are never stale messages to drain, eliminating InitializeAsync.
    private readonly string _topicName = Guid.NewGuid().ToString("N")[..8];
    protected override string TopicName => _topicName;

    private readonly RabbitFixture _fixture;

    // Container started on demand by TrySetupDroppableBrokerAsync; disposed in DisposeAsync.
    private IContainer? _droppableContainer;

    public RabbitBehaviorTests(RabbitFixture fixture)
    {
        _fixture = fixture;
    }

    protected override Dictionary<string, string?> CreateConfig() =>
        new() { { "RabbitUri", _fixture.GetUri() } };

    // Declares the exchanges, queues and bindings that AddPublisher / AddSubscriber register in
    // DI as BrokerTopologyDeclaration records. Connection failures are swallowed so the
    // "broker is down" tests still exercise their intended error paths.
    protected override async Task PreStartAsync(IHost host, CancellationToken cancellationToken)
    {
        var declarations = host.Services.GetServices<BrokerTopologyDeclaration>().ToList();
        if (declarations.Count == 0)
            return;

        var uri = host.Services.GetRequiredService<IOptions<MessagingOptions>>().Value.RabbitUri;
        if (string.IsNullOrEmpty(uri))
            return;

        try
        {
            var factory = new ConnectionFactory { Uri = new Uri(uri) };
            await using var conn = await factory.CreateConnectionAsync(cancellationToken);
            await using var channel = await conn.CreateChannelAsync(cancellationToken: cancellationToken);
            foreach (var decl in declarations)
            {
                // topic (not fanout): subscription queues bind with the family wire name as
                // routing key so each subscription only receives one family's messages.
                await channel.ExchangeDeclareAsync(decl.TopicName, ExchangeType.Topic, durable: true,
                    cancellationToken: cancellationToken);
                if (decl.SubscriptionName is not null)
                {
                    var queueName = $"{decl.TopicName}.{decl.SubscriptionName}";
                    await channel.QueueDeclareAsync(queueName, durable: true, exclusive: false,
                        autoDelete: false,
                        arguments: new Dictionary<string, object?> { ["x-queue-type"] = "quorum" },
                        cancellationToken: cancellationToken);
                    await channel.QueueBindAsync(queueName, decl.TopicName, routingKey: decl.FamilyWireName,
                        cancellationToken: cancellationToken);
                }
            }
        }
        catch (BrokerUnreachableException)
        {
            // Expected for CreateBrokerDownConfig() tests; nothing to declare.
        }
    }

    // Rabbit has no per-message lock; unacked messages are only requeued when the channel closes
    // (or after the broker's consumer acknowledgement timeout, which defaults to 30 minutes).
    // The lock-expiry redelivery test is not meaningful for this backend.
    protected override bool SupportsAutomaticRedelivery => false;

    // All subscriptions are pre-declared on the shared host so CreateSecondaryInstanceAsync
    // can return the same host. Each SubscribeAsync() call creates an independent channel,
    // making concurrent calls on the same subscriber singleton behave as competing consumers.
    protected override Task<IHost> CreateSecondaryInstanceAsync(IHost originalHost, string? subscriptionName = null) =>
        Task.FromResult(originalHost);

    protected override async Task<bool> TryInjectInvalidMessageAsync(string topicName)
    {
        // Publish a JSON null body directly to the exchange so the subscriber receives a
        // message that deserializes to null and exercises the nack/discard path.
        await InjectRawAsync(topicName, "null"u8.ToArray());
        return true;
    }

    protected override async Task<bool> TryInjectMalformedJsonMessageAsync(string topicName)
    {
        // Publish a malformed JSON body so the subscriber hits the catch(Exception) block
        // in the deserializer and exercises the nack/discard path for thrown exceptions.
        await InjectRawAsync(topicName, "{"u8.ToArray());
        return true;
    }

    private async Task InjectRawAsync(string topicName, byte[] body)
    {
        // Route under the MyItemPayload family so the message reaches the subscriber's queue and
        // passes the defensive family-header check — the test wants the subscriber to decide the
        // body is unusable, not the routing or header check.
        var factory = new ConnectionFactory { Uri = new Uri(_fixture.GetUri()) };
        await using var conn = await factory.CreateConnectionAsync(TestContext.Current.CancellationToken);
        await using var channel = await conn.CreateChannelAsync(cancellationToken: TestContext.Current.CancellationToken);
        await channel.BasicPublishAsync(
            exchange: topicName,
            routingKey: MyItemPayload.WireName,
            mandatory: false,
            basicProperties: new BasicProperties
            {
                Headers = new Dictionary<string, object?> { [BrokerProperties.PayloadFamily] = MyItemPayload.WireName },
            },
            body: body,
            cancellationToken: TestContext.Current.CancellationToken);
    }

    // Use an invalid host/port so the connection attempt fails immediately, faulting the TCS
    // without throwing from StartAsync (resilient startup). The first publish then surfaces
    // BrokerUnavailableException wrapping the underlying BrokerUnreachableException.
    protected override Dictionary<string, string?> CreateBrokerDownConfig() =>
        new() { { "RabbitUri", "amqp://guest:guest@localhost:1/" } };

    // Start a fresh, isolated container so stopping it does not disrupt the shared fixture
    // that the regular behavior tests depend on.
    protected override async Task<(Dictionary<string, string?>, Func<Task>)?>
        TrySetupDroppableBrokerAsync()
    {
        _droppableContainer = new ContainerBuilder()
            .WithImage("rabbitmq")
            .WithPortBinding(5672, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Server startup complete"))
            .Build();
        await _droppableContainer.StartAsync(TestContext.Current.CancellationToken);
        var config = new Dictionary<string, string?> { { "RabbitUri", GetContainerUri(_droppableContainer) } };
        var container = _droppableContainer;
        return (config, () => container.StopAsync(CancellationToken.None));
    }

    // Verifies that a publish attempt after the broker drops mid-connection surfaces
    // BrokerUnavailableException wrapping OperationInterruptedException (the RabbitMQ client's
    // exception for an interrupted channel). This tests a different code path than the
    // "broker never reachable" variant in PublishThrowsBrokerUnavailableExceptionWhenBrokerIsDown.
    [Fact(Timeout = 60 * 1000)]
    public async Task PublishThrowsBrokerUnavailableExceptionWhenBrokerGoesDown()
    {
        await using var container = new ContainerBuilder()
            .WithImage("rabbitmq")
            .WithPortBinding(5672, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Server startup complete"))
            .Build();
        await container.StartAsync(TestContext.Current.CancellationToken);

        var config = new Dictionary<string, string?> { { "RabbitUri", GetContainerUri(container) } };
        var host = await BuildHostAsync(config, services => services.AddPublisher<MyItemPayload, MyItem>(TopicName));
        var publisher = host.Services.GetRequiredKeyedService<Publisher<MyItemPayload, MyItem>>(PublisherKey);

        // Connection is established; now stop the broker.
        await container.StopAsync(TestContext.Current.CancellationToken);

        // Retry until the client detects the dropped connection.
        BrokerUnavailableException? ex = null;
        for (var i = 0; i < 20 && ex is null; i++)
        {
            try { await publisher.Publish(new MyItem(1)).SendAsync(TestContext.Current.CancellationToken); }
            catch (BrokerUnavailableException e) { ex = e; }
            if (ex is null)
                await Task.Delay(250, TestContext.Current.CancellationToken);
        }

        Assert.NotNull(ex);
        Assert.Equal(TopicName, ex.TopicName);
        Assert.IsAssignableFrom<OperationInterruptedException>(ex.InnerException);
    }

    /// <summary>
    /// Verifies that calling <see cref="Envelope{TPayload, TCeiling}.RequeueAsync"/> after the iterator has been
    /// disposed (rabbitChannel closed) completes without error, covering the
    /// <c>_channel.IsOpen ? ... : Task.CompletedTask</c> branch in <c>RabbitSubscriber.RequeueCoreAsync</c>.
    /// </summary>
    [Fact(Timeout = 60 * 1000)]
    public async Task AbandonOnClosedChannelCompletesCleanly()
    {
        var host = await BuildHostAsync(services =>
        {
            services.AddPublisher<MyItemPayload, MyItem>(TopicName);
            services.AddSubscriber<MyItemPayload, MyItem>(TopicName, SubscriptionName);
        });
        var publisher = host.Services.GetRequiredKeyedService<Publisher<MyItemPayload, MyItem>>(PublisherKey);
        var subscriber = host.Services.GetRequiredKeyedService<ISubscriber<MyItemPayload, MyItem>>(SubscriptionKey);

        await publisher.Publish(new MyItem(1)).SendAsync(TestContext.Current.CancellationToken);

        Envelope<MyItemPayload, MyItem>? captured = null;
        await foreach (var envelope in subscriber.SubscribeAsync(TestContext.Current.CancellationToken))
        {
            captured = envelope;
            break; // disposes the iterator → await using var rabbitChannel runs → IsOpen = false
        }

        // Exercise the MessageId and TraceId property getters on the Rabbit envelope.
        Assert.NotNull(captured!.MessageId);
        _ = captured.TraceId; // null when no tracing active; exercises the getter

        // Channel is now closed; RequeueAsync must short-circuit with Task.CompletedTask.
        await captured.RequeueAsync(cancellationToken: TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// A misconfigured queue — its routing-key binding selects <c>user.deleted</c>, but the
    /// client is registered as a <see cref="UserCreatedPayload"/> subscriber. The broker routes
    /// the mismatched message through; the subscriber's defensive x-bw-payload-family header
    /// check must catch it, dead-letter the message, and never yield it to the consumer.
    /// </summary>
    [Fact(Timeout = 60 * 1000)]
    public async Task MismatchedFamilyIsDeadLetteredByDefensiveCheck()
    {
        var misconfiguredTopic = Guid.NewGuid().ToString("N")[..8];
        const string MisfilteredSubscription = "misfiltered";
        var deadLetterExchange = $"dlx.{misconfiguredTopic}";
        var deadLetterQueue = $"dlq.{misconfiguredTopic}.{MisfilteredSubscription}";
        var misfilteredQueue = $"{misconfiguredTopic}.{MisfilteredSubscription}";

        // Build the misconfigured topology by hand: the subscriber's queue is bound with
        // routing-key = "user.deleted" (the WRONG family for UserCreatedPayload), and gets a
        // dead-letter exchange so we can observe the dead-lettered message.
        await using (var conn = await new ConnectionFactory { Uri = new Uri(_fixture.GetUri()) }
            .CreateConnectionAsync(TestContext.Current.CancellationToken))
        await using (var channel = await conn.CreateChannelAsync(cancellationToken: TestContext.Current.CancellationToken))
        {
            await channel.ExchangeDeclareAsync(misconfiguredTopic, ExchangeType.Topic, durable: true,
                cancellationToken: TestContext.Current.CancellationToken);
            await channel.ExchangeDeclareAsync(deadLetterExchange, ExchangeType.Fanout, durable: true,
                cancellationToken: TestContext.Current.CancellationToken);
            await channel.QueueDeclareAsync(deadLetterQueue, durable: true, exclusive: false, autoDelete: false,
                arguments: new Dictionary<string, object?> { ["x-queue-type"] = "quorum" },
                cancellationToken: TestContext.Current.CancellationToken);
            await channel.QueueBindAsync(deadLetterQueue, deadLetterExchange, routingKey: "",
                cancellationToken: TestContext.Current.CancellationToken);
            await channel.QueueDeclareAsync(misfilteredQueue, durable: true, exclusive: false, autoDelete: false,
                arguments: new Dictionary<string, object?>
                {
                    ["x-queue-type"] = "quorum",
                    ["x-dead-letter-exchange"] = deadLetterExchange,
                },
                cancellationToken: TestContext.Current.CancellationToken);
            // Routing-key binding selects user.deleted, but the subscriber reads as UserCreatedPayload.
            await channel.QueueBindAsync(misfilteredQueue, misconfiguredTopic,
                routingKey: UserDeletedPayload.WireName,
                cancellationToken: TestContext.Current.CancellationToken);
        }

        // Build the host directly (not via BuildHostAsync, which calls PreStartAsync — PreStartAsync
        // would try to redeclare the misfiltered queue without the DLX argument and fail with
        // "inequivalent arg").
        var host = new HostBuilder()
            .ConfigureAppConfiguration(b => b.AddInMemoryCollection(CreateConfig()))
            .ConfigureServices(services =>
            {
                services.AddMetrics();
                // Rabbit resolves its negotiation cache eagerly, so register both a backing
                // IDistributedCache and AddBitwardenCaching before the publisher/subscriber.
                services.AddDistributedMemoryCache();
                services.AddBitwardenCaching();
                services.AddPublisher<UserDeletedPayload, UserDeleted>(misconfiguredTopic);
                services.AddSubscriber<UserCreatedPayload, UserCreated>(misconfiguredTopic, MisfilteredSubscription);
                services.AddOptions<MessagingOptions>().BindConfiguration("");
                services.Configure<NegotiationOptions>(o => o.ServiceName = "negotiation");
            })
            .Build();
        await host.StartAsync(TestContext.Current.CancellationToken);
        try
        {

        var deletedPublisher = host.Services.GetRequiredKeyedService<Publisher<UserDeletedPayload, UserDeleted>>(
            MessageBrokerKeys.Publisher<UserDeletedPayload>(misconfiguredTopic));
        var misfiltered = host.Services.GetRequiredKeyedService<ISubscriber<UserCreatedPayload, UserCreated>>(
            $"{misconfiguredTopic}/{MisfilteredSubscription}");

        await deletedPublisher.Publish(new UserDeleted(Guid.NewGuid(), "test")).SendAsync(TestContext.Current.CancellationToken);

        // Prove the subscriber never yields the mismatched message. The cancellation fires once
        // we've given the receiver enough time to see (and dead-letter) the message.
        using var receiveCts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        receiveCts.CancelAfter(TimeSpan.FromSeconds(5));
        var receivedAny = false;
        try
        {
            await foreach (var _ in misfiltered.SubscribeAsync(receiveCts.Token))
            {
                receivedAny = true;
                break;
            }
        }
        catch (OperationCanceledException) { /* expected: subscriber drained nothing yieldable */ }
        catch (BrokerDisconnectedException) { /* expected: channel closed on iterator dispose */ }

        Assert.False(receivedAny, "Defensive check should have dead-lettered the mismatched family before yielding.");

        // The message must land in the dead-letter queue the operator wired up.
        await using var inspectConn = await new ConnectionFactory { Uri = new Uri(_fixture.GetUri()) }
            .CreateConnectionAsync(TestContext.Current.CancellationToken);
        await using var inspectChannel = await inspectConn.CreateChannelAsync(cancellationToken: TestContext.Current.CancellationToken);
        var result = await inspectChannel.BasicGetAsync(deadLetterQueue, autoAck: true, TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        var familyBytes = (byte[])result.BasicProperties.Headers![BrokerProperties.PayloadFamily]!;
        Assert.Equal(UserDeletedPayload.WireName, System.Text.Encoding.UTF8.GetString(familyBytes));
        }
        finally
        {
            await host.StopAsync(TestContext.Current.CancellationToken);
            host.Dispose();
        }
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        if (_droppableContainer is not null)
            await _droppableContainer.DisposeAsync();
    }

    private static string GetContainerUri(IContainer container) =>
        $"amqp://guest:guest@{container.Hostname}:{container.GetMappedPublicPort(5672)}/";
}
