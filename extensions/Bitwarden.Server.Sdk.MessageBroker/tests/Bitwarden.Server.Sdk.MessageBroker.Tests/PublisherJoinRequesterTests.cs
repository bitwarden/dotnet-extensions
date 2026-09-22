using Bitwarden.Server.Sdk.Caching;
using Microsoft.Extensions.DependencyInjection;

namespace Bitwarden.Server.Sdk.MessageBroker.Tests;

public class PublisherJoinRequesterTests
{
    private const string Topic = "topic";

    [Fact]
    public async Task JoinRequestSucceedsWhenListenerRepliesGo()
    {
        await using var harness = TestHarness.Build(reply: _ => new NegotiationAck { Go = true });
        harness.RegisterPublisher(Topic);

        // StartAsync completes without throwing = fleet admitted the requester.
        await harness.Requester.StartAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task JoinRequestThrowsWhenListenerRepliesNoGo()
    {
        await using var harness = TestHarness.Build(reply: _ => new NegotiationAck
        {
            Go = false,
            Offenders = [new NegotiationIncompatibility { InstanceId = "downstream-blocker", WireNames = new HashSet<string> { "v99" } }],
        });
        harness.RegisterPublisher(Topic);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.Requester.StartAsync(TestContext.Current.CancellationToken));

        Assert.Contains("downstream-blocker", ex.Message);
    }

    [Fact]
    public async Task JoinRequestThrowsOnTimeoutWhenNoListenerReplies()
    {
        // No listener attached — the send times out at AdmissionTimeout with no ack.
        await using var harness = TestHarness.BuildWithoutListener();
        harness.RegisterPublisher(Topic);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.Requester.StartAsync(TestContext.Current.CancellationToken));

        Assert.Contains("timed out", ex.Message);
    }

    /// <summary>
    /// Wires a real <see cref="PublisherJoinRequester"/> against an in-memory
    /// <see cref="INegotiationTransport"/> so tests drive <see cref="PublisherJoinRequester.StartAsync"/>
    /// end-to-end without a real broker. Registrations go through the real <c>AddPublisher</c>
    /// so the production DI + marker path is exercised.
    /// </summary>
    private sealed class TestHarness : IAsyncDisposable
    {
        private readonly ServiceCollection _services = new();
        private readonly InMemoryNegotiationBroker _broker;
        private readonly CancellationTokenSource? _listenerCts;
        private readonly Task? _listenerRunTask;
        private ServiceProvider? _provider;

        private TestHarness(
            InMemoryNegotiationBroker broker,
            CancellationTokenSource? listenerCts,
            Task? listenerRunTask)
        {
            _broker = broker;
            _listenerCts = listenerCts;
            _listenerRunTask = listenerRunTask;

            // Bare-bones config for the requester's DI path: a distributed backend must appear
            // configured so StartAsync doesn't short-circuit, and NegotiationOptions must have
            // ServiceName + a short AdmissionTimeout. The actual RabbitUri value is never
            // dialed — the sender factory below returns an in-memory transport.
            _services.AddLogging();
            // PublisherCacheValidator requires AddBitwardenCaching whenever a distributed
            // backend is configured, even though this test never resolves the cache.
            _services.AddDistributedMemoryCache();
            _services.AddBitwardenCaching();
            _services.Configure<MessagingOptions>(o => o.RabbitUri = "amqp://test-does-not-connect");
            _services.Configure<NegotiationOptions>(o =>
            {
                o.ServiceName = "svc";
                o.ProcessDisplayName = "test";
                o.AdmissionTimeout = TimeSpan.FromSeconds(2);
            });
        }

        public static TestHarness Build(Func<PublisherJoin, NegotiationAck> reply)
        {
            var broker = new InMemoryNegotiationBroker();
            var listenerTransport = new InMemoryNegotiationTransport(broker, dataTopic: Topic);
            var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            var runTask = Task.Run(async () =>
            {
                try
                {
                    await foreach (var request in listenerTransport.ReceiveRequestsAsync(cts.Token))
                    {
                        if (request is JoinRequest jr)
                            await jr.ReplyAsync(reply(jr.Join), cts.Token);
                    }
                }
                catch (OperationCanceledException) { }
            });
            return new TestHarness(broker, cts, runTask);
        }

        public static TestHarness BuildWithoutListener() => new(new InMemoryNegotiationBroker(), null, null);

        public void RegisterPublisher(string topic)
            => _services.AddPublisher<MyItemPayload, MyItem>(topic);

        public PublisherJoinRequester Requester
        {
            get
            {
                // Override the DI-registered factory with one that returns an in-memory
                // transport sharing the harness's broker. Done lazily so publishers registered
                // via RegisterPublisher(...) after Build() are all in place.
                _services.AddSingleton<PublisherNegotiationSenderFactory>(
                    _ => _ => new InMemoryNegotiationTransport(_broker, dataTopic: "sender-ignored"));
                _provider = _services.BuildServiceProvider();
                return _provider.GetRequiredService<PublisherJoinRequester>();
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_listenerCts is not null)
            {
                await _listenerCts.CancelAsync();
                if (_listenerRunTask is not null)
                {
                    try { await _listenerRunTask; } catch (OperationCanceledException) { }
                }
                _listenerCts.Dispose();
            }
            if (_provider is not null) await _provider.DisposeAsync();
        }
    }
}
