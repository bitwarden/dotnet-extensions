using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ZiggyCreatures.Caching.Fusion;

namespace Bitwarden.Server.Sdk.MessageBroker.Tests;

public class NegotiationListenerTests
{
    private const string Topic = "topic";

    [Fact]
    public async Task CapabilityAdmittedAndUpsertedWhenAllPublishersOverlap()
    {
        await using var harness = StartListener();
        await harness.State.UpsertPublisherAsync(Topic, Pub("pub-1", "v1", "v2"), TestContext.Current.CancellationToken);
        await harness.State.UpsertPublisherAsync(Topic, Pub("pub-2", "v2", "v3"), TestContext.Current.CancellationToken);

        var ack = await harness.Sender.SendCapabilityAsync(Cap("sub-1", "v2"), TestContext.Current.CancellationToken);

        Assert.True(ack.Go);
        Assert.Empty(ack.Offenders);
        var cached = await ToListAsync(harness.State.GetSubscribersAsync(Topic, TestContext.Current.CancellationToken));
        Assert.Equal("sub-1", cached.Single().InstanceId);
    }

    [Fact]
    public async Task CapabilityRejectedWithOffendersAndNotCachedWhenAnyPublisherLacksOverlap()
    {
        await using var harness = StartListener();
        await harness.State.UpsertPublisherAsync(Topic, Pub("pub-ok", "v1"), TestContext.Current.CancellationToken);
        await harness.State.UpsertPublisherAsync(Topic, Pub("pub-blocker", "v99"), TestContext.Current.CancellationToken);

        var ack = await harness.Sender.SendCapabilityAsync(Cap("sub-1", "v1"), TestContext.Current.CancellationToken);

        Assert.False(ack.Go);
        Assert.Equal("pub-blocker", ack.Offenders.Single().InstanceId);
        var cached = await ToListAsync(harness.State.GetSubscribersAsync(Topic, TestContext.Current.CancellationToken));
        Assert.Empty(cached);
    }

    [Fact]
    public async Task CapabilityShortCircuitsRepeatWithSameWireNames()
    {
        await using var harness = StartListener();
        Assert.True((await harness.Sender.SendCapabilityAsync(Cap("sub-1", "v1"), TestContext.Current.CancellationToken)).Go);

        // Insert a blocker AFTER the first admission. A repeat send with unchanged wire-names
        // must skip the admission scan and reply Go without consulting the new publisher.
        await harness.State.UpsertPublisherAsync(Topic, Pub("blocker", "v99"), TestContext.Current.CancellationToken);

        var ack = await harness.Sender.SendCapabilityAsync(Cap("sub-1", "v1"), TestContext.Current.CancellationToken);

        Assert.True(ack.Go);
        Assert.Empty(ack.Offenders);
    }

    [Fact]
    public async Task CapabilityReadmitsWhenWireNamesChanged()
    {
        await using var harness = StartListener();
        Assert.True((await harness.Sender.SendCapabilityAsync(Cap("sub-1", "v1"), TestContext.Current.CancellationToken)).Go);
        await harness.State.UpsertPublisherAsync(Topic, Pub("blocker", "v1"), TestContext.Current.CancellationToken);

        // Same InstanceId but different wire-names: short-circuit must NOT fire; admission
        // re-runs and rejects the changed set.
        var ack = await harness.Sender.SendCapabilityAsync(Cap("sub-1", "v2"), TestContext.Current.CancellationToken);

        Assert.False(ack.Go);
        Assert.Equal("blocker", ack.Offenders.Single().InstanceId);
    }

    [Fact]
    public async Task JoinAdmittedAndUpsertedWhenSubscribersOverlap()
    {
        await using var harness = StartListener();
        await harness.State.UpsertSubscriberAsync(Topic, Cap("sub-1", "v1", "v2"), TestContext.Current.CancellationToken);

        var ack = await harness.Sender.SendJoinAsync(Pub("pub-1", "v2"), TestContext.Current.CancellationToken);

        Assert.True(ack.Go);
        var cached = await ToListAsync(harness.State.GetPublishersAsync(Topic, TestContext.Current.CancellationToken));
        Assert.Equal("pub-1", cached.Single().InstanceId);
    }

    [Fact]
    public async Task JoinRejectedByOffendingSubscribers()
    {
        await using var harness = StartListener();
        await harness.State.UpsertSubscriberAsync(Topic, Cap("sub-old", "v1"), TestContext.Current.CancellationToken);

        var ack = await harness.Sender.SendJoinAsync(Pub("pub-1", "v99"), TestContext.Current.CancellationToken);

        Assert.False(ack.Go);
        Assert.Equal("sub-old", ack.Offenders.Single().InstanceId);
        var cached = await ToListAsync(harness.State.GetPublishersAsync(Topic, TestContext.Current.CancellationToken));
        Assert.Empty(cached);
    }

    [Fact]
    public async Task PublisherLeaveRemovesTheInstanceFromCache()
    {
        await using var harness = StartListener();
        await harness.State.UpsertPublisherAsync(Topic, Pub("pub-1", "v1"), TestContext.Current.CancellationToken);
        await harness.State.UpsertPublisherAsync(Topic, Pub("pub-2", "v1"), TestContext.Current.CancellationToken);

        await harness.Sender.SendPublisherLeaveAsync(
            new PublisherLeave { DataTopic = Topic, InstanceId = "pub-1" },
            TestContext.Current.CancellationToken);

        // Leave is fire-and-forget so give the listener a moment to process before asserting.
        await WaitUntilAsync(
            async () => (await harness.State.TryGetPublisherAsync(Topic, "pub-1", TestContext.Current.CancellationToken)) is null,
            TimeSpan.FromSeconds(5));

        Assert.Null(await harness.State.TryGetPublisherAsync(Topic, "pub-1", TestContext.Current.CancellationToken));
        Assert.NotNull(await harness.State.TryGetPublisherAsync(Topic, "pub-2", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SubscriberLeaveRemovesTheInstanceFromCache()
    {
        await using var harness = StartListener();
        await harness.State.UpsertSubscriberAsync(Topic, Cap("sub-1", "v1"), TestContext.Current.CancellationToken);
        await harness.State.UpsertSubscriberAsync(Topic, Cap("sub-2", "v1"), TestContext.Current.CancellationToken);

        await harness.Sender.SendSubscriberLeaveAsync(
            new SubscriberLeave { DataTopic = Topic, InstanceId = "sub-1" },
            TestContext.Current.CancellationToken);

        await WaitUntilAsync(
            async () => (await harness.State.TryGetSubscriberAsync(Topic, "sub-1", TestContext.Current.CancellationToken)) is null,
            TimeSpan.FromSeconds(5));

        Assert.Null(await harness.State.TryGetSubscriberAsync(Topic, "sub-1", TestContext.Current.CancellationToken));
        Assert.NotNull(await harness.State.TryGetSubscriberAsync(Topic, "sub-2", TestContext.Current.CancellationToken));
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> predicate, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await predicate()) return;
            await Task.Delay(10);
        }
        throw new TimeoutException($"Condition not met within {timeout}");
    }

    private static ListenerHarness StartListener()
    {
        var broker = new InMemoryNegotiationBroker();
        var cache = new FusionCache(new FusionCacheOptions());
        var options = Options.Create(new NegotiationOptions { ServiceName = "svc", ProcessDisplayName = "test" });
        var state = new FusionCacheNegotiationState(cache, options);
        var metrics = new NegotiationMetrics(TestMeterFactory.Instance);
        var listener = new NegotiationListener(new InMemoryNegotiationTransport(broker, dataTopic: Topic), state, metrics);
        var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var runTask = Task.Run(() => listener.RunAsync(cts.Token), CancellationToken.None);
        var sender = new InMemoryNegotiationTransport(broker, dataTopic: "sender-ignored");
        return new ListenerHarness(sender, state, cts, runTask, cache);
    }

    private static Capability Cap(string id, params string[] wireNames)
        => new() { DataTopic = Topic, InstanceId = id, WireNames = [.. wireNames] };

    private static PublisherJoin Pub(string id, params string[] wireNames)
        => new() { DataTopic = Topic, InstanceId = id, WireNames = [.. wireNames] };

    private static async Task<List<T>> ToListAsync<T>(IAsyncEnumerable<T> source)
    {
        var list = new List<T>();
        await foreach (var item in source) list.Add(item);
        return list;
    }

    private sealed class ListenerHarness(
        INegotiationTransport sender,
        INegotiationState state,
        CancellationTokenSource cts,
        Task runTask,
        FusionCache cache) : IAsyncDisposable
    {
        public INegotiationTransport Sender { get; } = sender;
        public INegotiationState State { get; } = state;

        public async ValueTask DisposeAsync()
        {
            await cts.CancelAsync();
            try { await runTask; } catch (OperationCanceledException) { /* expected */ }
            cts.Dispose();
            cache.Dispose();
        }
    }
}
