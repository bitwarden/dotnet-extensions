using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Bitwarden.Server.Sdk.MessageBroker.Tests;

public sealed record MyItem(int Id) : MyItemPayload.ISole;

public class MyItemPayload : Payload<MyItemPayload, MyItem, MyItem>, IPayloadVariants<MyItemPayload>
{
    public static IReadOnlyList<(Type, string)> Variants => [(typeof(MyItem), nameof(MyItem))];
}

public class MessagingOptionsValidationTests
{
    [Fact]
    public void FailsWhenBothBackendsAreConfigured()
    {
        var services = new ServiceCollection();
        services.AddPublisher<MyItemPayload, MyItem>("test");
        var provider = services.BuildServiceProvider();

        var validators = provider.GetServices<IValidateOptions<MessagingOptions>>();
        var options = new MessagingOptions
        {
            AzureServiceBusConnectionString = "Endpoint=sb://fake.servicebus.windows.net/;SharedAccessKeyName=k;SharedAccessKey=abc=",
            RabbitUri = "amqp://guest:guest@localhost/",
        };

        var failed = validators
            .Select(v => v.Validate(null, options))
            .Any(r => r.Failed);

        Assert.True(failed);
    }

    [Fact]
    public void PassesWhenOnlyOneBackendIsConfigured()
    {
        var services = new ServiceCollection();
        services.AddPublisher<MyItemPayload, MyItem>("test");
        var provider = services.BuildServiceProvider();

        var validators = provider.GetServices<IValidateOptions<MessagingOptions>>();
        var options = new MessagingOptions { RabbitUri = "amqp://guest:guest@localhost/" };

        var anyFailed = validators
            .Select(v => v.Validate(null, options))
            .Any(r => r.Failed);

        Assert.False(anyFailed);
    }
}

public abstract class BehaviorTests : IAsyncLifetime
{
    private readonly List<IHost> _instances = [];

    protected abstract Dictionary<string, string?> CreateConfig();

    /// <summary>Topic/queue name used for all registrations.</summary>
    protected virtual string TopicName => "test";

    /// <summary>Default subscription name. Defaults to <see cref="TopicName"/>.</summary>
    protected virtual string SubscriptionName => TopicName;

    /// <summary>The keyed service key for the default subscriber.</summary>
    protected string SubscriptionKey => $"{TopicName}/{SubscriptionName}";

    /// <summary>
    /// Whether the broker automatically redelivers a message whose lock has expired without the
    /// consumer settling it. False for the in-memory channel backend (no lock concept).
    /// </summary>
    protected virtual bool SupportsAutomaticRedelivery => true;

    /// <summary>
    /// Whether disposing the <see cref="IAsyncEnumerator{T}"/> returned by
    /// <see cref="ISubscriber{T}.SubscribeAsync"/> immediately requeues any unsettled message
    /// held by that enumerator. True for broker backends (ASB releases the lock on receiver
    /// close; Rabbit requeues unacked messages on channel close). False for the in-memory channel
    /// backend, which simply discards the message.
    /// </summary>
    protected virtual bool SupportsDisposalRequeue => true;

    /// <summary>
    /// How long to wait after not settling a message before expecting the broker to redeliver it.
    /// Override when the broker is configured with a shorter lock/consumer timeout.
    /// </summary>
    protected virtual TimeSpan LockExpiryDelay => TimeSpan.FromSeconds(35);

    /// <summary>
    /// Creates the primary host with publisher, default subscriber, and the two pub-sub
    /// subscriber groups ("pm" and "sm") pre-registered so all tests share one host.
    /// </summary>
    protected virtual Task<IHost> CreateInstanceAsync() => BuildHostAsync(services =>
    {
        services.AddPublisher<MyItemPayload, MyItem>(TopicName);
        services.AddSubscriber<MyItemPayload, MyItem>(TopicName, SubscriptionName);
        services.AddSubscriber<MyItemPayload, MyItem>(TopicName, "pm");
        services.AddSubscriber<MyItemPayload, MyItem>(TopicName, "sm");
    });

    /// <summary>
    /// Returns the host to use as a secondary participant (competing consumer or additional
    /// subscriber group). The default creates a new independent host; override to return
    /// <paramref name="originalHost"/> when the backend shares state within a single process
    /// (in-memory channels, ASB emulator with one connection pool, etc.).
    /// </summary>
    protected virtual Task<IHost> CreateSecondaryInstanceAsync(IHost originalHost, string? subscriptionName = null) =>
        BuildHostAsync(services =>
        {
            services.AddPublisher<MyItemPayload, MyItem>(TopicName);
            services.AddSubscriber<MyItemPayload, MyItem>(TopicName, subscriptionName ?? SubscriptionName);
        });

    protected async Task<IHost> BuildHostAsync(Action<IServiceCollection> configure)
    {
        var host = new HostBuilder()
            .ConfigureAppConfiguration(config => config.AddInMemoryCollection(CreateConfig()))
            .ConfigureServices(services =>
            {
                services.AddMetrics();
                configure(services);
                services.AddOptions<MessagingOptions>().BindConfiguration("");
            })
            .Build();
        await host.StartAsync(TestContext.Current.CancellationToken);
        _instances.Add(host);
        return host;
    }

    protected async Task<IHost> BuildHostAsync(Dictionary<string, string?> config, Action<IServiceCollection> configure)
    {
        var host = new HostBuilder()
            .ConfigureAppConfiguration(b => b.AddInMemoryCollection(config))
            .ConfigureServices(services =>
            {
                services.AddMetrics();
                configure(services);
                services.AddOptions<MessagingOptions>().BindConfiguration("");
            })
            .Build();
        await host.StartAsync(TestContext.Current.CancellationToken);
        _instances.Add(host);
        return host;
    }

    /// <summary>
    /// Runs before each test. Override to drain stale messages from persistent broker
    /// subscriptions so tests start with an empty queue.
    /// </summary>
    public virtual ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public virtual async ValueTask DisposeAsync()
    {
        foreach (var host in _instances)
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    [Fact(Timeout = 60 * 1000)]
    public async Task SimpleAsync()
    {
        var host = await CreateInstanceAsync();
        var publisher = host.Services.GetRequiredKeyedService<Publisher<MyItemPayload, MyItem>>(TopicName);
        var secondaryHost = await CreateSecondaryInstanceAsync(host);
        var subscriber = secondaryHost.Services.GetRequiredKeyedService<ISubscriber<MyItemPayload, MyItem>>(SubscriptionKey);

        await publisher.Publish(new MyItem(1)).SendAsync(TestContext.Current.CancellationToken);

        await foreach (var envelope in subscriber.SubscribeAsync(TestContext.Current.CancellationToken))
        {
            await envelope.CompleteAsync(TestContext.Current.CancellationToken);
            break;
        }
    }

    [Fact(Timeout = 60 * 1000)]
    public async Task EarlyMessagesCanBeProcessed()
    {
        var host = await CreateInstanceAsync();
        var publisher = host.Services.GetRequiredKeyedService<Publisher<MyItemPayload, MyItem>>(TopicName);

        await publisher.Publish(new MyItem(1)).SendAsync(TestContext.Current.CancellationToken);
        await publisher.Publish(new MyItem(2)).SendAsync(TestContext.Current.CancellationToken);

        var count = 0;
        var secondaryHost = await CreateSecondaryInstanceAsync(host);
        var subscriber = secondaryHost.Services.GetRequiredKeyedService<ISubscriber<MyItemPayload, MyItem>>(SubscriptionKey);
        await foreach (var envelope in subscriber.SubscribeAsync(TestContext.Current.CancellationToken))
        {
            await envelope.CompleteAsync(TestContext.Current.CancellationToken);
            if (++count >= 2) break;
        }

        Assert.Equal(2, count);
    }

    [Fact(Timeout = 60 * 1000)]
    public async Task AbandonedMessageIsRedelivered()
    {
        var host = await CreateInstanceAsync();
        var publisher = host.Services.GetRequiredKeyedService<Publisher<MyItemPayload, MyItem>>(TopicName);
        var subscriber = (await CreateSecondaryInstanceAsync(host)).Services.GetRequiredKeyedService<ISubscriber<MyItemPayload, MyItem>>(SubscriptionKey);

        await publisher.Publish(new MyItem(1)).SendAsync(TestContext.Current.CancellationToken);

        // Receive and explicitly return the message (abandon it for redelivery).
        await foreach (var envelope in subscriber.SubscribeAsync(TestContext.Current.CancellationToken))
        {
            Assert.Equal(1, envelope.Payload.Id);
            Assert.Equal(1, envelope.DeliveryCount);
            await envelope.RequeueAsync(cancellationToken: TestContext.Current.CancellationToken);
            break;
        }

        // The returned message must be redelivered with an incremented DeliveryCount.
        await foreach (var envelope in subscriber.SubscribeAsync(TestContext.Current.CancellationToken))
        {
            Assert.Equal(1, envelope.Payload.Id);
            Assert.Equal(2, envelope.DeliveryCount);
            await envelope.CompleteAsync(TestContext.Current.CancellationToken);
            break;
        }
    }

    /// <summary>
    /// Verifies that disposing the <see cref="IAsyncEnumerator{T}"/> without settling the
    /// current envelope immediately makes the message available again — the broker interprets
    /// enumerator disposal (receiver/channel close) as abandonment and requeues at once,
    /// without waiting for any lock timeout.
    /// </summary>
    [Fact(Timeout = 60 * 1000)]
    public async Task DisposingEnumeratorWithUnsettledMessageRequeuesImmediately()
    {
        Assert.SkipUnless(SupportsDisposalRequeue,
            "This backend does not requeue unsettled messages on enumerator disposal (in-memory channel discards them).");

        var host = await CreateInstanceAsync();
        var publisher = host.Services.GetRequiredKeyedService<Publisher<MyItemPayload, MyItem>>(TopicName);
        var subscriber = (await CreateSecondaryInstanceAsync(host)).Services
            .GetRequiredKeyedService<ISubscriber<MyItemPayload, MyItem>>(SubscriptionKey);

        await publisher.Publish(new MyItem(1)).SendAsync(TestContext.Current.CancellationToken);

        // Receive without settling, then dispose the enumerator — this closes the underlying
        // receiver/channel, which the broker treats as abandonment.
        var ct = TestContext.Current.CancellationToken;
        var enumerator = subscriber.SubscribeAsync(ct).GetAsyncEnumerator(ct);
        await enumerator.MoveNextAsync();
        Assert.Equal(1, enumerator.Current.Payload.Id);
        Assert.Equal(1, enumerator.Current.DeliveryCount);
        await enumerator.DisposeAsync(); // unsettled — broker must requeue immediately

        // The message must be immediately available with an incremented DeliveryCount.
        await foreach (var envelope in subscriber.SubscribeAsync(ct))
        {
            Assert.Equal(1, envelope.Payload.Id);
            Assert.True(envelope.DeliveryCount > 1,
                $"Expected DeliveryCount > 1 after enumerator disposal but got {envelope.DeliveryCount}.");
            await envelope.CompleteAsync(ct);
            break;
        }
    }

    /// <summary>
    /// Verifies that a message whose lock expires without the consumer settling it is
    /// automatically redelivered by the broker with an incremented <see cref="Envelope{T}.DeliveryCount"/>.
    /// This test is <c>Explicit</c> because it requires a real broker configured with a short
    /// lock timeout, and it must wait for that timeout to elapse before asserting redelivery.
    /// Run with <c>--explicit on</c> or <c>--explicit only</c>.
    /// </summary>
    [Fact(Explicit = true, Timeout = 5 * 60 * 1000)]
    public async Task UnsettledEnvelopeIsRedeliveredAfterLockExpiry()
    {
        Assert.SkipUnless(SupportsAutomaticRedelivery,
            "This backend does not support automatic lock-expiry redelivery (in-memory channel has no lock concept).");

        var host = await CreateInstanceAsync();
        var publisher = host.Services.GetRequiredKeyedService<Publisher<MyItemPayload, MyItem>>(TopicName);
        var subscriber = (await CreateSecondaryInstanceAsync(host)).Services
            .GetRequiredKeyedService<ISubscriber<MyItemPayload, MyItem>>(SubscriptionKey);

        await publisher.Publish(new MyItem(99)).SendAsync(TestContext.Current.CancellationToken);

        // Hold the enumerator open so the broker keeps the lock alive — disposing it would
        // abandon the message immediately rather than letting the lock time out naturally.
        var ct = TestContext.Current.CancellationToken;
        var enumerator = subscriber.SubscribeAsync(ct).GetAsyncEnumerator(ct);
        try
        {
            await enumerator.MoveNextAsync();
            var first = enumerator.Current;
            Assert.Equal(99, first.Payload.Id);
            Assert.Equal(1, first.DeliveryCount);
            // Intentionally unsettled — the enumerator stays open so the broker holds the lock.

            // Wait for the broker to reclaim the expired lock and redeliver.
            await Task.Delay(LockExpiryDelay, ct);

            // The redelivered message must arrive on the same open receiver with DeliveryCount > 1.
            await enumerator.MoveNextAsync();
            var redelivered = enumerator.Current;
            Assert.Equal(99, redelivered.Payload.Id);
            Assert.True(redelivered.DeliveryCount > 1,
                $"Expected DeliveryCount > 1 after lock expiry but got {redelivered.DeliveryCount}.");
            await redelivered.CompleteAsync(ct);
        }
        finally
        {
            await enumerator.DisposeAsync();
        }
    }

    [Fact(Timeout = 60 * 1000)]
    public async Task StoppingSubscriberDoesNotLoseMessages()
    {
        const int total = 5;
        const int firstBatch = 2;

        var host = await CreateInstanceAsync();
        var publisher = host.Services.GetRequiredKeyedService<Publisher<MyItemPayload, MyItem>>(TopicName);

        for (var i = 0; i < total; i++)
            await publisher.Publish(new MyItem(i)).SendAsync(TestContext.Current.CancellationToken);

        // First subscriber receives and completes firstBatch messages, then stops.
        var firstHost = await CreateSecondaryInstanceAsync(host);
        var firstIds = new List<int>();
        await foreach (var envelope in firstHost.Services.GetRequiredKeyedService<ISubscriber<MyItemPayload, MyItem>>(SubscriptionKey)
            .SubscribeAsync(TestContext.Current.CancellationToken))
        {
            await envelope.CompleteAsync(TestContext.Current.CancellationToken);
            firstIds.Add(envelope.Payload.Id);
            if (firstIds.Count >= firstBatch) break;
        }

        // Dispose the first subscriber's host (simulates host shutdown).
        if (!ReferenceEquals(firstHost, host))
        {
            await firstHost.StopAsync(TestContext.Current.CancellationToken);
            firstHost.Dispose();
        }

        // Remaining messages must be visible to a new subscriber.
        var secondHost = await CreateSecondaryInstanceAsync(host);
        var secondIds = new List<int>();
        await foreach (var envelope in secondHost.Services.GetRequiredKeyedService<ISubscriber<MyItemPayload, MyItem>>(SubscriptionKey)
            .SubscribeAsync(TestContext.Current.CancellationToken))
        {
            await envelope.CompleteAsync(TestContext.Current.CancellationToken);
            secondIds.Add(envelope.Payload.Id);
            if (secondIds.Count >= total - firstBatch) break;
        }

        var allIds = firstIds.Concat(secondIds).Order().ToList();
        Assert.Equal(Enumerable.Range(0, total).ToList(), allIds);
    }

    [Fact(Timeout = 60 * 1000)]
    public async Task SubscriberCompletesWhenHostDisposed()
    {
        var host = await CreateInstanceAsync();
        var subscriber = host.Services.GetRequiredKeyedService<ISubscriber<MyItemPayload, MyItem>>(SubscriptionKey);

        // Subscribe without any external cancellation — the stream must terminate on its own
        // when the host is disposed.
        var subscribeTask = Task.Run(async () =>
        {
            await foreach (var _ in subscriber.SubscribeAsync(CancellationToken.None)) { }
        }, TestContext.Current.CancellationToken);

        // Give the consumer time to register with the broker.
        await Task.Delay(200, TestContext.Current.CancellationToken);

        await host.StopAsync(TestContext.Current.CancellationToken);
        host.Dispose();

        // If the backend doesn't signal termination on disposal this will hang until the test timeout.
        await subscribeTask.WaitAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Returns config that points to a broker that is unreachable from the start, or null if
    /// this backend cannot be configured to fail on publish (e.g., in-memory channel which
    /// never fails to accept messages). When non-null, the publish exception test is executed.
    /// </summary>
    protected virtual Dictionary<string, string?>? CreateBrokerDownConfig() => null;

    /// <summary>
    /// Starts a temporary broker instance that can be stopped mid-test to simulate a
    /// mid-stream disconnect. Returns the config and a delegate to stop the broker, or null
    /// to skip the subscribe-disconnect exception test for this backend.
    /// </summary>
    protected virtual Task<(Dictionary<string, string?> config, Func<Task> stopBrokerAsync)?>
        TrySetupDroppableBrokerAsync() =>
        Task.FromResult<(Dictionary<string, string?>, Func<Task>)?>(null);

    [Fact(Timeout = 60 * 1000)]
    public async Task PublishThrowsBrokerUnavailableExceptionWhenBrokerIsDown()
    {
        var config = CreateBrokerDownConfig();
        Assert.SkipWhen(config is null, "This backend cannot be configured to fail on publish (e.g., in-memory channel).");

        var host = await BuildHostAsync(config, services => services.AddPublisher<MyItemPayload, MyItem>(TopicName));
        var publisher = host.Services.GetRequiredKeyedService<Publisher<MyItemPayload, MyItem>>(TopicName);

        var ex = await Assert.ThrowsAsync<BrokerUnavailableException>(
            () => publisher.Publish(new MyItem(1)).SendAsync(TestContext.Current.CancellationToken));

        Assert.Equal(TopicName, ex.TopicName);
        Assert.NotNull(ex.InnerException);
    }

    [Fact(Timeout = 60 * 1000)]
    public async Task SubscribeThrowsBrokerDisconnectedExceptionWhenBrokerGoesDown()
    {
        var result = await TrySetupDroppableBrokerAsync();
        Assert.SkipWhen(result is null, "This backend does not support mid-stream broker disconnection testing.");

        var (config, stopBrokerAsync) = result.Value;
        var host = await BuildHostAsync(config, services =>
        {
            services.AddPublisher<MyItemPayload, MyItem>(TopicName);
            services.AddSubscriber<MyItemPayload, MyItem>(TopicName, SubscriptionName);
        });
        var subscriber = host.Services.GetRequiredKeyedService<ISubscriber<MyItemPayload, MyItem>>(SubscriptionKey);

        var subscribeTask = Task.Run(async () =>
        {
            await foreach (var _ in subscriber.SubscribeAsync(TestContext.Current.CancellationToken)) { }
        }, TestContext.Current.CancellationToken);

        // Give the subscriber time to connect and register with the broker.
        await Task.Delay(500, TestContext.Current.CancellationToken);
        await stopBrokerAsync();

        var ex = await Assert.ThrowsAsync<BrokerDisconnectedException>(() => subscribeTask);
        Assert.Equal(TopicName, ex.TopicName);
        Assert.NotNull(ex.InnerException);
    }

    /// <summary>
    /// Injects a message with a null JSON body directly into the broker to trigger the
    /// dead-letter / discard path. Returns false if the backend does not support raw
    /// injection (e.g., in-memory channels); the test is then skipped.
    /// </summary>
    protected virtual Task<bool> TryInjectInvalidMessageAsync(string topicName) =>
        Task.FromResult(false);

    /// <summary>
    /// Injects a message with a malformed JSON body directly into the broker to trigger the
    /// deserialize-throws / dead-letter path. Returns false if the backend does not support raw
    /// injection; the test is then skipped.
    /// </summary>
    protected virtual Task<bool> TryInjectMalformedJsonMessageAsync(string topicName) =>
        Task.FromResult(false);

    [Fact(Timeout = 60 * 1000)]
    public async Task DeadLettersUndeserializableMessage()
    {
        var host = await CreateInstanceAsync();

        var injected = await TryInjectInvalidMessageAsync(TopicName);
        Assert.SkipWhen(!injected, "This backend does not support raw message injection (in-memory channel only accepts typed messages).");

        var publisher = host.Services.GetRequiredKeyedService<Publisher<MyItemPayload, MyItem>>(TopicName);
        var secondaryHost = await CreateSecondaryInstanceAsync(host);
        var subscriber = secondaryHost.Services.GetRequiredKeyedService<ISubscriber<MyItemPayload, MyItem>>(SubscriptionKey);

        // Publish a valid message after the undeserializable one.
        await publisher.Publish(new MyItem(99)).SendAsync(TestContext.Current.CancellationToken);

        // The subscriber must discard the null-body message (dead-letter / nack without requeue)
        // and deliver the valid message without blocking.
        await foreach (var envelope in subscriber.SubscribeAsync(TestContext.Current.CancellationToken))
        {
            Assert.Equal(99, envelope.Payload.Id);
            await envelope.CompleteAsync(TestContext.Current.CancellationToken);
            break;
        }
    }

    [Fact(Timeout = 60 * 1000)]
    public async Task DeadLettersMalformedJsonMessage()
    {
        var host = await CreateInstanceAsync();

        var injected = await TryInjectMalformedJsonMessageAsync(TopicName);
        Assert.SkipWhen(!injected, "This backend does not support raw message injection (in-memory channel only accepts typed messages).");

        var publisher = host.Services.GetRequiredKeyedService<Publisher<MyItemPayload, MyItem>>(TopicName);
        var secondaryHost = await CreateSecondaryInstanceAsync(host);
        var subscriber = secondaryHost.Services.GetRequiredKeyedService<ISubscriber<MyItemPayload, MyItem>>(SubscriptionKey);

        // Publish a valid message after the malformed one.
        await publisher.Publish(new MyItem(99)).SendAsync(TestContext.Current.CancellationToken);

        // The subscriber must dead-letter the malformed message (JsonException in the catch block)
        // and deliver the valid message without blocking.
        await foreach (var envelope in subscriber.SubscribeAsync(TestContext.Current.CancellationToken))
        {
            Assert.Equal(99, envelope.Payload.Id);
            await envelope.CompleteAsync(TestContext.Current.CancellationToken);
            break;
        }
    }

    [Fact(Timeout = 60 * 1000)]
    public async Task MetricsAreEmittedOnPublish()
    {
        var host = await CreateInstanceAsync();
        var meterFactory = host.Services.GetRequiredService<IMeterFactory>();
        using var collector = new MetricCollector<long>(
            meterFactory, "Bitwarden.Server.Sdk.MessageBroker", "messaging.client.published.messages");

        var publisher = host.Services.GetRequiredKeyedService<Publisher<MyItemPayload, MyItem>>(TopicName);
        await publisher.Publish(new MyItem(1)).SendAsync(TestContext.Current.CancellationToken);

        var measurement = Assert.Single(collector.GetMeasurementSnapshot());
        Assert.Equal(1, measurement.Value);
        Assert.Equal(TopicName, measurement.Tags["messaging.destination.name"]);
    }

    [Fact(Timeout = 60 * 1000)]
    public async Task MetricsAreEmittedOnConsume()
    {
        var host = await CreateInstanceAsync();
        var publisher = host.Services.GetRequiredKeyedService<Publisher<MyItemPayload, MyItem>>(TopicName);

        // The subscriber lives on the secondary host (possibly a separate DI container),
        // so the consume metric is emitted into that host's IMeterFactory.
        var secondaryHost = await CreateSecondaryInstanceAsync(host);
        var meterFactory = secondaryHost.Services.GetRequiredService<IMeterFactory>();
        using var collector = new MetricCollector<long>(
            meterFactory, "Bitwarden.Server.Sdk.MessageBroker", "messaging.client.consumed.messages");
        var subscriber = secondaryHost.Services.GetRequiredKeyedService<ISubscriber<MyItemPayload, MyItem>>(SubscriptionKey);

        await publisher.Publish(new MyItem(1)).SendAsync(TestContext.Current.CancellationToken);

        await foreach (var envelope in subscriber.SubscribeAsync(TestContext.Current.CancellationToken))
        {
            await envelope.CompleteAsync(TestContext.Current.CancellationToken);
            break;
        }

        var measurement = Assert.Single(collector.GetMeasurementSnapshot());
        Assert.Equal(1, measurement.Value);
        Assert.Equal(TopicName, measurement.Tags["messaging.destination.name"]);
        Assert.Equal(nameof(MyItem), measurement.Tags["messaging.variant.name"]);
    }

    [Fact(Timeout = 60 * 1000)]
    public async Task TracingSpansAreCreatedOnPublish()
    {
        // ConcurrentBag: ActivityStarted fires on arbitrary threads (parallel tests share the
        // global ActivitySource), so List<Activity> is not safe here.
        var activities = new ConcurrentBag<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Bitwarden.Server.Sdk.MessageBroker",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStarted = activities.Add,
        };
        ActivitySource.AddActivityListener(listener);

        var host = await CreateInstanceAsync();
        var publisher = host.Services.GetRequiredKeyedService<Publisher<MyItemPayload, MyItem>>(TopicName);

        // Snapshot before publish so activities from other parallel tests are excluded.
        var before = activities.ToHashSet(ReferenceEqualityComparer.Instance);
        await publisher.Publish(new MyItem(1)).SendAsync(TestContext.Current.CancellationToken);
        var activity = Assert.Single(activities, a => !before.Contains(a) && a.OperationName == $"{TopicName} publish");

        Assert.Equal(ActivityKind.Producer, activity.Kind);
    }

    [Fact(Timeout = 60 * 1000)]
    public async Task TracingSpansAreCreatedOnConsumeAndLinkedToProducerSpan()
    {
        var activities = new ConcurrentBag<Activity>();
        using var activityListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Bitwarden.Server.Sdk.MessageBroker",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activities.Add,
        };
        ActivitySource.AddActivityListener(activityListener);

        var host = await CreateInstanceAsync();
        var publisher = host.Services.GetRequiredKeyedService<Publisher<MyItemPayload, MyItem>>(TopicName);
        var secondaryHost = await CreateSecondaryInstanceAsync(host);
        var subscriber = secondaryHost.Services.GetRequiredKeyedService<ISubscriber<MyItemPayload, MyItem>>(SubscriptionKey);

        var before = activities.ToHashSet(ReferenceEqualityComparer.Instance);
        await publisher.Publish(new MyItem(1)).SendAsync(TestContext.Current.CancellationToken);

        await foreach (var envelope in subscriber.SubscribeAsync(TestContext.Current.CancellationToken))
        {
            await envelope.CompleteAsync(TestContext.Current.CancellationToken);
            break;
        }

        AssertConsumerSpans(activities, before);
    }

    protected virtual void AssertConsumerSpans(ConcurrentBag<Activity> activities, HashSet<object?> before)
    {
        var publishSpan = Assert.Single(activities, a => !before.Contains(a) && a.OperationName == $"{TopicName} publish");
        var receiveSpan = Assert.Single(activities, a => !before.Contains(a) && a.OperationName == $"{TopicName} receive");
        Assert.Equal(ActivityKind.Consumer, receiveSpan.Kind);
        Assert.Equal(publishSpan.SpanId, receiveSpan.ParentSpanId);
    }

    [Fact(Timeout = 60 * 1000)]
    public async Task CompetingConsumersBothHandleMessages()
    {
        const int messageCount = 20;

        var host = await CreateInstanceAsync();
        var publisher = host.Services.GetRequiredKeyedService<Publisher<MyItemPayload, MyItem>>(TopicName);
        var subscriber1 = (await CreateSecondaryInstanceAsync(host)).Services.GetRequiredKeyedService<ISubscriber<MyItemPayload, MyItem>>(SubscriptionKey);
        var subscriber2 = (await CreateSecondaryInstanceAsync(host)).Services.GetRequiredKeyedService<ISubscriber<MyItemPayload, MyItem>>(SubscriptionKey);

        var received1 = new ConcurrentBag<int>();
        var received2 = new ConcurrentBag<int>();
        var totalReceived = 0;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        async Task CollectAsync(ISubscriber<MyItemPayload, MyItem> subscriber, ConcurrentBag<int> bag)
        {
            try
            {
                await foreach (var envelope in subscriber.SubscribeAsync(cts.Token))
                {
                    await envelope.CompleteAsync(TestContext.Current.CancellationToken);
                    bag.Add(envelope.Payload.Id);
                    if (Interlocked.Increment(ref totalReceived) >= messageCount)
                        cts.Cancel();
                }
            }
            catch (OperationCanceledException) { }
        }

        var collectTask = Task.WhenAll(
            CollectAsync(subscriber1, received1),
            CollectAsync(subscriber2, received2));

        // Both subscribers must be actively waiting before messages arrive so that fair
        // competing-consumer distribution holds. Without this, the first subscriber to run
        // can drain the entire channel / exhaust the broker's prefetch window before the
        // second registers. There is no externally observable signal for "subscriber is now
        // waiting", so a small delay is the pragmatic synchronisation point here.
        await Task.Delay(200, TestContext.Current.CancellationToken);

        for (var i = 0; i < messageCount; i++)
            await publisher.Publish(new MyItem(i)).SendAsync(TestContext.Current.CancellationToken);

        await collectTask;

        var allIds = received1.Concat(received2).Order().ToList();
        Assert.Equal(Enumerable.Range(0, messageCount).ToList(), allIds);
        Assert.NotEmpty(received1);
        Assert.NotEmpty(received2);
    }

    [Fact(Timeout = 60 * 1000)]
    public async Task MultiplePublishersAndCompetingConsumersEachMessageDeliveredOnce()
    {
        const int messagesPerPublisher = 10;
        const int totalMessages = messagesPerPublisher * 2;

        var host = await CreateInstanceAsync();
        var publisher1 = host.Services.GetRequiredKeyedService<Publisher<MyItemPayload, MyItem>>(TopicName);
        var subscriber1 = host.Services.GetRequiredKeyedService<ISubscriber<MyItemPayload, MyItem>>(SubscriptionKey);

        var secondaryHost = await CreateSecondaryInstanceAsync(host);
        var publisher2 = secondaryHost.Services.GetRequiredKeyedService<Publisher<MyItemPayload, MyItem>>(TopicName);
        var subscriber2 = secondaryHost.Services.GetRequiredKeyedService<ISubscriber<MyItemPayload, MyItem>>(SubscriptionKey);

        var received1 = new ConcurrentBag<int>();
        var received2 = new ConcurrentBag<int>();
        var totalReceived = 0;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        async Task CollectAsync(ISubscriber<MyItemPayload, MyItem> subscriber, ConcurrentBag<int> bag)
        {
            try
            {
                await foreach (var envelope in subscriber.SubscribeAsync(cts.Token))
                {
                    await envelope.CompleteAsync(TestContext.Current.CancellationToken);
                    bag.Add(envelope.Payload.Id);
                    if (Interlocked.Increment(ref totalReceived) >= totalMessages)
                        cts.Cancel();
                }
            }
            catch (OperationCanceledException) { }
        }

        var collectTask = Task.WhenAll(
            CollectAsync(subscriber1, received1),
            CollectAsync(subscriber2, received2));

        // Both subscribers must be actively waiting before messages arrive — see
        // CompetingConsumersBothHandleMessages for the full explanation.
        await Task.Delay(200, TestContext.Current.CancellationToken);

        await Task.WhenAll(
            Task.Run(async () =>
            {
                for (var i = 0; i < messagesPerPublisher; i++)
                    await publisher1.Publish(new MyItem(i)).SendAsync(TestContext.Current.CancellationToken);
            }, TestContext.Current.CancellationToken),
            Task.Run(async () =>
            {
                for (var i = messagesPerPublisher; i < totalMessages; i++)
                    await publisher2.Publish(new MyItem(i)).SendAsync(TestContext.Current.CancellationToken);
            }, TestContext.Current.CancellationToken));

        await collectTask;

        // Every message from both publishers must appear exactly once across both subscribers —
        // duplicates or drops would shift the sorted sequence off Enumerable.Range(0, totalMessages).
        var allIds = received1.Concat(received2).Order().ToList();
        Assert.Equal(Enumerable.Range(0, totalMessages).ToList(), allIds);
        Assert.NotEmpty(received1);
        Assert.NotEmpty(received2);
    }

    [Fact(Timeout = 60 * 1000)]
    public async Task MultiplePublishersDeliverToSingleSubscriber()
    {
        const int messagesPerPublisher = 10;

        var host = await CreateInstanceAsync();
        var publisher1 = host.Services.GetRequiredKeyedService<Publisher<MyItemPayload, MyItem>>(TopicName);
        var subscriber = host.Services.GetRequiredKeyedService<ISubscriber<MyItemPayload, MyItem>>(SubscriptionKey);

        var secondaryHost = await CreateSecondaryInstanceAsync(host);
        var publisher2 = secondaryHost.Services.GetRequiredKeyedService<Publisher<MyItemPayload, MyItem>>(TopicName);

        var received = new ConcurrentBag<int>();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        var subscribeTask = Task.Run(async () =>
        {
            try
            {
                await foreach (var envelope in subscriber.SubscribeAsync(cts.Token))
                {
                    await envelope.CompleteAsync(TestContext.Current.CancellationToken);
                    received.Add(envelope.Payload.Id);
                    if (received.Count >= messagesPerPublisher * 2)
                        cts.Cancel();
                }
            }
            catch (OperationCanceledException) { }
        }, TestContext.Current.CancellationToken);

        // Publisher 1 sends IDs 0..9, publisher 2 sends IDs 10..19 concurrently.
        await Task.WhenAll(
            Task.Run(async () =>
            {
                for (var i = 0; i < messagesPerPublisher; i++)
                    await publisher1.Publish(new MyItem(i)).SendAsync(TestContext.Current.CancellationToken);
            }, TestContext.Current.CancellationToken),
            Task.Run(async () =>
            {
                for (var i = messagesPerPublisher; i < messagesPerPublisher * 2; i++)
                    await publisher2.Publish(new MyItem(i)).SendAsync(TestContext.Current.CancellationToken);
            }, TestContext.Current.CancellationToken));

        await subscribeTask;

        Assert.Equal(Enumerable.Range(0, messagesPerPublisher * 2).ToList(), received.Order().ToList());
    }

    // --- Pub-sub tests ---
    // A work-queue is just pub-sub with one subscriber group. These tests exercise
    // multiple independent groups receiving the same messages, which is the general case.

    [Fact(Timeout = 60 * 1000)]
    public async Task EachSubscriberGroupReceivesEveryMessage()
    {
        var host = await CreateInstanceAsync();
        var publisher = host.Services.GetRequiredKeyedService<Publisher<MyItemPayload, MyItem>>(TopicName);
        var pmHost = await CreateSecondaryInstanceAsync(host, "pm");
        var smHost = await CreateSecondaryInstanceAsync(host, "sm");
        var pmSubscriber = pmHost.Services.GetRequiredKeyedService<ISubscriber<MyItemPayload, MyItem>>($"{TopicName}/pm");
        var smSubscriber = smHost.Services.GetRequiredKeyedService<ISubscriber<MyItemPayload, MyItem>>($"{TopicName}/sm");

        var ct = TestContext.Current.CancellationToken;

        static async Task<int> ReceiveOneAsync(ISubscriber<MyItemPayload, MyItem> subscriber, CancellationToken ct)
        {
            await foreach (var envelope in subscriber.SubscribeAsync(ct))
            {
                var id = envelope.Payload.Id;
                await envelope.CompleteAsync(ct);
                return id;
            }
            throw new InvalidOperationException("No message received.");
        }

        var pmTask = ReceiveOneAsync(pmSubscriber, ct);
        var smTask = ReceiveOneAsync(smSubscriber, ct);

        // Both subscribers must be waiting before the message arrives.
        await Task.Delay(200, ct);

        await publisher.Publish(new MyItem(42)).SendAsync(ct);

        Assert.Equal(42, await pmTask);
        Assert.Equal(42, await smTask);
    }

    [Fact(Timeout = 60 * 1000)]
    public async Task AbandonedMessageIsOnlyRedeliveredToSameSubscriberGroup()
    {
        var host = await CreateInstanceAsync();
        var publisher = host.Services.GetRequiredKeyedService<Publisher<MyItemPayload, MyItem>>(TopicName);
        var pmHost = await CreateSecondaryInstanceAsync(host, "pm");
        var smHost = await CreateSecondaryInstanceAsync(host, "sm");
        var ct = TestContext.Current.CancellationToken;

        var pmSubscriber = pmHost.Services.GetRequiredKeyedService<ISubscriber<MyItemPayload, MyItem>>($"{TopicName}/pm");
        var smSubscriber = smHost.Services.GetRequiredKeyedService<ISubscriber<MyItemPayload, MyItem>>($"{TopicName}/sm");

        var smReceiveCount = 0;
        using var smCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        // sm: complete every message it receives, counting total deliveries.
        var smTask = Task.Run(async () =>
        {
            try
            {
                await foreach (var envelope in smSubscriber.SubscribeAsync(smCts.Token))
                {
                    Interlocked.Increment(ref smReceiveCount);
                    await envelope.CompleteAsync(ct);
                }
            }
            catch (OperationCanceledException) { }
        }, ct);

        // pm: abandon the first delivery, complete the redelivery.
        var pmDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pmTask = Task.Run(async () =>
        {
            var deliveries = 0;
            await foreach (var envelope in pmSubscriber.SubscribeAsync(ct))
            {
                deliveries++;
                if (deliveries == 1)
                    await envelope.RequeueAsync(cancellationToken: ct);
                else
                {
                    await envelope.CompleteAsync(ct);
                    pmDone.TrySetResult();
                    break;
                }
            }
        }, ct);

        // Give both subscribers time to register with the broker before publishing.
        await Task.Delay(200, ct);
        await publisher.Publish(new MyItem(1)).SendAsync(ct);

        // Wait until pm has completed its full abandon → redelivery → complete cycle.
        await pmDone.Task.WaitAsync(ct);

        // Give sm time to receive any spurious extras that leaked from the re-queued envelope.
        await Task.Delay(500, ct);
        await smCts.CancelAsync();
        await smTask;

        // sm should have seen exactly 1 message (the original fan-out).
        // A count of 2 indicates the abandon re-fanned the envelope to all subscriber groups.
        Assert.Equal(1, smReceiveCount);
    }

    [Fact(Timeout = 60 * 1000)]
    public async Task WithinSubscriberGroupCompetingConsumers()
    {
        const int messageCount = 20;

        var host = await CreateInstanceAsync();
        var publisher = host.Services.GetRequiredKeyedService<Publisher<MyItemPayload, MyItem>>(TopicName);
        var pmHost = await CreateSecondaryInstanceAsync(host, "pm");
        var pmNode1 = pmHost.Services.GetRequiredKeyedService<ISubscriber<MyItemPayload, MyItem>>($"{TopicName}/pm");
        var pmNode2Host = await CreateSecondaryInstanceAsync(pmHost, "pm");
        var pmNode2 = pmNode2Host.Services.GetRequiredKeyedService<ISubscriber<MyItemPayload, MyItem>>($"{TopicName}/pm");

        var received1 = new ConcurrentBag<int>();
        var received2 = new ConcurrentBag<int>();
        var totalReceived = 0;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        async Task CollectAsync(ISubscriber<MyItemPayload, MyItem> subscriber, ConcurrentBag<int> bag)
        {
            try
            {
                await foreach (var envelope in subscriber.SubscribeAsync(cts.Token))
                {
                    await envelope.CompleteAsync(TestContext.Current.CancellationToken);
                    bag.Add(envelope.Payload.Id);
                    if (Interlocked.Increment(ref totalReceived) >= messageCount)
                        cts.Cancel();
                }
            }
            catch (OperationCanceledException) { }
        }

        var collectTask = Task.WhenAll(
            CollectAsync(pmNode1, received1),
            CollectAsync(pmNode2, received2));

        await Task.Delay(200, TestContext.Current.CancellationToken);

        for (var i = 0; i < messageCount; i++)
            await publisher.Publish(new MyItem(i)).SendAsync(TestContext.Current.CancellationToken);

        await collectTask;

        var allIds = received1.Concat(received2).Order().ToList();
        Assert.Equal(Enumerable.Range(0, messageCount).ToList(), allIds);
        Assert.NotEmpty(received1);
        Assert.NotEmpty(received2);
    }
}
