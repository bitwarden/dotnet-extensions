using Bitwarden.Server.Sdk.Caching;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bitwarden.Server.Sdk.MessageBroker.Tests;

public class SubscriberCapabilityRequesterTests
{
    private const string Topic = "topic";
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task CapabilityRequestSucceedsWhenListenerRepliesGo()
    {
        await using var harness = TestHarness.Build(reply: _ => new NegotiationAck { Go = true });
        harness.RegisterSubscriber(Topic, "sub");

        // StartAsync completes without throwing = fleet admitted the subscriber.
        await harness.Requester.StartAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CapabilityRequestThrowsWhenListenerRepliesNoGo()
    {
        await using var harness = TestHarness.Build(reply: _ => new NegotiationAck
        {
            Go = false,
            Offenders = [new NegotiationIncompatibility { InstanceId = "incompatible-pub", WireNames = new HashSet<string> { "v99" } }],
        });
        harness.RegisterSubscriber(Topic, "sub");

        var ex = await Assert.ThrowsAsync<NegotiationRejectedException>(() =>
            harness.Requester.StartAsync(TestContext.Current.CancellationToken));

        Assert.Equal(Topic, ex.DataTopic);
        Assert.Contains("incompatible-pub", ex.OffenderInstanceIds);
    }

    [Fact]
    public async Task CapabilityRequestThrowsOnTimeoutWhenNoListenerReplies()
    {
        // No listener attached — the send times out at AdmissionTimeout with no ack.
        await using var harness = TestHarness.BuildWithoutListener();
        harness.RegisterSubscriber(Topic, "sub");

        var ex = await Assert.ThrowsAsync<NegotiationTimeoutException>(() =>
            harness.Requester.StartAsync(TestContext.Current.CancellationToken));

        Assert.Equal(Topic, ex.DataTopic);
    }

    [Fact]
    public async Task CapabilityRequestLogsAndProceedsOnTimeoutWhenMarkerOptsIn()
    {
        // No listener attached, but the marker opts into graceful degradation. Instead of
        // throwing on AdmissionTimeout, the requester logs an error and returns normally.
        await using var harness = TestHarness.BuildWithoutListener();
        harness.RegisterSubscriber(Topic, "sub", proceedOnAdmissionTimeout: true);

        await harness.Requester.StartAsync(TestContext.Current.CancellationToken);

        var errors = harness.LoggedErrors.ToList();
        Assert.Single(errors);
        Assert.Contains(Topic, errors[0]);
        Assert.Contains("timed out", errors[0]);
    }

    [Fact]
    public async Task CapabilityRequestStillThrowsOnNoGoEvenWhenMarkerOptsInToDegradation()
    {
        // ProceedOnAdmissionTimeout only softens the timeout path. An explicit no-go from the
        // fleet is a real wire-name incompatibility and must still hard-fail startup.
        await using var harness = TestHarness.Build(reply: _ => new NegotiationAck
        {
            Go = false,
            Offenders = [new NegotiationIncompatibility { InstanceId = "incompatible-pub", WireNames = new HashSet<string> { "v99" } }],
        });
        harness.RegisterSubscriber(Topic, "sub", proceedOnAdmissionTimeout: true);

        await Assert.ThrowsAsync<NegotiationRejectedException>(() =>
            harness.Requester.StartAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CapabilityRequestThrowsOnDisagreeingProceedOnTimeout()
    {
        // Two AddSubscriber calls for the same topic disagree on the flag: one opts into
        // graceful degradation, the other doesn't. Strictest wins — the topic must hard-fail
        // on timeout regardless of the soft opt-in. Drives the real StartAsync path so the
        // production dedupe rule is under test.
        await using var harness = TestHarness.BuildWithoutListener();
        harness.RegisterSubscriber(Topic, "sub-a", proceedOnAdmissionTimeout: true);
        harness.RegisterSubscriber(Topic, "sub-b", proceedOnAdmissionTimeout: false);

        var ex = await Assert.ThrowsAsync<NegotiationTimeoutException>(() =>
            harness.Requester.StartAsync(TestContext.Current.CancellationToken));

        Assert.Equal(Topic, ex.DataTopic);
    }

    [Fact]
    public async Task HeartbeatRepublishesCapabilityAtEveryInterval()
    {
        // Verifies the ExecuteAsync loop wakes on each HeartbeatInterval tick and re-sends the
        // Capability so the SAC-holding listener refreshes this instance's cache entry.
        await using var harness = TestHarness.Build(reply: _ => new NegotiationAck { Go = true });
        harness.RegisterSubscriber(Topic, "sub");
        harness.ConfigureNegotiation(o => o.HeartbeatInterval = TimeSpan.FromMilliseconds(50));

        await harness.Requester.StartAsync(TestContext.Current.CancellationToken);
        await harness.WaitForCapabilitiesAsync(count: 3);
        await harness.Requester.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task HeartbeatFailureLogsWarningAndKeepsLoopAlive()
    {
        // Ack the first (startup) request as Go; drop every subsequent request so the heartbeat
        // send times out. The loop must swallow, log, and continue so the next tick has a
        // chance to recover. ProceedOnAdmissionTimeout is irrelevant on the heartbeat path —
        // every failure mode is soft regardless.
        var callCount = 0;
        await using var harness = TestHarness.Build(
            reply: _ =>
            {
                var n = Interlocked.Increment(ref callCount);
                if (n == 1) return new NegotiationAck { Go = true };
                throw new InvalidOperationException($"simulated heartbeat failure #{n}");
            });
        harness.RegisterSubscriber(Topic, "sub");
        harness.ConfigureNegotiation(o =>
        {
            o.HeartbeatInterval = TimeSpan.FromMilliseconds(50);
            o.AdmissionTimeout = TimeSpan.FromMilliseconds(100);
        });

        await harness.Requester.StartAsync(TestContext.Current.CancellationToken);
        // Warnings only land after each failing heartbeat's admission timeout fires; wait for
        // at least two of those cycles to be sure the loop survived the first failure and made
        // it to a second attempt.
        await harness.WaitForWarningsAsync(count: 2);

        Assert.All(harness.LoggedWarnings, w => Assert.Contains(Topic, w));

        await harness.Requester.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task StopAsyncIsSafeWhenStartWasSkippedForChannelBackend()
    {
        // No RabbitUri / ASB connection string configured → StartAsync short-circuits without
        // touching the sender or state. StopAsync must be a no-op regardless.
        await using var harness = TestHarness.BuildForChannelBackend();
        harness.RegisterSubscriber(Topic, "sub");

        await harness.Requester.StartAsync(TestContext.Current.CancellationToken);
        await harness.Requester.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task StopAsyncSendsSubscriberLeaveForEachAdmittedMarker()
    {
        await using var harness = TestHarness.Build(reply: _ => new NegotiationAck { Go = true });
        harness.RegisterSubscriber(Topic, "sub");

        var requester = harness.Requester;
        await requester.StartAsync(TestContext.Current.CancellationToken);
        await requester.StopAsync(TestContext.Current.CancellationToken);

        // Await the leave-observed signal — leaves are fire-and-forget from the sender's side,
        // so the listener-side observation may not land before StopAsync returns.
        await harness.WaitForLeavesAsync(count: 1);

        var leave = Assert.Single(harness.ObservedSubscriberLeaves);
        Assert.Equal(Topic, leave.DataTopic);
        Assert.Equal(harness.NegotiationOptions.InstanceId, leave.InstanceId);
    }

    /// <summary>
    /// Wires a real <see cref="SubscriberJoinRequester"/> against an in-memory
    /// <see cref="INegotiationTransport"/> so tests drive <see cref="SubscriberJoinRequester.StartAsync"/>
    /// end-to-end without a real broker. Registrations go through the real <c>AddSubscriber</c>
    /// so the production DI + marker + dedupe path is exercised.
    /// </summary>
    private sealed class TestHarness : IAsyncDisposable
    {
        private readonly ServiceCollection _services = new();
        private readonly InMemoryNegotiationBroker _broker;
        private readonly CancellationTokenSource? _listenerCts;
        private Task? _listenerRunTask;
        private readonly CapturingLoggerProvider _loggerProvider = new();
        private int _observedCapabilityCount;
        private readonly List<SubscriberLeave> _observedSubscriberLeaves = [];
        private ServiceProvider? _provider;

        private TestHarness(
            InMemoryNegotiationBroker broker,
            CancellationTokenSource? listenerCts,
            bool configureDistributedBackend = true)
        {
            _broker = broker;
            _listenerCts = listenerCts;

            // Bare-bones config for the requester's DI path: a distributed backend must appear
            // configured so StartAsync doesn't short-circuit, and NegotiationOptions must have
            // ServiceName + a short AdmissionTimeout for tests. The actual RabbitUri value is
            // never dialed — the sender factory below returns an in-memory transport.
            _services.AddLogging(b => b.AddProvider(_loggerProvider));
            // AddBitwardenCaching pulls IConfiguration via its options-configuration types.
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

        public static TestHarness Build(Func<Capability, NegotiationAck> reply)
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
                            case CapabilityRequest cr:
                                Interlocked.Increment(ref harness._observedCapabilityCount);
                                NegotiationAck ack;
                                try { ack = reply(cr.Capability); }
                                catch
                                {
                                    // Simulate a listener-side failure by leaving the reply pending;
                                    // the requester's AdmissionTimeout will then fire.
                                    continue;
                                }
                                await cr.ReplyAsync(ack, cts.Token);
                                break;
                            case SubscriberLeaveNotification sln:
                                lock (harness._observedSubscriberLeaves)
                                    harness._observedSubscriberLeaves.Add(sln.Leave);
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

        public void RegisterSubscriber(string topic, string subscription, bool proceedOnAdmissionTimeout = false)
            => _services.AddSubscriber<MyItemPayload, MyItem>(topic, subscription, proceedOnAdmissionTimeout);

        public void ConfigureNegotiation(Action<NegotiationOptions> configure)
            => _services.Configure(configure);

        public SubscriberJoinRequester Requester
        {
            get
            {
                // Override the DI-registered factory with one that returns an in-memory
                // transport sharing the harness's broker. Done lazily so subscribers registered
                // via RegisterSubscriber(...) after Build() are all in place.
                _services.AddSingleton<SubscriberNegotiationSenderFactory>(
                    _ => _ => new InMemoryNegotiationTransport(_broker, dataTopic: "sender-ignored"));
                _provider = _services.BuildServiceProvider();
                return _provider.GetRequiredService<SubscriberJoinRequester>();
            }
        }

        public int ObservedCapabilityCount => Volatile.Read(ref _observedCapabilityCount);

        public IReadOnlyList<SubscriberLeave> ObservedSubscriberLeaves
        {
            get { lock (_observedSubscriberLeaves) return [.. _observedSubscriberLeaves]; }
        }

        public NegotiationOptions NegotiationOptions
            => _provider!.GetRequiredService<Microsoft.Extensions.Options.IOptions<NegotiationOptions>>().Value;

        public async Task WaitForLeavesAsync(int count)
        {
            var deadline = DateTime.UtcNow + TestTimeout;
            while (ObservedSubscriberLeaves.Count < count)
            {
                if (DateTime.UtcNow > deadline)
                    throw new TimeoutException($"Expected at least {count} subscriber leaves, observed {ObservedSubscriberLeaves.Count}");
                await Task.Delay(10);
            }
        }

        public async Task WaitForCapabilitiesAsync(int count)
        {
            var deadline = DateTime.UtcNow + TestTimeout;
            while (ObservedCapabilityCount < count)
            {
                if (DateTime.UtcNow > deadline)
                    throw new TimeoutException($"Expected at least {count} capabilities, observed {ObservedCapabilityCount}");
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

        public IEnumerable<string> LoggedErrors => _loggerProvider.Entries
            .Where(e => e.Level == LogLevel.Error)
            .Select(e => e.Message);

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
