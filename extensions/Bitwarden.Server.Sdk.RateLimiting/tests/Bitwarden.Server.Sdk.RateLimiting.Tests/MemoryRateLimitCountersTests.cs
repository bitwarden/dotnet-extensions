using System.Runtime.CompilerServices;
using Microsoft.Extensions.Time.Testing;

namespace Bitwarden.Server.Sdk.RateLimiting.Tests;

/// <summary>
/// Counts in this process's memory: a fixed window per bucket that opens with its first request and lasts its
/// period, at least one request allowed in each, and an exact count under concurrent requests.
/// </summary>
public sealed class MemoryRateLimitCountersTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    // A window opens with its bucket's first request and lasts its period, as AspNetCoreRateLimit's counter.
    [Fact]
    public async Task AWindowOpensWithItsFirstRequest_AndClosesAfterItsPeriod()
    {
        var time = new FakeTimeProvider();
        using var counters = new MemoryRateLimitCounters(time);

        time.Advance(TimeSpan.FromMilliseconds(700));
        Assert.True(await counters.TryTakeAsync("bucket", 1, TimeSpan.FromSeconds(1), Token));
        time.Advance(TimeSpan.FromMilliseconds(999));
        Assert.False(await counters.TryTakeAsync("bucket", 1, TimeSpan.FromSeconds(1), Token));
        Assert.True(await counters.TryTakeAsync("other", 1, TimeSpan.FromSeconds(1), Token));
        time.Advance(TimeSpan.FromMilliseconds(1));
        Assert.True(await counters.TryTakeAsync("bucket", 1, TimeSpan.FromSeconds(1), Token));

        // Long after, swept windows count afresh.
        time.Advance(TimeSpan.FromMinutes(5));
        Assert.True(await counters.TryTakeAsync("other", 1, TimeSpan.FromSeconds(1), Token));
        Assert.False(await counters.TryTakeAsync("other", 1, TimeSpan.FromSeconds(1), Token));
    }

    [Fact]
    public async Task AWindow_AllowsItsLimit_AndRefusesTheNext()
    {
        using var counters = new MemoryRateLimitCounters(new FakeTimeProvider());

        for (var i = 0; i < 3; i++)
        {
            Assert.True(await counters.TryTakeAsync("bucket", 3, TimeSpan.FromMinutes(1), Token));
        }

        Assert.False(await counters.TryTakeAsync("bucket", 3, TimeSpan.FromMinutes(1), Token));
    }

    // A limit below one still lets a window's first request through; refusing every request is the rules' job.
    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task ALimitBelowOne_AllowsTheWindowsFirstRequest(int limit)
    {
        using var counters = new MemoryRateLimitCounters(new FakeTimeProvider());

        Assert.True(await counters.TryTakeAsync("bucket", limit, TimeSpan.FromMinutes(1), Token));
        Assert.False(await counters.TryTakeAsync("bucket", limit, TimeSpan.FromMinutes(1), Token));
    }

    // Requests arriving together are each counted once: exactly the limit is let through.
    [Fact]
    public async Task ConcurrentRequests_AreCountedExactly()
    {
        using var counters = new MemoryRateLimitCounters(new FakeTimeProvider());

        var allowed = await Task.WhenAll(Enumerable.Range(0, 200).Select(_ =>
            Task.Run(async () => await counters.TryTakeAsync("bucket", 50, TimeSpan.FromMinutes(1), Token), Token)));

        Assert.Equal(50, allowed.Count(taken => taken));
    }

    // A window past its period is let go as counting goes on, so an address seen once is not held for the life of the
    // process.
    [Fact]
    public async Task AWindowPastItsPeriod_IsLetGo_AsCountingGoesOn()
    {
        var time = new FakeTimeProvider();
        using var counters = new MemoryRateLimitCounters(time);
        var bucket = await CountOnce(counters);

        time.Advance(TimeSpan.FromMinutes(2));
        Assert.True(await counters.TryTakeAsync("other", 1, TimeSpan.FromSeconds(1), Token));

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.False(bucket.TryGetTarget(out _));
    }

    // Disposing forgets every window.
    [Fact]
    public async Task Dispose_ForgetsTheWindows()
    {
        var counters = new MemoryRateLimitCounters(new FakeTimeProvider());
        Assert.True(await counters.TryTakeAsync("bucket", 1, TimeSpan.FromMinutes(1), Token));

        counters.Dispose();

        Assert.True(await counters.TryTakeAsync("bucket", 1, TimeSpan.FromMinutes(1), Token));
    }

    /// <summary>
    /// Counts once in a bucket no one else holds; the bucket is reachable afterwards only through the counters.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<WeakReference<string>> CountOnce(MemoryRateLimitCounters counters)
    {
        var bucket = string.Concat("bucket-", Guid.NewGuid().ToString("N"));
        Assert.True(await counters.TryTakeAsync(bucket, 1, TimeSpan.FromSeconds(1), Token));

        return new WeakReference<string>(bucket);
    }

    // Without a clock given, windows open and close by the system's.
    [Fact]
    public async Task WithoutAClock_CountsByTheSystemClock()
    {
        using var counters = new MemoryRateLimitCounters();

        Assert.True(await counters.TryTakeAsync("bucket", 1, TimeSpan.FromMilliseconds(200), Token));
        Assert.False(await counters.TryTakeAsync("bucket", 1, TimeSpan.FromMilliseconds(200), Token));
        await Task.Delay(TimeSpan.FromMilliseconds(300), Token);
        Assert.True(await counters.TryTakeAsync("bucket", 1, TimeSpan.FromMilliseconds(200), Token));
    }
}
