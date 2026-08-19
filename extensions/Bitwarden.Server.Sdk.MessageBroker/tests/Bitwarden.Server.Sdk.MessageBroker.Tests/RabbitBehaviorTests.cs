using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace Bitwarden.Server.Sdk.MessageBroker.Tests;

public class RabbitBehaviorTests : BehaviorTests, IClassFixture<RabbitBehaviorTests.RabbitFixture>
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
        var factory = new ConnectionFactory { Uri = new Uri(_fixture.GetUri()) };
        await using var conn = await factory.CreateConnectionAsync(TestContext.Current.CancellationToken);
        await using var channel = await conn.CreateChannelAsync(cancellationToken: TestContext.Current.CancellationToken);
        await channel.BasicPublishAsync(
            exchange: topicName,
            routingKey: "",
            mandatory: false,
            basicProperties: new BasicProperties(),
            body: "null"u8.ToArray(),
            cancellationToken: TestContext.Current.CancellationToken);
        return true;
    }

    protected override async Task<bool> TryInjectMalformedJsonMessageAsync(string topicName)
    {
        // Publish a malformed JSON body so the subscriber hits the catch(Exception) block
        // in the deserializer and exercises the nack/discard path for thrown exceptions.
        var factory = new ConnectionFactory { Uri = new Uri(_fixture.GetUri()) };
        await using var conn = await factory.CreateConnectionAsync(TestContext.Current.CancellationToken);
        await using var channel = await conn.CreateChannelAsync(cancellationToken: TestContext.Current.CancellationToken);
        await channel.BasicPublishAsync(
            exchange: topicName,
            routingKey: "",
            mandatory: false,
            basicProperties: new BasicProperties(),
            body: "{"u8.ToArray(),
            cancellationToken: TestContext.Current.CancellationToken);
        return true;
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
            .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(5672))
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
            .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(5672))
            .Build();
        await container.StartAsync(TestContext.Current.CancellationToken);

        var config = new Dictionary<string, string?> { { "RabbitUri", GetContainerUri(container) } };
        var host = await BuildHostAsync(config, services => services.AddPublisher<MyItem>(TopicName));
        var publisher = host.Services.GetRequiredKeyedService<IPublisher<MyItem>>(TopicName);

        // Connection is established; now stop the broker.
        await container.StopAsync(TestContext.Current.CancellationToken);

        // Retry until the client detects the dropped connection.
        BrokerUnavailableException? ex = null;
        for (var i = 0; i < 20 && ex is null; i++)
        {
            try { await publisher.PublishAsync(new MyItem(1), TestContext.Current.CancellationToken); }
            catch (BrokerUnavailableException e) { ex = e; }
            if (ex is null)
                await Task.Delay(250, TestContext.Current.CancellationToken);
        }

        Assert.NotNull(ex);
        Assert.Equal(TopicName, ex.TopicName);
        Assert.IsAssignableFrom<OperationInterruptedException>(ex.InnerException);
    }

    [Fact(Timeout = 60 * 1000)]
    public async Task PublishBatchThrowsBrokerUnavailableExceptionWhenBrokerIsDown()
    {
        var config = CreateBrokerDownConfig();
        var host = await BuildHostAsync(config, services => services.AddPublisher<MyItem>(TopicName));
        var publisher = host.Services.GetRequiredKeyedService<IPublisher<MyItem>>(TopicName);

        var ex = await Assert.ThrowsAsync<BrokerUnavailableException>(
            () => publisher.PublishBatchAsync([new MyItem(1)], TestContext.Current.CancellationToken));
        Assert.Equal(TopicName, ex.TopicName);
        Assert.NotNull(ex.InnerException);
    }

    /// <summary>
    /// Verifies that <see cref="IPublisher{T}.PublishBatchAsync"/> surfaces
    /// <see cref="BrokerUnavailableException"/> wrapping <see cref="OperationInterruptedException"/>
    /// when the broker goes down after the connection was established, covering the
    /// <c>catch (OperationInterruptedException)</c> branch in <c>RabbitPublisher.PublishBatchAsync</c>.
    /// </summary>
    [Fact(Timeout = 60 * 1000)]
    public async Task PublishBatchThrowsBrokerUnavailableExceptionWhenBrokerGoesDown()
    {
        await using var container = new ContainerBuilder()
            .WithImage("rabbitmq")
            .WithPortBinding(5672, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(5672))
            .Build();
        await container.StartAsync(TestContext.Current.CancellationToken);

        var config = new Dictionary<string, string?> { { "RabbitUri", GetContainerUri(container) } };
        var host = await BuildHostAsync(config, services => services.AddPublisher<MyItem>(TopicName));
        var publisher = host.Services.GetRequiredKeyedService<IPublisher<MyItem>>(TopicName);

        // Establish the connection, then stop the broker.
        await container.StopAsync(TestContext.Current.CancellationToken);

        // Retry until the client detects the dropped connection.
        BrokerUnavailableException? ex = null;
        for (var i = 0; i < 20 && ex is null; i++)
        {
            try { await publisher.PublishBatchAsync([new MyItem(1)], TestContext.Current.CancellationToken); }
            catch (BrokerUnavailableException e) { ex = e; }
            if (ex is null)
                await Task.Delay(250, TestContext.Current.CancellationToken);
        }

        Assert.NotNull(ex);
        Assert.Equal(TopicName, ex.TopicName);
        Assert.IsAssignableFrom<OperationInterruptedException>(ex.InnerException);
    }

    /// <summary>
    /// Verifies that calling <see cref="Envelope{T}.RequeueAsync"/> after the iterator has been
    /// disposed (rabbitChannel closed) completes without error, covering the
    /// <c>_channel.IsOpen ? ... : Task.CompletedTask</c> branch in <c>RabbitSubscriber.RequeueCoreAsync</c>.
    /// </summary>
    [Fact(Timeout = 60 * 1000)]
    public async Task AbandonOnClosedChannelCompletesCleanly()
    {
        var host = await BuildHostAsync(services =>
        {
            services.AddPublisher<MyItem>(TopicName);
            services.AddSubscriber<MyItem>(TopicName, SubscriptionName);
        });
        var publisher = host.Services.GetRequiredKeyedService<IPublisher<MyItem>>(TopicName);
        var subscriber = host.Services.GetRequiredKeyedService<ISubscriber<MyItem>>(SubscriptionKey);

        await publisher.PublishAsync(new MyItem(1), TestContext.Current.CancellationToken);

        Envelope<MyItem>? captured = null;
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

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        if (_droppableContainer is not null)
            await _droppableContainer.DisposeAsync();
    }

    private static string GetContainerUri(IContainer container) =>
        $"amqp://guest:guest@{container.Hostname}:{container.GetMappedPublicPort(5672)}/";

    public class RabbitFixture : IAsyncLifetime
    {
        private IContainer? _container;

        public string GetUri() =>
            $"amqp://guest:guest@{_container!.Hostname}:{_container.GetMappedPublicPort(5672)}/";

        public async ValueTask InitializeAsync()
        {
            _container = new ContainerBuilder()
                .WithImage("rabbitmq")
                .WithPortBinding(5672, true)
                .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(5672))
                .Build();

            await _container.StartAsync(TestContext.Current.CancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            if (_container != null) await _container.DisposeAsync();
        }
    }
}
