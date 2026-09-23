using Bitwarden.Server.Sdk.Caching;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bitwarden.Server.Sdk.MessageBroker.Tests;

public class PublisherJoinRequesterTests
{
    private const string Topic = "topic";
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task JoinRequestSucceedsWhenListenerRepliesGo()
    {
        await using var harness = TestHarness.Build(reply: _ => new NegotiationAck { Go = true });
        harness.RegisterPublisher(Topic);

        // StartingAsync completes without throwing = fleet admitted the requester.
        await harness.Requester.StartingAsync(TestContext.Current.CancellationToken);
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

        var ex = await Assert.ThrowsAsync<NegotiationRejectedException>(() =>
            harness.Requester.StartingAsync(TestContext.Current.CancellationToken));

        Assert.Equal(Topic, ex.DataTopic);
        Assert.Contains("downstream-blocker", ex.OffenderInstanceIds);
    }

    [Fact]
    public async Task JoinRequestThrowsOnTimeoutWhenNoListenerReplies()
    {
        // No listener attached — the send times out at AdmissionTimeout with no ack.
        await using var harness = TestHarness.BuildWithoutListener();
        harness.RegisterPublisher(Topic);

        var ex = await Assert.ThrowsAsync<NegotiationTimeoutException>(() =>
            harness.Requester.StartingAsync(TestContext.Current.CancellationToken));

        Assert.Equal(Topic, ex.DataTopic);
    }

    [Fact]
    public async Task HeartbeatRepublishesJoinAtEveryInterval()
    {
        // Verifies the ExecuteAsync loop wakes on each HeartbeatInterval tick and re-sends the
        // PublisherJoin so the SAC-holding listener refreshes this instance's cache entry.
        await using var harness = TestHarness.Build(reply: _ => new NegotiationAck { Go = true });
        harness.RegisterPublisher(Topic);
        // Short real-time interval so the test can observe multiple beats within a few hundred
        // milliseconds. FakeTimeProvider would race with ExecuteAsync's first entry into
        // Task.Delay; real time avoids that ordering hazard.
        harness.ConfigureNegotiation(o => o.HeartbeatInterval = TimeSpan.FromMilliseconds(50));

        var requester = harness.Requester;
        await requester.StartingAsync(TestContext.Current.CancellationToken);
        await requester.StartAsync(TestContext.Current.CancellationToken);

        await harness.WaitForJoinsAsync(count: 3);
        await requester.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task HeartbeatFailureLogsWarningAndKeepsLoopAlive()
    {
        // Ack the first (startup) request as Go; make every subsequent request time out by
        // dropping the reply. The loop must swallow, log, and continue so the next tick has a
        // chance to recover.
        var callCount = 0;
        await using var harness = TestHarness.Build(
            reply: _ =>
            {
                var n = Interlocked.Increment(ref callCount);
                if (n == 1) return new NegotiationAck { Go = true };
                throw new InvalidOperationException($"simulated heartbeat failure #{n}");
            });
        harness.RegisterPublisher(Topic);
        harness.ConfigureNegotiation(o =>
        {
            o.HeartbeatInterval = TimeSpan.FromMilliseconds(50);
            // Short admission timeout so failing heartbeat sends unblock quickly.
            o.AdmissionTimeout = TimeSpan.FromMilliseconds(100);
        });

        var requester = harness.Requester;
        await requester.StartingAsync(TestContext.Current.CancellationToken);
        await requester.StartAsync(TestContext.Current.CancellationToken);
        // Warnings only land after each failing heartbeat's admission timeout fires; wait for
        // at least two of those cycles to be sure the loop survived the first failure and made
        // it to a second attempt.
        await harness.WaitForWarningsAsync(count: 2);

        Assert.All(harness.LoggedWarnings, w => Assert.Contains(Topic, w));

        await requester.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task StopAsyncIsSafeWhenStartWasSkippedForChannelBackend()
    {
        // No RabbitUri / ASB connection string configured → StartingAsync short-circuits without
        // touching the sender or state. StopAsync must be a no-op regardless.
        await using var harness = TestHarness.BuildForChannelBackend();
        harness.RegisterPublisher(Topic);

        await harness.Requester.StartingAsync(TestContext.Current.CancellationToken);
        await harness.Requester.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task StopAsyncSendsPublisherLeaveForEachAdmittedMarker()
    {
        await using var harness = TestHarness.Build(reply: _ => new NegotiationAck { Go = true });
        harness.RegisterPublisher(Topic);

        var requester = harness.Requester;
        await requester.StartingAsync(TestContext.Current.CancellationToken);
        await requester.StopAsync(TestContext.Current.CancellationToken);

        // Await the leave-observed signal — leaves are fire-and-forget from the sender's side,
        // so the listener-side observation may not land before StopAsync returns.
        await harness.WaitForLeavesAsync(count: 1);

        var leave = Assert.Single(harness.ObservedPublisherLeaves);
        Assert.Equal(Topic, leave.DataTopic);
        Assert.Equal(harness.NegotiationOptions.InstanceId, leave.InstanceId);
    }

    /// <summary>
    /// Wires a real <see cref="PublisherJoinRequester"/> against an in-memory
    /// <see cref="INegotiationTransport"/> so tests drive
    /// <see cref="PublisherJoinRequester.StartingAsync"/> end-to-end without a real broker.
    /// Registrations go through the real <c>AddPublisher</c> so the production DI + marker path
    /// is exercised.
    /// </summary>
    private sealed class TestHarness : IAsyncDisposable
    {
        private readonly ServiceCollection _services = new();
        private readonly InMemoryNegotiationBroker _broker;
        private readonly CancellationTokenSource? _listenerCts;
        private Task? _listenerRunTask;
        private readonly CapturingLoggerProvider _loggerProvider = new();
        private int _observedJoinCount;
        private readonly List<PublisherLeave> _observedPublisherLeaves = [];
        private ServiceProvider? _provider;

        private TestHarness(
            InMemoryNegotiationBroker broker,
            CancellationTokenSource? listenerCts,
            bool configureDistributedBackend = true)
        {
            _broker = broker;
            _listenerCts = listenerCts;

            // Bare-bones config for the requester's DI path: a distributed backend must appear
            // configured so StartingAsync doesn't short-circuit, and NegotiationOptions must
            // have ServiceName + a short AdmissionTimeout. The actual RabbitUri value is never
            // dialed — the sender factory below returns an in-memory transport.
            _services.AddLogging(b => b.AddProvider(_loggerProvider));
            // PublisherCacheValidator requires AddBitwardenCaching whenever a distributed
            // backend is configured. Registered unconditionally so the shutdown-remove path
            // has the negotiation cache available too. AddBitwardenCaching pulls IConfiguration
            // via its options-configuration types, so wire an empty one for tests.
            _services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(
                new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
            _services.AddDistributedMemoryCache();
            _services.AddBitwardenCaching();
            // Registers IMeterFactory so NegotiationMetrics can resolve. Production callers
            // typically get this from Host.CreateApplicationBuilder; bare ServiceCollection tests
            // must add it explicitly.
            _services.AddMetrics();
            if (configureDistributedBackend)
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
            var harness = new TestHarness(broker, cts);
            harness._listenerRunTask = Task.Run(async () =>
            {
                try
                {
                    await foreach (var request in listenerTransport.ReceiveRequestsAsync(cts.Token))
                    {
                        switch (request)
                        {
                            case JoinRequest jr:
                                Interlocked.Increment(ref harness._observedJoinCount);
                                NegotiationAck ack;
                                try { ack = reply(jr.Join); }
                                catch
                                {
                                    // Simulate a listener-side failure by leaving the reply pending;
                                    // the requester's AdmissionTimeout will then fire.
                                    continue;
                                }
                                await jr.ReplyAsync(ack, cts.Token);
                                break;
                            case PublisherLeaveNotification pln:
                                lock (harness._observedPublisherLeaves)
                                    harness._observedPublisherLeaves.Add(pln.Leave);
                                break;
                        }
                    }
                }
                catch (OperationCanceledException) { }
            });
            return harness;
        }

        public static TestHarness BuildWithoutListener() => new(new InMemoryNegotiationBroker(), null);

        public static TestHarness BuildForChannelBackend()
            => new(new InMemoryNegotiationBroker(), null, configureDistributedBackend: false);

        public void RegisterPublisher(string topic)
            => _services.AddPublisher<MyItemPayload, MyItem>(topic);

        public void ConfigureNegotiation(Action<NegotiationOptions> configure)
            => _services.Configure(configure);

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

        public int ObservedJoinCount => Volatile.Read(ref _observedJoinCount);

        public IReadOnlyList<PublisherLeave> ObservedPublisherLeaves
        {
            get { lock (_observedPublisherLeaves) return [.. _observedPublisherLeaves]; }
        }

        public NegotiationOptions NegotiationOptions
            => _provider!.GetRequiredService<Microsoft.Extensions.Options.IOptions<NegotiationOptions>>().Value;

        public async Task WaitForLeavesAsync(int count)
        {
            var deadline = DateTime.UtcNow + TestTimeout;
            while (ObservedPublisherLeaves.Count < count)
            {
                if (DateTime.UtcNow > deadline)
                    throw new TimeoutException($"Expected at least {count} publisher leaves, observed {ObservedPublisherLeaves.Count}");
                await Task.Delay(10);
            }
        }

        public async Task WaitForJoinsAsync(int count)
        {
            var deadline = DateTime.UtcNow + TestTimeout;
            while (ObservedJoinCount < count)
            {
                if (DateTime.UtcNow > deadline)
                    throw new TimeoutException($"Expected at least {count} joins, observed {ObservedJoinCount}");
                await Task.Delay(10);
            }
        }

        public async Task WaitForWarningsAsync(int count)
        {
            var deadline = DateTime.UtcNow + TestTimeout;
            while (LoggedWarnings.Count() < count)
            {
                if (DateTime.UtcNow > deadline)
                    throw new TimeoutException($"Expected at least {count} warnings, observed {LoggedWarnings.Count()}");
                await Task.Delay(10);
            }
        }

        public IEnumerable<string> LoggedWarnings => _loggerProvider.Entries
            .Where(e => e.Level == LogLevel.Warning)
            .Select(e => e.Message);

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

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];
        public ILogger CreateLogger(string categoryName) => new CapturingLogger(Entries);
        public void Dispose() { }

        private sealed class CapturingLogger(List<(LogLevel, string)> sink) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                lock (sink) sink.Add((logLevel, formatter(state, exception)));
            }
        }
    }
}
