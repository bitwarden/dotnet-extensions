using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ZiggyCreatures.Caching.Fusion;

namespace Bitwarden.Server.Sdk.MessageBroker.Tests;

/// <summary>
/// Covers the ordering guarantees the negotiation listener relies on when a subscriber boots
/// before any publisher exists. The queued <c>Capability</c> drains FIFO ahead of the booting
/// publisher's own <c>PublisherJoin</c>, so the subscriber lands in the fleet cache first and
/// becomes the baseline the incoming publisher must comply with.
/// </summary>
public class AdmissionOrderingTests
{
    private const string Topic = "topic";
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task QueuedCapabilityDrainsAheadOfPublisherJoinAndCompatiblePublisherIsAdmitted()
    {
        await using var harness = new Harness();

        // 1. Subscriber sends a Capability before any listener exists. It queues in the broker
        //    and the send times out at AdmissionTimeout, mimicking a subscriber that boots first
        //    and continues past its timeout via proceedOnAdmissionTimeout.
        await harness.SubscriberSendsCapabilityAndTimesOutAsync(instanceId: "sub-1", wireNames: ["v1"]);
        Assert.Null(await harness.State.TryGetSubscriberAsync(Topic, "sub-1", TestContext.Current.CancellationToken));

        // 2. Publisher's listener starts. FIFO drain processes the queued Capability first, so
        //    the subscriber lands in the cache before any publisher does.
        harness.StartListener();
        await harness.WaitForSubscriberInCacheAsync("sub-1");

        // 3. Publisher's own PublisherJoin — compatible wire-names — is checked against the
        //    now-cached subscriber and admitted.
        var ack = await harness.PublisherSendsJoinAsync(instanceId: "pub-1", wireNames: ["v1"]);

        Assert.True(ack.Go);
        Assert.NotNull(await harness.State.TryGetPublisherAsync(Topic, "pub-1", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task QueuedCapabilityDrainsAheadOfPublisherJoinAndIncompatiblePublisherIsNoGoed()
    {
        await using var harness = new Harness();

        await harness.SubscriberSendsCapabilityAndTimesOutAsync(instanceId: "sub-1", wireNames: ["v1"]);

        harness.StartListener();
        await harness.WaitForSubscriberInCacheAsync("sub-1");

        // Publisher's wire-names do not overlap the cached subscriber's — listener replies NoGo
        // with the subscriber as the offender.
        var ack = await harness.PublisherSendsJoinAsync(instanceId: "pub-1", wireNames: ["v99"]);

        Assert.False(ack.Go);
        Assert.Equal("sub-1", Assert.Single(ack.Offenders).InstanceId);
        Assert.Null(await harness.State.TryGetPublisherAsync(Topic, "pub-1", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task QueuedJoinDrainsAheadOfCapabilityAndCompatibleSubscriberIsAdmitted()
    {
        // Mirror of the subscriber-first path: publisher's Join queues first (its Requester ran
        // before any listener acquired SAC to reply). When a listener starts, it drains the Join
        // first, upserts the publisher, and then processes a later Capability against the cached
        // publisher.
        await using var harness = new Harness();

        await harness.PublisherSendsJoinAndTimesOutAsync(instanceId: "pub-1", wireNames: ["v1"]);
        Assert.Null(await harness.State.TryGetPublisherAsync(Topic, "pub-1", TestContext.Current.CancellationToken));

        harness.StartListener();
        await harness.WaitForPublisherInCacheAsync("pub-1");

        var ack = await harness.SubscriberSendsCapabilityAsync(instanceId: "sub-1", wireNames: ["v1"]);

        Assert.True(ack.Go);
        Assert.NotNull(await harness.State.TryGetSubscriberAsync(Topic, "sub-1", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task QueuedJoinDrainsAheadOfCapabilityAndIncompatibleSubscriberIsNoGoed()
    {
        await using var harness = new Harness();

        await harness.PublisherSendsJoinAndTimesOutAsync(instanceId: "pub-1", wireNames: ["v1"]);

        harness.StartListener();
        await harness.WaitForPublisherInCacheAsync("pub-1");

        var ack = await harness.SubscriberSendsCapabilityAsync(instanceId: "sub-1", wireNames: ["v99"]);

        Assert.False(ack.Go);
        Assert.Equal("pub-1", Assert.Single(ack.Offenders).InstanceId);
        Assert.Null(await harness.State.TryGetSubscriberAsync(Topic, "sub-1", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task MultipleQueuedJoinsDrainInOrderAndAllAreAdmitted()
    {
        // Two publishers of the same service each queue a Join before any listener is up (a
        // real fleet-cold-start with two instances racing to boot). FIFO drain admits both in
        // send order — HandleJoinAsync only checks against cached subscribers, so the second
        // Join is trivially admitted against the cached-but-empty subscriber set.
        await using var harness = new Harness();

        await harness.PublisherSendsJoinAndTimesOutAsync(instanceId: "pub-1", wireNames: ["v1"]);
        await harness.PublisherSendsJoinAndTimesOutAsync(instanceId: "pub-2", wireNames: ["v1"]);

        harness.StartListener();
        await harness.WaitForPublisherInCacheAsync("pub-1");
        await harness.WaitForPublisherInCacheAsync("pub-2");
    }

    [Fact]
    public async Task MultipleQueuedCapabilitiesDrainInOrderAndAllAreAdmitted()
    {
        // Symmetric to the multi-Join case for subscribers.
        await using var harness = new Harness();

        await harness.SubscriberSendsCapabilityAndTimesOutAsync(instanceId: "sub-1", wireNames: ["v1"]);
        await harness.SubscriberSendsCapabilityAndTimesOutAsync(instanceId: "sub-2", wireNames: ["v1"]);

        harness.StartListener();
        await harness.WaitForSubscriberInCacheAsync("sub-1");
        await harness.WaitForSubscriberInCacheAsync("sub-2");
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly InMemoryNegotiationBroker _broker = new();
        private readonly FusionCache _cache = new(new FusionCacheOptions());
        private readonly FusionCacheNegotiationState _state;
        private readonly InMemoryNegotiationTransport _subscriberSender;
        private readonly InMemoryNegotiationTransport _publisherSender;
        private readonly InMemoryNegotiationTransport _listenerTransport;
        private readonly IOptions<NegotiationOptions> _options = Options.Create(new NegotiationOptions
        {
            ServiceName = "svc",
            ProcessDisplayName = "test",
            AdmissionTimeout = TimeSpan.FromMilliseconds(200),
        });
        private CancellationTokenSource? _listenerCts;
        private Task? _listenerRun;

        public Harness()
        {
            _state = new FusionCacheNegotiationState(_cache, _options);
            // Sender-side transports use a placeholder receive-topic — receive is not exercised
            // from these instances; sends route by the outgoing message's DataTopic.
            _subscriberSender = new InMemoryNegotiationTransport(_broker, dataTopic: "sub-sender-ignored");
            _publisherSender = new InMemoryNegotiationTransport(_broker, dataTopic: "pub-sender-ignored");
            _listenerTransport = new InMemoryNegotiationTransport(_broker, dataTopic: Topic);
        }

        public INegotiationState State => _state;

        public async Task SubscriberSendsCapabilityAndTimesOutAsync(string instanceId, string[] wireNames)
        {
            using var cts = new CancellationTokenSource(_options.Value.AdmissionTimeout);
            var send = _subscriberSender.SendCapabilityAsync(
                new Capability { DataTopic = Topic, InstanceId = instanceId, WireNames = [.. wireNames] },
                cts.Token);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => send);
        }

        public async Task PublisherSendsJoinAndTimesOutAsync(string instanceId, string[] wireNames)
        {
            using var cts = new CancellationTokenSource(_options.Value.AdmissionTimeout);
            var send = _publisherSender.SendJoinAsync(
                new PublisherJoin { DataTopic = Topic, InstanceId = instanceId, WireNames = [.. wireNames] },
                cts.Token);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => send);
        }

        public Task<NegotiationAck> SubscriberSendsCapabilityAsync(string instanceId, string[] wireNames)
            => _subscriberSender.SendCapabilityAsync(
                new Capability { DataTopic = Topic, InstanceId = instanceId, WireNames = [.. wireNames] },
                TestContext.Current.CancellationToken);

        public Task<NegotiationAck> PublisherSendsJoinAsync(string instanceId, string[] wireNames)
            => _publisherSender.SendJoinAsync(
                new PublisherJoin { DataTopic = Topic, InstanceId = instanceId, WireNames = [.. wireNames] },
                TestContext.Current.CancellationToken);

        public void StartListener()
        {
            var metrics = new NegotiationMetrics(TestMeterFactory.Instance);
            var listener = new NegotiationListener(_listenerTransport, _state, metrics);
            _listenerCts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            _listenerRun = Task.Run(() => listener.RunAsync(_listenerCts.Token), CancellationToken.None);
        }

        public Task WaitForSubscriberInCacheAsync(string instanceId)
            => WaitUntilAsync(
                async () => (await _state.TryGetSubscriberAsync(Topic, instanceId, TestContext.Current.CancellationToken)) is not null,
                $"Subscriber '{instanceId}' did not appear in the cache within {TestTimeout}.");

        public Task WaitForPublisherInCacheAsync(string instanceId)
            => WaitUntilAsync(
                async () => (await _state.TryGetPublisherAsync(Topic, instanceId, TestContext.Current.CancellationToken)) is not null,
                $"Publisher '{instanceId}' did not appear in the cache within {TestTimeout}.");

        private static async Task WaitUntilAsync(Func<Task<bool>> predicate, string failureMessage)
        {
            var deadline = DateTime.UtcNow + TestTimeout;
            while (DateTime.UtcNow < deadline)
            {
                if (await predicate()) return;
                await Task.Delay(10);
            }
            throw new TimeoutException(failureMessage);
        }

        public async ValueTask DisposeAsync()
        {
            if (_listenerCts is not null)
            {
                await _listenerCts.CancelAsync();
                if (_listenerRun is not null)
                {
                    try { await _listenerRun; } catch (OperationCanceledException) { /* expected */ }
                }
                _listenerCts.Dispose();
            }
            _cache.Dispose();
        }
    }
}
