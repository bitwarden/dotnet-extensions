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
    public async Task FailedMessageIsRequeued()
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

        // The first delivery throws → the base class calls RequeueAsync → message is re-queued.
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
        // A failed first delivery triggers RequeueAsync, which discards instead of re-queuing.
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
    /// Verifies that when both HandleAsync and RequeueAsync throw, the consumer swallows
    /// the requeue failure and continues processing subsequent messages.
    /// </summary>
    [Fact(Timeout = 60 * 1000)]
    public async Task ConsumerContinuesWhenRequeueThrows()
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

        // First: HandleAsync throws (FailFirstDelivery), then RequeueAsync throws.
        // The consumer must swallow the requeue failure and continue.
        await ch.Writer.WriteAsync(new ThrowingRequeueEnvelope(), TestContext.Current.CancellationToken);
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

    // -------------------------------------------------------------------------
    // Settlement-guard tests
    // -------------------------------------------------------------------------

    /// <summary>
    /// When <see cref="IMessageConsumer{T}.HandleAsync"/> returns without settling the envelope,
    /// the framework calls <see cref="Envelope{T}.CompleteAsync"/> exactly once.
    /// </summary>
    [Fact(Timeout = 60 * 1000)]
    public async Task FrameworkCompletesEnvelopeWhenHandlerDoesNotSettle()
    {
        var ch = Channel.CreateUnbounded<Envelope<MyItem>>();
        var envelope = new TrackingEnvelope(new MyItem(1));

        var host = new HostBuilder()
            .ConfigureServices(services =>
            {
                services.AddKeyedSingleton<ISubscriber<MyItem>>("test/test",
                    (_, _) => new ChannelBackedSubscriber(ch.Reader));
                services.AddMessageConsumer<MyItem, ActionConsumer>("test", "test");
                services.AddSingleton<Func<Envelope<MyItem>, CancellationToken, Task>>(
                    (_, _) => Task.CompletedTask);
                services.AddOptions<MessagingOptions>().BindConfiguration("");
            })
            .Build();

        await host.StartAsync(TestContext.Current.CancellationToken);
        await ch.Writer.WriteAsync(envelope, TestContext.Current.CancellationToken);
        await envelope.Settled.WaitAsync(TestContext.Current.CancellationToken);
        await host.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, envelope.CompleteCalls);
        Assert.Equal(0, envelope.RequeueCalls);
        Assert.Equal(0, envelope.DeadLetterCalls);
    }

    /// <summary>
    /// When <see cref="IMessageConsumer{T}.HandleAsync"/> calls
    /// <see cref="Envelope{T}.RequeueAsync"/> explicitly and returns normally, the framework's
    /// subsequent <see cref="Envelope{T}.CompleteAsync"/> is a no-op — the envelope is settled
    /// exactly once via requeue.
    /// </summary>
    [Fact(Timeout = 60 * 1000)]
    public async Task FrameworkSkipsCompleteWhenHandlerExplicitlyRequeues()
    {
        var ch = Channel.CreateUnbounded<Envelope<MyItem>>();
        var envelope = new TrackingEnvelope(new MyItem(2));

        var host = new HostBuilder()
            .ConfigureServices(services =>
            {
                services.AddKeyedSingleton<ISubscriber<MyItem>>("test/test",
                    (_, _) => new ChannelBackedSubscriber(ch.Reader));
                services.AddMessageConsumer<MyItem, ActionConsumer>("test", "test");
                services.AddSingleton<Func<Envelope<MyItem>, CancellationToken, Task>>(
                    (e, ct) => e.RequeueAsync(cancellationToken: ct));
                services.AddOptions<MessagingOptions>().BindConfiguration("");
            })
            .Build();

        await host.StartAsync(TestContext.Current.CancellationToken);
        await ch.Writer.WriteAsync(envelope, TestContext.Current.CancellationToken);
        await envelope.Settled.WaitAsync(TestContext.Current.CancellationToken);
        await host.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, envelope.CompleteCalls);
        Assert.Equal(1, envelope.RequeueCalls);
        Assert.Equal(0, envelope.DeadLetterCalls);
    }

    /// <summary>
    /// When <see cref="IMessageConsumer{T}.HandleAsync"/> calls
    /// <see cref="Envelope{T}.DeadLetterAsync"/> and returns normally, the framework's subsequent
    /// <see cref="Envelope{T}.CompleteAsync"/> is a no-op — the envelope is settled exactly once
    /// via dead-letter.
    /// </summary>
    [Fact(Timeout = 60 * 1000)]
    public async Task FrameworkSkipsCompleteWhenHandlerDeadLetters()
    {
        var ch = Channel.CreateUnbounded<Envelope<MyItem>>();
        var envelope = new TrackingEnvelope(new MyItem(3));

        var host = new HostBuilder()
            .ConfigureServices(services =>
            {
                services.AddKeyedSingleton<ISubscriber<MyItem>>("test/test",
                    (_, _) => new ChannelBackedSubscriber(ch.Reader));
                services.AddMessageConsumer<MyItem, ActionConsumer>("test", "test");
                services.AddSingleton<Func<Envelope<MyItem>, CancellationToken, Task>>(
                    (e, ct) => e.DeadLetterAsync(cancellationToken: ct));
                services.AddOptions<MessagingOptions>().BindConfiguration("");
            })
            .Build();

        await host.StartAsync(TestContext.Current.CancellationToken);
        await ch.Writer.WriteAsync(envelope, TestContext.Current.CancellationToken);
        await envelope.Settled.WaitAsync(TestContext.Current.CancellationToken);
        await host.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, envelope.CompleteCalls);
        Assert.Equal(0, envelope.RequeueCalls);
        Assert.Equal(1, envelope.DeadLetterCalls);
    }

    /// <summary>
    /// When <see cref="IMessageConsumer{T}.HandleAsync"/> throws, the framework calls
    /// <see cref="Envelope{T}.RequeueAsync"/> exactly once — <see cref="Envelope{T}.CompleteAsync"/>
    /// is never called.
    /// </summary>
    [Fact(Timeout = 60 * 1000)]
    public async Task FrameworkRequeuesEnvelopeWhenHandlerThrows()
    {
        var ch = Channel.CreateUnbounded<Envelope<MyItem>>();
        var envelope = new TrackingEnvelope(new MyItem(4));

        var host = new HostBuilder()
            .ConfigureServices(services =>
            {
                services.AddKeyedSingleton<ISubscriber<MyItem>>("test/test",
                    (_, _) => new ChannelBackedSubscriber(ch.Reader));
                services.AddMessageConsumer<MyItem, ActionConsumer>("test", "test");
                services.AddSingleton<Func<Envelope<MyItem>, CancellationToken, Task>>(
                    (_, _) => Task.FromException(new InvalidOperationException("simulated failure")));
                services.AddOptions<MessagingOptions>().BindConfiguration("");
            })
            .Build();

        await host.StartAsync(TestContext.Current.CancellationToken);
        await ch.Writer.WriteAsync(envelope, TestContext.Current.CancellationToken);
        await envelope.Settled.WaitAsync(TestContext.Current.CancellationToken);
        await host.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, envelope.CompleteCalls);
        Assert.Equal(1, envelope.RequeueCalls);
        Assert.Equal(0, envelope.DeadLetterCalls);
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

    /// <summary>
    /// Drives a consumer via a delegate so settlement-guard tests can vary handler behaviour
    /// without defining a new consumer class per scenario.
    /// </summary>
    private sealed class ActionConsumer : IMessageConsumer<MyItem>
    {
        private readonly Func<Envelope<MyItem>, CancellationToken, Task> _handler;

        public ActionConsumer(Func<Envelope<MyItem>, CancellationToken, Task> handler)
        {
            _handler = handler;
        }

        public Task HandleAsync(Envelope<MyItem> envelope, CancellationToken cancellationToken)
            => _handler(envelope, cancellationToken);
    }

    /// <summary>
    /// Records how many times each settlement path was taken so tests can assert exactly one
    /// settlement occurred via the expected method. Releases <see cref="Settled"/> on any
    /// settlement so the test can await it without a fixed delay.
    /// </summary>
    private sealed class TrackingEnvelope : Envelope<MyItem>
    {
        public int CompleteCalls;
        public int RequeueCalls;
        public int DeadLetterCalls;
        public SemaphoreSlim Settled { get; } = new(0);

        public TrackingEnvelope(MyItem message) : base(message) { }
        public override string MessageId => Guid.NewGuid().ToString();
        public override string? TraceId => null;
        public override int DeliveryCount => 1;

        protected override Task CompleteAsyncCore(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref CompleteCalls);
            Settled.Release();
            return Task.CompletedTask;
        }

        protected override Task RequeueCoreAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref RequeueCalls);
            Settled.Release();
            return Task.CompletedTask;
        }

        protected override Task DeadLetterAsyncCore(string? reason, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref DeadLetterCalls);
            Settled.Release();
            return Task.CompletedTask;
        }
    }

    /// <summary>An envelope whose <see cref="Envelope{T}.RequeueAsync"/> always throws.</summary>
    private sealed class ThrowingRequeueEnvelope : Envelope<MyItem>
    {
        public ThrowingRequeueEnvelope() : base(new MyItem(-1)) { }
        public override string MessageId => "throwing-requeue";
        public override string? TraceId => null;
        public override int DeliveryCount => 1;
        protected override Task CompleteAsyncCore(CancellationToken cancellationToken) => Task.CompletedTask;
        protected override Task RequeueCoreAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("simulated requeue failure");
        protected override Task DeadLetterAsyncCore(string? reason, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    /// <summary>A well-behaved envelope that completes and requeues without side effects.</summary>
    private sealed class GoodEnvelope : Envelope<MyItem>
    {
        public GoodEnvelope(MyItem message) : base(message) { }
        public override string MessageId => Guid.NewGuid().ToString();
        public override string? TraceId => null;
        public override int DeliveryCount => 1;
        protected override Task CompleteAsyncCore(CancellationToken cancellationToken) => Task.CompletedTask;
        protected override Task RequeueCoreAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        protected override Task DeadLetterAsyncCore(string? reason, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
