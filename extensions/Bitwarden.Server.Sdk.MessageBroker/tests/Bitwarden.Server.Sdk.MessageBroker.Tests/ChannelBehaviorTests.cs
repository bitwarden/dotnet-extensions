using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Bitwarden.Server.Sdk.MessageBroker.Tests;

public class ChannelBehaviorTests : BehaviorTests
{
    // Unique per test instance so parallel runs don't share ActivitySource operation names
    // or ChannelTopic registrations, eliminating the need for a serializing [Collection].
    private readonly string _topicName = Guid.NewGuid().ToString("N")[..8];
    protected override string TopicName => _topicName;

    protected override Dictionary<string, string?> CreateConfig() => [];

    // The in-memory channel has no lock concept — messages are consumed from the channel and
    // never automatically redelivered if the consumer doesn't settle them.
    protected override bool SupportsAutomaticRedelivery => false;

    // Disposing the enumerator without settling simply discards the message for the channel
    // backend — there is no lock to release and no broker to requeue it.
    protected override bool SupportsDisposalRequeue => false;

    // The in-memory channel scheduler may deliver all messages to the first-scheduled consumer
    // rather than interleaving across both, making fair distribution non-deterministic. Only
    // the safety invariant (no duplicate or dropped messages) is verified for this backend.
    protected override void AssertFairDistribution(ConcurrentBag<int> received1, ConcurrentBag<int> received2) { }

    // All subscribers must be registered when the host is built — the in-memory channel
    // cannot add subscriptions after startup. Pre-register the default subscription and
    // the two pub-sub groups so CreateSecondaryInstanceAsync can return the same host.
    protected override Task<IHost> CreateInstanceAsync() => BuildHostAsync(services =>
    {
        services.AddPublisher<MyItem>(TopicName);
        services.AddSubscriber<MyItem>(TopicName, SubscriptionName);
        services.AddSubscriber<MyItem>(TopicName, "pm");
        services.AddSubscriber<MyItem>(TopicName, "sm");
    });

    // Channel does not support out-of-process communication; all participants share one host.
    protected override Task<IHost> CreateSecondaryInstanceAsync(IHost originalHost, string? subscriptionName = null) =>
        Task.FromResult(originalHost);

    [Fact(Timeout = 60 * 1000)]
    public async Task BufferedMessagesAreDrainedOnShutdown()
    {
        const int messageCount = 10;

        var host = await CreateInstanceAsync();
        var publisher = host.Services.GetRequiredKeyedService<IPublisher<MyItem>>(TopicName);
        var subscriber = host.Services.GetRequiredKeyedService<ISubscriber<MyItem>>(SubscriptionKey);

        // Fill the channel before the subscriber starts so all messages sit buffered.
        for (var i = 0; i < messageCount; i++)
            await publisher.PublishAsync(new MyItem(i), TestContext.Current.CancellationToken);

        // Iterate without an external cancellation token — the enumerable must yield every
        // buffered message before completing when ApplicationStopping fires.
        var received = new List<int>();
        var subscribeTask = Task.Run(async () =>
        {
            await foreach (var envelope in subscriber.SubscribeAsync(CancellationToken.None))
            {
                await envelope.CompleteAsync();
                received.Add(envelope.Message.Id);
            }
        }, TestContext.Current.CancellationToken);

        // StopAsync fires ApplicationStopping, which completes the channel writers.
        // The subscriber must drain all buffered messages before the enumerable ends.
        await host.StopAsync(TestContext.Current.CancellationToken);
        await subscribeTask.WaitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(messageCount, received.Count);
    }

    [Fact(Timeout = 60 * 1000)]
    public async Task QueueDepthGaugeTracksBufferedMessages()
    {
        const int messageCount = 5;

        // Use only the default subscription so the gauge reports a single subscription's
        // depth rather than the sum of all pre-registered subscriptions.
        var host = await BuildHostAsync(services =>
        {
            services.AddPublisher<MyItem>(TopicName);
            services.AddSubscriber<MyItem>(TopicName, SubscriptionName);
        });
        var publisher = host.Services.GetRequiredKeyedService<IPublisher<MyItem>>(TopicName);
        var subscriber = host.Services.GetRequiredKeyedService<ISubscriber<MyItem>>(SubscriptionKey);

        for (var i = 0; i < messageCount; i++)
            await publisher.PublishAsync(new MyItem(i), TestContext.Current.CancellationToken);

        Assert.Equal(messageCount, ReadQueueDepth());

        var received = 0;
        await foreach (var envelope in subscriber.SubscribeAsync(TestContext.Current.CancellationToken))
        {
            await envelope.CompleteAsync(TestContext.Current.CancellationToken);
            if (++received >= messageCount) break;
        }

        Assert.Equal(0L, ReadQueueDepth());

        long ReadQueueDepth()
        {
            long depth = 0;
            using var meterListener = new MeterListener();
            meterListener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == "Bitwarden.Server.Sdk.MessageBroker" &&
                    instrument.Name == "messaging.channel.queued.messages")
                    listener.EnableMeasurementEvents(instrument);
            };
            meterListener.SetMeasurementEventCallback<long>((_, measurement, _, _) => depth = measurement);
            meterListener.Start();
            meterListener.RecordObservableInstruments();
            return depth;
        }
    }

    /// <summary>
    /// Verifies that receiving an envelope via <see cref="ISubscriber{T}"/> without calling any
    /// settlement method silently consumes it — the message is not requeued and does not
    /// reappear. Contrast with <see cref="BehaviorTests.AbandonedMessageIsRedelivered"/> which
    /// shows that an explicit <see cref="Envelope{T}.RequeueAsync"/> call does reappear.
    /// </summary>
    [Fact(Timeout = 60 * 1000)]
    public async Task UnsettledEnvelopeIsNotRequeued()
    {
        var host = await CreateInstanceAsync();
        var publisher = host.Services.GetRequiredKeyedService<IPublisher<MyItem>>(TopicName);
        var subscriber = host.Services.GetRequiredKeyedService<ISubscriber<MyItem>>(SubscriptionKey);

        await publisher.PublishAsync(new MyItem(1), TestContext.Current.CancellationToken);
        await publisher.PublishAsync(new MyItem(2), TestContext.Current.CancellationToken);

        // Receive the first without calling any settlement method.
        await foreach (var envelope in subscriber.SubscribeAsync(TestContext.Current.CancellationToken))
        {
            Assert.Equal(1, envelope.Message.Id);
            // Intentionally unsettled — no CompleteAsync, RequeueAsync, or DeadLetterAsync.
            break;
        }

        // The second message must be next; message 1 must not reappear.
        await foreach (var envelope in subscriber.SubscribeAsync(TestContext.Current.CancellationToken))
        {
            Assert.Equal(2, envelope.Message.Id);
            await envelope.CompleteAsync(TestContext.Current.CancellationToken);
            break;
        }
    }

    /// <summary>
    /// Verifies that <see cref="Envelope{T}.DeadLetterAsync"/> discards the message — it does
    /// not reappear, in contrast to <see cref="Envelope{T}.RequeueAsync"/>.
    /// </summary>
    [Fact(Timeout = 60 * 1000)]
    public async Task DeadLetteredEnvelopeIsNotRequeued()
    {
        var host = await CreateInstanceAsync();
        var publisher = host.Services.GetRequiredKeyedService<IPublisher<MyItem>>(TopicName);
        var subscriber = host.Services.GetRequiredKeyedService<ISubscriber<MyItem>>(SubscriptionKey);

        await publisher.PublishAsync(new MyItem(1), TestContext.Current.CancellationToken);
        await publisher.PublishAsync(new MyItem(2), TestContext.Current.CancellationToken);

        // Receive and dead-letter the first.
        await foreach (var envelope in subscriber.SubscribeAsync(TestContext.Current.CancellationToken))
        {
            Assert.Equal(1, envelope.Message.Id);
            await envelope.DeadLetterAsync(cancellationToken: TestContext.Current.CancellationToken);
            break;
        }

        // The second message must be next; dead-lettered message 1 must not reappear.
        await foreach (var envelope in subscriber.SubscribeAsync(TestContext.Current.CancellationToken))
        {
            Assert.Equal(2, envelope.Message.Id);
            await envelope.CompleteAsync(TestContext.Current.CancellationToken);
            break;
        }
    }

    [Fact(Timeout = 60 * 1000)]
    public async Task UserCancellationDoesNotDrainBufferedMessages()
    {
        const int messageCount = 5;

        var host = await CreateInstanceAsync();
        var publisher = host.Services.GetRequiredKeyedService<IPublisher<MyItem>>(TopicName);
        var subscriber = host.Services.GetRequiredKeyedService<ISubscriber<MyItem>>(SubscriptionKey);

        // Fill the channel with messages before the subscriber starts.
        for (var i = 0; i < messageCount; i++)
            await publisher.PublishAsync(new MyItem(i), TestContext.Current.CancellationToken);

        // Cancel immediately — a user-owned token unrelated to ApplicationStopping.
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var received = new List<int>();
        try
        {
            await foreach (var envelope in subscriber.SubscribeAsync(cts.Token))
            {
                await envelope.CompleteAsync(TestContext.Current.CancellationToken);
                received.Add(envelope.Message.Id);
            }
        }
        catch (OperationCanceledException) { }

        // The channel is still open (host not stopped), so buffered messages must not be
        // drained — they remain for the next consumer.
        Assert.Empty(received);
    }
}
