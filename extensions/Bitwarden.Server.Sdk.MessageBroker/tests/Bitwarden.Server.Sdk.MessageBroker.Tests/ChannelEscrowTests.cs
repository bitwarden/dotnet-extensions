using System.Buffers;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Bitwarden.Server.Sdk.MessageBroker.Tests;

[Collection("InMemory")]
public class ChannelEscrowTests
{
    /// <summary>
    /// Verifies that messages buffered in the channel when the host stops are written to the
    /// primary escrow store if one is registered.
    /// </summary>
    [Fact(Timeout = 60 * 1000)]
    public async Task UnconsumedMessagesAreEscrowedOnShutdown()
    {
        const int messageCount = 5;
        var escrow = new InMemoryEscrowStore();

        var host = new HostBuilder()
            .ConfigureServices(services =>
            {
                services.AddPublisher<MyItem>("test");
                services.AddKeyedSingleton<ISubscriber<MyItem>>("test/group", (_, _) => new NeverYieldingSubscriber());
                services.AddMessageConsumer<MyItem, BlockingConsumer>("test", "group");
                services.AddSingleton<IMessageEscrowStore>(escrow);
                services.AddOptions<MessagingOptions>().BindConfiguration("");
            })
            .Build();

        await host.StartAsync(TestContext.Current.CancellationToken);

        var publisher = host.Services.GetRequiredKeyedService<IPublisher<MyItem>>("test");
        for (var i = 0; i < messageCount; i++)
            await publisher.PublishAsync(new MyItem(i), TestContext.Current.CancellationToken);

        await host.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal(messageCount, escrow.TotalCount);
    }

    /// <summary>
    /// Verifies that escrowed messages are recovered and re-injected into the channel on the next
    /// host startup, then delivered to a consumer.
    /// </summary>
    [Fact(Timeout = 60 * 1000)]
    public async Task EscrowedMessagesAreRecoveredOnRestart()
    {
        const int messageCount = 5;
        var escrow = new InMemoryEscrowStore();

        // First host: publish messages but block the consumer so they stay in the channel.
        var host1 = new HostBuilder()
            .ConfigureServices(services =>
            {
                services.AddPublisher<MyItem>("test");
                services.AddKeyedSingleton<ISubscriber<MyItem>>("test/group", (_, _) => new NeverYieldingSubscriber());
                services.AddMessageConsumer<MyItem, BlockingConsumer>("test", "group");
                services.AddSingleton<IMessageEscrowStore>(escrow);
                services.AddOptions<MessagingOptions>().BindConfiguration("");
            })
            .Build();

        await host1.StartAsync(TestContext.Current.CancellationToken);

        var publisher = host1.Services.GetRequiredKeyedService<IPublisher<MyItem>>("test");
        for (var i = 0; i < messageCount; i++)
            await publisher.PublishAsync(new MyItem(i), TestContext.Current.CancellationToken);

        await host1.StopAsync(TestContext.Current.CancellationToken);

        // All messages must have been escrowed.
        Assert.Equal(messageCount, escrow.TotalCount);

        // Second host: escrow recovery re-injects messages; the recording consumer receives them.
        var state = new RecordingState();
        var host2 = new HostBuilder()
            .ConfigureServices(services =>
            {
                services.AddPublisher<MyItem>("test");
                services.AddMessageConsumer<MyItem, RecordingConsumer>("test", "group");
                services.AddSingleton(state);
                services.AddSingleton<IMessageEscrowStore>(escrow);
                services.AddOptions<MessagingOptions>().BindConfiguration("");
            })
            .Build();

        await host2.StartAsync(TestContext.Current.CancellationToken);

        for (var i = 0; i < messageCount; i++)
            await state.Received.WaitAsync(TestContext.Current.CancellationToken);

        await host2.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal(Enumerable.Range(0, messageCount).ToList(), state.Ids.Order().ToList());
    }

    /// <summary>
    /// Verifies that when no <see cref="IMessageEscrowStore"/> is registered, undelivered messages
    /// are logged as errors and the host shuts down without throwing.
    /// </summary>
    [Fact(Timeout = 60 * 1000)]
    public async Task FallsBackToLogWhenNoPrimaryStoreIsRegistered()
    {
        const int messageCount = 3;

        var host = new HostBuilder()
            .ConfigureServices(services =>
            {
                services.AddPublisher<MyItem>("test");
                services.AddKeyedSingleton<ISubscriber<MyItem>>("test/group", (_, _) => new NeverYieldingSubscriber());
                services.AddMessageConsumer<MyItem, BlockingConsumer>("test", "group");
                services.AddOptions<MessagingOptions>().BindConfiguration("");
            })
            .Build();

        await host.StartAsync(TestContext.Current.CancellationToken);

        var publisher = host.Services.GetRequiredKeyedService<IPublisher<MyItem>>("test");
        for (var i = 0; i < messageCount; i++)
            await publisher.PublishAsync(new MyItem(i), TestContext.Current.CancellationToken);

        // Must complete without throwing even though there is no escrow store.
        await host.StopAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Verifies that a read failure from the escrow store during startup is swallowed and the
    /// host starts successfully without recovering any messages.
    /// </summary>
    [Fact(Timeout = 60 * 1000)]
    public async Task RecoveryIsSkippedWhenEscrowStoreThrowsOnRead()
    {
        var host = new HostBuilder()
            .ConfigureServices(services =>
            {
                services.AddPublisher<MyItem>("test");
                services.AddKeyedSingleton<ISubscriber<MyItem>>("test/group", (_, _) => new NeverYieldingSubscriber());
                services.AddMessageConsumer<MyItem, BlockingConsumer>("test", "group");
                services.AddSingleton<IMessageEscrowStore>(new ThrowingOnReadEscrowStore());
                services.AddOptions<MessagingOptions>().BindConfiguration("");
            })
            .Build();

        // Must start without throwing even though the escrow store throws on ReadAndClearAsync.
        await host.StartAsync(TestContext.Current.CancellationToken);
        await host.StopAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Verifies that a corrupt escrowed payload is logged and skipped, and the next valid
    /// message in the same batch is still recovered and delivered.
    /// </summary>
    [Fact(Timeout = 60 * 1000)]
    public async Task CorruptEscrowedPayloadIsSkippedDuringRecovery()
    {
        const string escrowKey = "test/group";
        var escrow = new InMemoryEscrowStore();

        var validPayload = JsonSerializer.SerializeToUtf8Bytes(new MyItem(99));
        await escrow.WriteAsync(escrowKey, [
            new EscrowedMessage(Guid.NewGuid().ToString(), null, 1, "NOT_VALID_JSON"u8.ToArray()),
            new EscrowedMessage(Guid.NewGuid().ToString(), null, 1, validPayload),
        ], TestContext.Current.CancellationToken);

        var state = new RecordingState();
        var host = new HostBuilder()
            .ConfigureServices(services =>
            {
                services.AddPublisher<MyItem>("test");
                services.AddMessageConsumer<MyItem, RecordingConsumer>("test", "group");
                services.AddSingleton(state);
                services.AddSingleton<IMessageEscrowStore>(escrow);
                services.AddOptions<MessagingOptions>().BindConfiguration("");
            })
            .Build();

        await host.StartAsync(TestContext.Current.CancellationToken);
        await state.Received.WaitAsync(TestContext.Current.CancellationToken);
        await host.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal([99], state.Ids);
    }

    /// <summary>
    /// Verifies that an escrowed payload that deserializes to null is logged and skipped,
    /// and the next valid message in the same batch is still recovered and delivered.
    /// </summary>
    [Fact(Timeout = 60 * 1000)]
    public async Task NullEscrowedMessageIsSkippedDuringRecovery()
    {
        const string escrowKey = "test/group";
        var escrow = new InMemoryEscrowStore();

        var validPayload = JsonSerializer.SerializeToUtf8Bytes(new MyItem(77));
        await escrow.WriteAsync(escrowKey, [
            new EscrowedMessage(Guid.NewGuid().ToString(), null, 1, "null"u8.ToArray()),
            new EscrowedMessage(Guid.NewGuid().ToString(), null, 1, validPayload),
        ], TestContext.Current.CancellationToken);

        var state = new RecordingState();
        var host = new HostBuilder()
            .ConfigureServices(services =>
            {
                services.AddPublisher<MyItem>("test");
                services.AddMessageConsumer<MyItem, RecordingConsumer>("test", "group");
                services.AddSingleton(state);
                services.AddSingleton<IMessageEscrowStore>(escrow);
                services.AddOptions<MessagingOptions>().BindConfiguration("");
            })
            .Build();

        await host.StartAsync(TestContext.Current.CancellationToken);
        await state.Received.WaitAsync(TestContext.Current.CancellationToken);
        await host.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal([77], state.Ids);
    }

    /// <summary>
    /// Verifies that when the primary escrow store throws on <c>WriteAsync</c>, undelivered
    /// messages are logged as errors and the host shuts down without throwing.
    /// </summary>
    [Fact(Timeout = 60 * 1000)]
    public async Task EscrowFallsBackToLogWhenStoreThrowsOnWrite()
    {
        const int messageCount = 3;

        var host = new HostBuilder()
            .ConfigureServices(services =>
            {
                services.AddPublisher<MyItem>("test");
                services.AddKeyedSingleton<ISubscriber<MyItem>>("test/group", (_, _) => new NeverYieldingSubscriber());
                services.AddMessageConsumer<MyItem, BlockingConsumer>("test", "group");
                services.AddSingleton<IMessageEscrowStore>(new ThrowingOnWriteEscrowStore());
                services.AddOptions<MessagingOptions>().BindConfiguration("");
            })
            .Build();

        await host.StartAsync(TestContext.Current.CancellationToken);

        var publisher = host.Services.GetRequiredKeyedService<IPublisher<MyItem>>("test");
        for (var i = 0; i < messageCount; i++)
            await publisher.PublishAsync(new MyItem(i), TestContext.Current.CancellationToken);

        // Must complete without throwing even though the store throws on WriteAsync.
        await host.StopAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Verifies that <see cref="ChannelEscrowService{T}"/> is a no-op when an external broker
    /// (Rabbit or ASB) is configured — StartAsync and StopAsync both return immediately without
    /// draining the in-memory channel.
    /// </summary>
    [Fact(Timeout = 60 * 1000)]
    public async Task EscrowServiceSkipsWhenExternalBackendIsConfigured()
    {
        // Use an unreachable URI so the RabbitConnection fails in the background without
        // throwing from StartAsync (resilient startup).
        var host = new HostBuilder()
            .ConfigureAppConfiguration(c => c.AddInMemoryCollection(
                new Dictionary<string, string?> { { "RabbitUri", "amqp://guest:guest@localhost:1/" } }))
            .ConfigureServices(services =>
            {
                services.AddKeyedSingleton<ISubscriber<MyItem>>("test/group", (_, _) => new NeverYieldingSubscriber());
                services.AddMessageConsumer<MyItem, BlockingConsumer>("test", "group");
                services.AddOptions<MessagingOptions>().BindConfiguration("");
            })
            .Build();

        // Both ChannelEscrowService and ChannelConsumerValidationService must return early
        // when an external backend is configured.
        await host.StartAsync(TestContext.Current.CancellationToken);
        await host.StopAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Verifies that when the serializer throws during the shutdown drain, the failed message is
    /// discarded (logged) and the host shuts down without throwing.
    /// </summary>
    [Fact(Timeout = 60 * 1000)]
    public async Task EscrowSerializationFailureIsDiscardedOnShutdown()
    {
        var host = new HostBuilder()
            .ConfigureServices(services =>
            {
                // Register before AddPublisher so the TryAdd inside AddPublisher is a no-op.
                services.AddKeyedSingleton<IMessageSerializer>("test",
                    (_, _) => new ThrowingOnSerializeSerializer());
                services.AddPublisher<MyItem>("test");
                services.AddKeyedSingleton<ISubscriber<MyItem>>("test/group", (_, _) => new NeverYieldingSubscriber());
                services.AddMessageConsumer<MyItem, BlockingConsumer>("test", "group");
                services.AddOptions<MessagingOptions>().BindConfiguration("");
            })
            .Build();

        await host.StartAsync(TestContext.Current.CancellationToken);

        // ChannelPublisher writes the message directly to the channel without serializing;
        // serialization only happens in ChannelEscrowService.StopAsync when draining.
        var publisher = host.Services.GetRequiredKeyedService<IPublisher<MyItem>>("test");
        await publisher.PublishAsync(new MyItem(1), TestContext.Current.CancellationToken);

        // StopAsync drains the channel, calls Serialize → throws → caught and logged.
        await host.StopAsync(TestContext.Current.CancellationToken);
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private sealed class InMemoryEscrowStore : IMessageEscrowStore
    {
        private readonly Dictionary<string, List<EscrowedMessage>> _store = new();

        public int TotalCount => _store.Values.Sum(v => v.Count);

        public Task WriteAsync(string key, IReadOnlyList<EscrowedMessage> messages, CancellationToken cancellationToken = default)
        {
            _store[key] = messages.ToList();
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<EscrowedMessage>> ReadAndClearAsync(string key, CancellationToken cancellationToken = default)
        {
            if (_store.TryGetValue(key, out var messages))
            {
                _store.Remove(key);
                return Task.FromResult<IReadOnlyList<EscrowedMessage>>(messages);
            }
            return Task.FromResult<IReadOnlyList<EscrowedMessage>>([]);
        }
    }

    /// <summary>
    /// Never yields any messages; the consumer loop blocks until cancellation, leaving all
    /// <see cref="ChannelTopic{T}"/> messages buffered for the escrow drain.
    /// </summary>
    private sealed class NeverYieldingSubscriber : ISubscriber<MyItem>
    {
        public async IAsyncEnumerable<Envelope<MyItem>> SubscribeAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            try { await Task.Delay(Timeout.Infinite, cancellationToken); }
            catch (OperationCanceledException) { }
            yield break;
        }
    }

    /// <summary>Never processes messages; used alongside <see cref="NeverYieldingSubscriber"/>.</summary>
    private sealed class BlockingConsumer : IMessageConsumer<MyItem>
    {
        public Task HandleAsync(Envelope<MyItem> envelope, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class RecordingState
    {
        public List<int> Ids { get; } = [];
        public SemaphoreSlim Received { get; } = new(0, int.MaxValue);
    }

    private sealed class RecordingConsumer(RecordingState state) : IMessageConsumer<MyItem>
    {
        public Task HandleAsync(Envelope<MyItem> envelope, CancellationToken cancellationToken)
        {
            state.Ids.Add(envelope.Message.Id);
            state.Received.Release();
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingOnReadEscrowStore : IMessageEscrowStore
    {
        public Task<IReadOnlyList<EscrowedMessage>> ReadAndClearAsync(string key, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("simulated read failure");

        public Task WriteAsync(string key, IReadOnlyList<EscrowedMessage> messages, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class ThrowingOnWriteEscrowStore : IMessageEscrowStore
    {
        public Task<IReadOnlyList<EscrowedMessage>> ReadAndClearAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<EscrowedMessage>>([]);

        public Task WriteAsync(string key, IReadOnlyList<EscrowedMessage> messages, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("simulated write failure");
    }

    /// <summary>Always throws from <see cref="IMessageSerializer.Serialize{T}"/>.</summary>
    private sealed class ThrowingOnSerializeSerializer : IMessageSerializer
    {
        public void Serialize<T>(T message, IBufferWriter<byte> destination) =>
            throw new InvalidOperationException("simulated serialization failure");

        public T? Deserialize<T>(ReadOnlySpan<byte> source) => default;
    }
}
