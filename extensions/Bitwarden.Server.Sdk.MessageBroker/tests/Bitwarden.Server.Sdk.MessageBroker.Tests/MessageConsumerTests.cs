using System.Threading.Channels;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Bitwarden.Server.Sdk.MessageBroker.Tests;

[Collection("InMemory")]
public class MessageConsumerTests
{
    [Fact(Timeout = 60 * 1000)]
    public async Task ConsumerHandlesMessages()
    {
        var state = new ConsumerState();

        var host = new HostBuilder()
            .ConfigureServices(services =>
            {
                services.AddPublisher<MyItem>("test");
                services.AddMessageConsumer<MyItem, SignalingConsumer>("test", "test");
                services.AddSingleton(state);
                services.AddOptions<MessagingOptions>().BindConfiguration("");
            })
            .Build();

        await host.StartAsync(TestContext.Current.CancellationToken);

        var publisher = host.Services.GetRequiredKeyedService<IPublisher<MyItem>>("test");
        await publisher.PublishAsync(new MyItem(42), TestContext.Current.CancellationToken);

        await state.Received.WaitAsync(TestContext.Current.CancellationToken);

        await host.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal([42], state.Ids);
    }

    [Fact(Timeout = 60 * 1000)]
    public async Task FailedMessageIsAbandoned()
    {
        var state = new ConsumerState { FailFirstDelivery = true };

        var host = new HostBuilder()
            .ConfigureServices(services =>
            {
                services.AddPublisher<MyItem>("test");
                services.AddMessageConsumer<MyItem, SignalingConsumer>("test", "test");
                services.AddSingleton(state);
                services.AddOptions<MessagingOptions>().BindConfiguration("");
            })
            .Build();

        await host.StartAsync(TestContext.Current.CancellationToken);

        var publisher = host.Services.GetRequiredKeyedService<IPublisher<MyItem>>("test");
        await publisher.PublishAsync(new MyItem(7), TestContext.Current.CancellationToken);

        // The first delivery throws → the base class calls AbandonAsync → message is re-queued.
        // The second delivery succeeds → the semaphore is released.
        await state.Received.WaitAsync(TestContext.Current.CancellationToken);

        await host.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, state.AttemptCount);
        Assert.Equal([7], state.Ids);
    }

    [Fact(Timeout = 60 * 1000)]
    public async Task ConsumerIsResolvableByType()
    {
        var host = new HostBuilder()
            .ConfigureServices(services =>
            {
                services.AddPublisher<MyItem>("test");
                services.AddMessageConsumer<MyItem, SignalingConsumer>("test", "test");
                services.AddSingleton(new ConsumerState());
                services.AddOptions<MessagingOptions>().BindConfiguration("");
            })
            .Build();

        await host.StartAsync(TestContext.Current.CancellationToken);

        // AddMessageConsumer registers TConsumer as a singleton so tests can inspect it.
        var consumer = host.Services.GetRequiredService<SignalingConsumer>();
        Assert.NotNull(consumer);

        await host.StopAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Verifies that a message whose delivery count equals MaxDeliveryCount is discarded
    /// (not re-queued), and subsequent messages are still processed normally.
    /// </summary>
    [Fact(Timeout = 60 * 1000)]
    public async Task MessageExceedingMaxDeliveryCountIsDiscarded()
    {
        // MaxDeliveryCount=1: the first (and only) delivery is also the last.
        // A failed first delivery triggers AbandonAsync, which discards instead of re-queuing.
        var state = new ConsumerState { FailFirstDelivery = true };

        var host = new HostBuilder()
            .ConfigureAppConfiguration(c => c.AddInMemoryCollection(
                new Dictionary<string, string?> { { "MaxDeliveryCount", "1" } }))
            .ConfigureServices(services =>
            {
                services.AddPublisher<MyItem>("test");
                services.AddMessageConsumer<MyItem, SignalingConsumer>("test", "test");
                services.AddSingleton(state);
                services.AddOptions<MessagingOptions>().BindConfiguration("");
            })
            .Build();

        await host.StartAsync(TestContext.Current.CancellationToken);

        var publisher = host.Services.GetRequiredKeyedService<IPublisher<MyItem>>("test");

        // First message: fails, gets discarded rather than re-queued.
        await publisher.PublishAsync(new MyItem(42), TestContext.Current.CancellationToken);
        await Task.Delay(200, TestContext.Current.CancellationToken);

        // Second message: succeeds (AttemptCount is 2, so FailFirstDelivery no longer triggers).
        await publisher.PublishAsync(new MyItem(99), TestContext.Current.CancellationToken);
        await state.Received.WaitAsync(TestContext.Current.CancellationToken);

        await host.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal([99], state.Ids);
    }

    /// <summary>
    /// Verifies that when both HandleAsync and AbandonAsync throw, the consumer swallows
    /// the abandon failure and continues processing subsequent messages.
    /// </summary>
    [Fact(Timeout = 60 * 1000)]
    public async Task ConsumerContinuesWhenAbandonThrows()
    {
        // Feed envelopes into the consumer via a direct channel so we control the sequence.
        var ch = Channel.CreateUnbounded<Envelope<MyItem>>();
        var state = new ConsumerState { FailFirstDelivery = true };

        var host = new HostBuilder()
            .ConfigureServices(services =>
            {
                // Register the injecting subscriber before AddMessageConsumer so the TryAdd
                // inside AddSubscriber leaves our registration in place.
                services.AddKeyedSingleton<ISubscriber<MyItem>>("test/test",
                    (_, _) => new ChannelBackedSubscriber(ch.Reader));
                services.AddMessageConsumer<MyItem, SignalingConsumer>("test", "test");
                services.AddSingleton(state);
                services.AddOptions<MessagingOptions>().BindConfiguration("");
            })
            .Build();

        await host.StartAsync(TestContext.Current.CancellationToken);

        // First: HandleAsync throws (FailFirstDelivery), then AbandonAsync throws.
        // The consumer must swallow the abandon failure and continue.
        await ch.Writer.WriteAsync(new ThrowingAbandonEnvelope(), TestContext.Current.CancellationToken);
        await Task.Delay(200, TestContext.Current.CancellationToken);

        // Second: the consumer processes the good envelope normally.
        await ch.Writer.WriteAsync(new GoodEnvelope(new MyItem(55)), TestContext.Current.CancellationToken);
        await state.Received.WaitAsync(TestContext.Current.CancellationToken);

        await host.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal([55], state.Ids);
    }

    /// <summary>
    /// Verifies that <see cref="MessageConsumer{T}.ExecuteAsync"/> exits cleanly (reaches the
    /// closing brace of the method) when the subscriber's async stream completes normally
    /// rather than via cancellation.
    /// </summary>
    [Fact(Timeout = 60 * 1000)]
    public async Task ConsumerExitsCleanlyWhenSubscriberCompletes()
    {
        // Complete the writer before the host starts so ReadAllAsync returns immediately,
        // exercising the normal-return path through ExecuteAsync.
        var ch = Channel.CreateUnbounded<Envelope<MyItem>>();
        ch.Writer.Complete();

        var host = new HostBuilder()
            .ConfigureServices(services =>
            {
                // Register before AddMessageConsumer so TryAdd for the subscriber is a no-op.
                services.AddKeyedSingleton<ISubscriber<MyItem>>("test/test",
                    (_, _) => new ChannelBackedSubscriber(ch.Reader));
                services.AddMessageConsumer<MyItem, SignalingConsumer>("test", "test");
                services.AddSingleton(new ConsumerState());
                services.AddOptions<MessagingOptions>().BindConfiguration("");
            })
            .Build();

        await host.StartAsync(TestContext.Current.CancellationToken);
        // Give ExecuteAsync time to drain the completed channel and return naturally.
        await Task.Delay(100, TestContext.Current.CancellationToken);
        await host.StopAsync(TestContext.Current.CancellationToken);
    }

    private sealed class ConsumerState
    {
        public bool FailFirstDelivery { get; init; }
        public int AttemptCount;
        public List<int> Ids { get; } = [];
        public SemaphoreSlim Received { get; } = new(0);
    }

    private sealed class SignalingConsumer(ConsumerState state) : IMessageConsumer<MyItem>
    {
        public Task HandleAsync(Envelope<MyItem> envelope, CancellationToken cancellationToken)
        {
            var attempt = Interlocked.Increment(ref state.AttemptCount);
            if (state.FailFirstDelivery && attempt == 1)
                throw new InvalidOperationException("simulated first-delivery failure");

            state.Ids.Add(envelope.Message.Id);
            state.Received.Release();
            return Task.CompletedTask;
        }
    }

    /// <summary>Delivers envelopes from a <see cref="ChannelReader{T}"/> so tests can inject custom envelopes.</summary>
    private sealed class ChannelBackedSubscriber : ISubscriber<MyItem>
    {
        private readonly ChannelReader<Envelope<MyItem>> _reader;

        public ChannelBackedSubscriber(ChannelReader<Envelope<MyItem>> reader)
        {
            _reader = reader;
        }

        public IAsyncEnumerable<Envelope<MyItem>> SubscribeAsync(CancellationToken cancellationToken = default) =>
            _reader.ReadAllAsync(cancellationToken);
    }

    /// <summary>An envelope whose <see cref="Envelope{T}.AbandonAsync"/> always throws.</summary>
    private sealed class ThrowingAbandonEnvelope : Envelope<MyItem>
    {
        public ThrowingAbandonEnvelope() : base(new MyItem(-1)) { }
        public override string MessageId => "throwing-abandon";
        public override string? TraceId => null;
        public override int DeliveryCount => 1;
        protected override Task CompleteAsyncCore(CancellationToken cancellationToken) => Task.CompletedTask;
        protected override Task AbandonCoreAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("simulated abandon failure");
        protected override Task DeadLetterAsyncCore(string? reason, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    /// <summary>A well-behaved envelope that completes and abandons without side effects.</summary>
    private sealed class GoodEnvelope : Envelope<MyItem>
    {
        public GoodEnvelope(MyItem message) : base(message) { }
        public override string MessageId => Guid.NewGuid().ToString();
        public override string? TraceId => null;
        public override int DeliveryCount => 1;
        protected override Task CompleteAsyncCore(CancellationToken cancellationToken) => Task.CompletedTask;
        protected override Task AbandonCoreAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        protected override Task DeadLetterAsyncCore(string? reason, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
