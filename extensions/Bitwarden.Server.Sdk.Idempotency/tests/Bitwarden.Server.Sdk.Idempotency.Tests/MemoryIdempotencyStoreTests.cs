namespace Bitwarden.Server.Sdk.Idempotency.Tests;

/// <summary>
/// Keys held in this process: a claim holds its key until its lease ends, an answer for its retention, a release lets
/// go only of the write's own pending record, and the store is bounded.
/// </summary>
public sealed class MemoryIdempotencyStoreTests
{
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(2);

    [Fact]
    public async Task AClaim_HoldsTheKeyUntilItsLeaseEnds()
    {
        var clock = new Clock();
        var store = new MemoryIdempotencyStore(clock);

        Assert.Equal(ClaimOutcome.Held, (await store.ClaimAsync("k", "pending", Lease)).Outcome);
        var meanwhile = await store.ClaimAsync("k", "other", Lease);

        Assert.Equal(ClaimOutcome.Found, meanwhile.Outcome);
        Assert.Equal("pending", meanwhile.Existing);

        clock.Advance(Lease - TimeSpan.FromTicks(1));
        Assert.Equal(ClaimOutcome.Found, (await store.ClaimAsync("k", "other", Lease)).Outcome);

        clock.Advance(TimeSpan.FromTicks(1));
        Assert.Equal(ClaimOutcome.Held, (await store.ClaimAsync("k", "other", Lease)).Outcome);
    }

    [Fact]
    public async Task TwoKeys_AreHeldApart()
    {
        var store = new MemoryIdempotencyStore(new Clock());

        Assert.Equal(ClaimOutcome.Held, (await store.ClaimAsync("k", "pending", Lease)).Outcome);
        Assert.Equal(ClaimOutcome.Held, (await store.ClaimAsync("K", "pending", Lease)).Outcome);
    }

    [Fact]
    public async Task AnAnswer_IsKeptForItsRetention()
    {
        var clock = new Clock();
        var store = new MemoryIdempotencyStore(clock);
        await store.ClaimAsync("k", "pending", Lease);

        await store.CompleteAsync("k", "answer", TimeSpan.FromHours(24));
        clock.Advance(TimeSpan.FromHours(23));

        Assert.Equal("answer", (await store.ClaimAsync("k", "again", Lease)).Existing);
        clock.Advance(TimeSpan.FromHours(1));
        Assert.Equal(ClaimOutcome.Held, (await store.ClaimAsync("k", "again", Lease)).Outcome);
    }

    // Only the write that holds the key lets go of it: never another's.
    [Fact]
    public async Task Release_LetsGoOnlyOfItsOwn()
    {
        var store = new MemoryIdempotencyStore(new Clock());
        await store.ClaimAsync("k", "mine", Lease);

        await store.ReleaseAsync("k", "not-mine");
        Assert.Equal(ClaimOutcome.Found, (await store.ClaimAsync("k", "x", Lease)).Outcome);

        await store.ReleaseAsync("k", "mine");
        Assert.Equal(ClaimOutcome.Held, (await store.ClaimAsync("k", "x", Lease)).Outcome);
    }

    // Once the write completed, its own release (a late one) leaves the answer.
    [Fact]
    public async Task Release_AfterTheAnswer_KeepsIt()
    {
        var store = new MemoryIdempotencyStore(new Clock());
        await store.ClaimAsync("k", "mine", Lease);
        await store.CompleteAsync("k", "answer", TimeSpan.FromHours(24));

        await store.ReleaseAsync("k", "mine");

        Assert.Equal("answer", (await store.ClaimAsync("k", "x", Lease)).Existing);
    }

    // A write whose lease ran out, and whose key another write then claimed, lets go of nothing.
    [Fact]
    public async Task Release_AfterAnotherClaimedTheKey_LeavesTheirs()
    {
        var clock = new Clock();
        var store = new MemoryIdempotencyStore(clock);
        await store.ClaimAsync("k", "first", Lease);
        clock.Advance(Lease);
        await store.ClaimAsync("k", "second", Lease);

        await store.ReleaseAsync("k", "first");

        Assert.Equal("second", (await store.ClaimAsync("k", "x", Lease)).Existing);
    }

    [Fact]
    public async Task IsBounded_TheAnswersSoonestToExpireMakeRoom()
    {
        var store = new MemoryIdempotencyStore(new Clock());

        for (var key = 0; key <= MemoryIdempotencyStore.MaximumEntries; key++)
        {
            await store.ClaimAsync($"k{key}", "pending", Lease);
            await store.CompleteAsync($"k{key}", "answer", TimeSpan.FromMinutes(2 + key));
        }

        // The soonest to expire made room for the last, and the latest stays.
        Assert.Equal(ClaimOutcome.Held, (await store.ClaimAsync("k0", "again", Lease)).Outcome);
        Assert.Equal(ClaimOutcome.Found, (await store.ClaimAsync($"k{MemoryIdempotencyStore.MaximumEntries}", "again", Lease)).Outcome);
    }

    // A write still running keeps its hold however full the store is, even though its lease ends long before any
    // answer's retention: dropping it would let a repeat run the write a second time.
    [Fact]
    public async Task AWriteStillRunning_IsNeverMadeRoomFor()
    {
        var store = new MemoryIdempotencyStore(new Clock());
        await store.ClaimAsync("running", "pending", Lease);

        for (var key = 0; key < MemoryIdempotencyStore.MaximumEntries; key++)
        {
            await store.ClaimAsync($"k{key}", "pending", Lease);
            await store.CompleteAsync($"k{key}", "answer", TimeSpan.FromHours(24));
        }

        var repeat = await store.ClaimAsync("running", "again", Lease);

        Assert.Equal(ClaimOutcome.Found, repeat.Outcome);
        Assert.Equal("pending", repeat.Existing);
    }

    // With every key held by a write still running there is no room, and a new write runs as it would without a key.
    [Fact]
    public async Task FullOfWritesStillRunning_ANewClaimIsUnavailable()
    {
        var store = new MemoryIdempotencyStore(new Clock());

        for (var key = 0; key < MemoryIdempotencyStore.MaximumEntries; key++)
        {
            await store.ClaimAsync($"k{key}", "pending", Lease);
        }

        Assert.Equal(ClaimOutcome.Unavailable, (await store.ClaimAsync("new", "pending", Lease)).Outcome);
        Assert.Equal(ClaimOutcome.Found, (await store.ClaimAsync("k0", "again", Lease)).Outcome);
    }

    // A hold whose lease ended is expired, and goes before any unexpired answer is made room for. Only the expired
    // pass removes it: the answers-soonest-to-expire pass passes over a write still running.
    [Fact]
    public async Task Full_AnExpiredHoldGoesBeforeAnyAnswer()
    {
        var clock = new Clock();
        var store = new MemoryIdempotencyStore(clock);
        await store.ClaimAsync("stale", "pending", Lease);

        for (var key = 0; key < MemoryIdempotencyStore.MaximumEntries - 1; key++)
        {
            await store.ClaimAsync($"k{key}", "pending", Lease);
            await store.CompleteAsync($"k{key}", "answer", TimeSpan.FromHours(1) + TimeSpan.FromMinutes(key));
        }

        clock.Advance(Lease);

        Assert.Equal(ClaimOutcome.Held, (await store.ClaimAsync("new", "pending", Lease)).Outcome);
        Assert.Equal("answer", (await store.ClaimAsync("k0", "again", Lease)).Existing);
    }

    // An answer for a key no longer held (its hold was made room for, or its lease ended and went) is still bounded:
    // with no room it is not kept.
    [Fact]
    public async Task Full_AnAnswerForAKeyNotHeld_IsNotKept()
    {
        var store = new MemoryIdempotencyStore(new Clock());

        for (var key = 0; key < MemoryIdempotencyStore.MaximumEntries; key++)
        {
            await store.ClaimAsync($"k{key}", "pending", Lease);
        }

        await store.CompleteAsync("late", "answer", TimeSpan.FromHours(24));
        await store.ReleaseAsync("k0", "pending");

        Assert.Equal(ClaimOutcome.Held, (await store.ClaimAsync("late", "again", Lease)).Outcome);
    }

    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
