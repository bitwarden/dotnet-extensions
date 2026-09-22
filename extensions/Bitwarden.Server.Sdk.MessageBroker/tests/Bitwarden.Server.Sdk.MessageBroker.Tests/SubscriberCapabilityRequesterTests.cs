using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bitwarden.Server.Sdk.MessageBroker.Tests;

public class SubscriberCapabilityRequesterTests
{
    private const string Topic = "topic";

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

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.Requester.StartAsync(TestContext.Current.CancellationToken));

        Assert.Contains("incompatible-pub", ex.Message);
    }

    [Fact]
    public async Task CapabilityRequestThrowsOnTimeoutWhenNoListenerReplies()
    {
        // No listener attached — the send times out at AdmissionTimeout with no ack.
        await using var harness = TestHarness.BuildWithoutListener();
        harness.RegisterSubscriber(Topic, "sub");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.Requester.StartAsync(TestContext.Current.CancellationToken));

        Assert.Contains("timed out", ex.Message);
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

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
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

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.Requester.StartAsync(TestContext.Current.CancellationToken));

        Assert.Contains("timed out", ex.Message);
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
        private readonly Task? _listenerRunTask;
        private readonly CapturingLoggerProvider _loggerProvider = new();
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
            // ServiceName + a short AdmissionTimeout for tests. The actual RabbitUri value is
            // never dialed — the sender factory below returns an in-memory transport.
            _services.AddLogging(b => b.AddProvider(_loggerProvider));
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
            var runTask = Task.Run(async () =>
            {
                try
                {
                    await foreach (var request in listenerTransport.ReceiveRequestsAsync(cts.Token))
                    {
                        if (request is CapabilityRequest cr)
                            await cr.ReplyAsync(reply(cr.Capability), cts.Token);
                    }
                }
                catch (OperationCanceledException) { }
            });
            return new TestHarness(broker, cts, runTask);
        }

        public static TestHarness BuildWithoutListener() => new(new InMemoryNegotiationBroker(), null, null);

        public void RegisterSubscriber(string topic, string subscription, bool proceedOnAdmissionTimeout = false)
            => _services.AddSubscriber<MyItemPayload, MyItem>(topic, subscription, proceedOnAdmissionTimeout);

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

        public IEnumerable<string> LoggedErrors => _loggerProvider.Entries
            .Where(e => e.Level == LogLevel.Error)
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
                => sink.Add((logLevel, formatter(state, exception)));
        }
    }
}
