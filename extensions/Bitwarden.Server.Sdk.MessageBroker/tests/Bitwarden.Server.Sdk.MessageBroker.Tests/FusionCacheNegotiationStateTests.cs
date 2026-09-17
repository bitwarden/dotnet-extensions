using Microsoft.Extensions.Options;
using ZiggyCreatures.Caching.Fusion;

namespace Bitwarden.Server.Sdk.MessageBroker.Tests;

public class FusionCacheNegotiationStateTests
{
    private static (FusionCacheNegotiationState State, IFusionCache Cache) Build(
        TimeSpan? heartbeat = null)
    {
        var cache = new FusionCache(new FusionCacheOptions());
        var options = Options.Create(new NegotiationOptions
        {
            ServiceName = "billing",
            ProcessDisplayName = "test",
            HeartbeatInterval = heartbeat ?? TimeSpan.FromMinutes(5),
        });
        return (new FusionCacheNegotiationState(cache, options), cache);
    }

    private static Capability CapabilityFor(string instanceId, params string[] wireNames)
        => new()
        {
            DataTopic = "user",
            InstanceId = instanceId,
            WireNames = new HashSet<string>(wireNames),
        };

    private static PublisherJoin PublisherFor(string instanceId, params string[] wireNames)
        => new()
        {
            DataTopic = "user",
            InstanceId = instanceId,
            WireNames = new HashSet<string>(wireNames),
        };

    private static async Task<List<T>> ToListAsync<T>(IAsyncEnumerable<T> source, CancellationToken ct)
    {
        var list = new List<T>();
        await foreach (var item in source.WithCancellation(ct)) list.Add(item);
        return list;
    }

    [Fact]
    public async Task GetPublishersIsEmptyWhenNothingWritten()
    {
        var (state, _) = Build();

        var publishers = await ToListAsync(state.GetPublishersAsync("user", TestContext.Current.CancellationToken), TestContext.Current.CancellationToken);

        Assert.Empty(publishers);
    }

    [Fact]
    public async Task GetSubscribersIsEmptyWhenNothingWritten()
    {
        var (state, _) = Build();

        var subscribers = await ToListAsync(state.GetSubscribersAsync("user", TestContext.Current.CancellationToken), TestContext.Current.CancellationToken);

        Assert.Empty(subscribers);
    }

    [Fact]
    public async Task UpsertedPublisherIsReturned()
    {
        var (state, _) = Build();

        await state.UpsertPublisherAsync("user", PublisherFor("p1", "v1", "v2"), TestContext.Current.CancellationToken);
        var publishers = await ToListAsync(state.GetPublishersAsync("user", TestContext.Current.CancellationToken), TestContext.Current.CancellationToken);

        var single = Assert.Single(publishers);
        Assert.Equal("p1", single.InstanceId);
        Assert.True(single.WireNames.SetEquals(["v1", "v2"]));
    }

    [Fact]
    public async Task UpsertedSubscriberIsReturned()
    {
        var (state, _) = Build();

        await state.UpsertSubscriberAsync("user", CapabilityFor("s1", "v1"), TestContext.Current.CancellationToken);
        var subscribers = await ToListAsync(state.GetSubscribersAsync("user", TestContext.Current.CancellationToken), TestContext.Current.CancellationToken);

        var single = Assert.Single(subscribers);
        Assert.Equal("s1", single.InstanceId);
    }

    [Fact]
    public async Task TryGetPublisherReturnsNullWhenAbsent()
    {
        var (state, _) = Build();

        Assert.Null(await state.TryGetPublisherAsync("user", "missing", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TryGetSubscriberReturnsNullWhenAbsent()
    {
        var (state, _) = Build();

        Assert.Null(await state.TryGetSubscriberAsync("user", "missing", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TryGetPublisherReturnsUpsertedEntry()
    {
        var (state, _) = Build();
        await state.UpsertPublisherAsync("user", PublisherFor("p1", "v1", "v2"), TestContext.Current.CancellationToken);

        var found = await state.TryGetPublisherAsync("user", "p1", TestContext.Current.CancellationToken);

        Assert.NotNull(found);
        Assert.True(found.WireNames.SetEquals(["v1", "v2"]));
    }

    [Fact]
    public async Task TryGetSubscriberReturnsUpsertedEntry()
    {
        var (state, _) = Build();
        await state.UpsertSubscriberAsync("user", CapabilityFor("s1", "v1"), TestContext.Current.CancellationToken);

        var found = await state.TryGetSubscriberAsync("user", "s1", TestContext.Current.CancellationToken);

        Assert.NotNull(found);
        Assert.Equal("s1", found.InstanceId);
    }

    [Fact]
    public async Task PublishersAndSubscribersAreReturnedIndependently()
    {
        var (state, _) = Build();

        await state.UpsertPublisherAsync("user", PublisherFor("p1", "v1"), TestContext.Current.CancellationToken);
        await state.UpsertSubscriberAsync("user", CapabilityFor("s1", "v1"), TestContext.Current.CancellationToken);
        await state.UpsertSubscriberAsync("user", CapabilityFor("s2", "v2"), TestContext.Current.CancellationToken);

        var publishers = await ToListAsync(state.GetPublishersAsync("user", TestContext.Current.CancellationToken), TestContext.Current.CancellationToken);
        var subscribers = await ToListAsync(state.GetSubscribersAsync("user", TestContext.Current.CancellationToken), TestContext.Current.CancellationToken);

        Assert.Single(publishers);
        Assert.Equal(2, subscribers.Count);
    }

    [Fact]
    public async Task RemovedPublisherIsAbsent()
    {
        var (state, _) = Build();
        await state.UpsertPublisherAsync("user", PublisherFor("p1", "v1"), TestContext.Current.CancellationToken);
        await state.UpsertPublisherAsync("user", PublisherFor("p2", "v2"), TestContext.Current.CancellationToken);

        await state.RemovePublisherAsync("user", "p1", TestContext.Current.CancellationToken);
        var publishers = await ToListAsync(state.GetPublishersAsync("user", TestContext.Current.CancellationToken), TestContext.Current.CancellationToken);

        var single = Assert.Single(publishers);
        Assert.Equal("p2", single.InstanceId);
    }

    [Fact]
    public async Task RemovedSubscriberIsAbsent()
    {
        var (state, _) = Build();
        await state.UpsertSubscriberAsync("user", CapabilityFor("s1", "v1"), TestContext.Current.CancellationToken);

        await state.RemoveSubscriberAsync("user", "s1", TestContext.Current.CancellationToken);
        var subscribers = await ToListAsync(state.GetSubscribersAsync("user", TestContext.Current.CancellationToken), TestContext.Current.CancellationToken);

        Assert.Empty(subscribers);
    }

    [Fact]
    public async Task DifferentTopicsHaveIndependentEntries()
    {
        var (state, _) = Build();

        await state.UpsertPublisherAsync("user", PublisherFor("p-user", "v1"), TestContext.Current.CancellationToken);
        await state.UpsertPublisherAsync("order", PublisherFor("p-order", "v2"), TestContext.Current.CancellationToken);

        var userPublishers = await ToListAsync(state.GetPublishersAsync("user", TestContext.Current.CancellationToken), TestContext.Current.CancellationToken);
        var orderPublishers = await ToListAsync(state.GetPublishersAsync("order", TestContext.Current.CancellationToken), TestContext.Current.CancellationToken);

        Assert.Equal("p-user", Assert.Single(userPublishers).InstanceId);
        Assert.Equal("p-order", Assert.Single(orderPublishers).InstanceId);
    }

    [Fact]
    public async Task ReadPrunesExpiredIdsFromTheManifest()
    {
        // Register an instance, let it expire, then read. Manifest lists it before the read
        // (manifest has no TTL) and does not after — prune-on-read removed it.
        var (state, cache) = Build(heartbeat: TimeSpan.FromMilliseconds(100));

        await state.UpsertPublisherAsync("user", PublisherFor("p1", "v1"), TestContext.Current.CancellationToken);

        var manifestBefore = await cache.GetOrDefaultAsync<HashSet<string>?>(
            "negotiation/user/manifest/publishers",
            defaultValue: null,
            token: TestContext.Current.CancellationToken);
        Assert.NotNull(manifestBefore);
        Assert.Contains("p1", manifestBefore);

        await Task.Delay(TimeSpan.FromMilliseconds(300), TestContext.Current.CancellationToken);
        var publishers = await ToListAsync(state.GetPublishersAsync("user", TestContext.Current.CancellationToken), TestContext.Current.CancellationToken);
        Assert.Empty(publishers);

        var manifestAfter = await cache.GetOrDefaultAsync<HashSet<string>?>(
            "negotiation/user/manifest/publishers",
            defaultValue: null,
            token: TestContext.Current.CancellationToken);
        Assert.NotNull(manifestAfter);
        Assert.DoesNotContain("p1", manifestAfter);
    }

    [Fact]
    public async Task EarlyBreakStillPrunesObservedDeadIds()
    {
        // Consumer breaks out of the iteration before seeing every id. The finally block in
        // the async iterator still runs, so any dead ids observed before the break are pruned.
        var (state, cache) = Build(heartbeat: TimeSpan.FromMilliseconds(200));

        // Set up manifest containing one dead and one live id. Dead first so the consumer
        // observes it before it can break.
        await state.UpsertPublisherAsync("user", PublisherFor("dead", "v1"), TestContext.Current.CancellationToken);
        await state.UpsertPublisherAsync("user", PublisherFor("live", "v2"), TestContext.Current.CancellationToken);
        await Task.Delay(TimeSpan.FromMilliseconds(150), TestContext.Current.CancellationToken);
        await state.UpsertPublisherAsync("user", PublisherFor("live", "v2"), TestContext.Current.CancellationToken);
        await Task.Delay(TimeSpan.FromMilliseconds(150), TestContext.Current.CancellationToken);

        // Iterate but break after the first live entry.
        await foreach (var publisher in state.GetPublishersAsync("user", TestContext.Current.CancellationToken))
        {
            _ = publisher;
            break;
        }

        var manifestAfter = await cache.GetOrDefaultAsync<HashSet<string>?>(
            "negotiation/user/manifest/publishers",
            defaultValue: null,
            token: TestContext.Current.CancellationToken);
        Assert.NotNull(manifestAfter);
        Assert.DoesNotContain("dead", manifestAfter);
    }
}
