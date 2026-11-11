using Bitwarden.Server.Sdk.Service.Caching;
using Microsoft.Extensions.Caching.Memory;

namespace Bitwarden.Server.Sdk.Service.Tests.Caching;

/// <summary>
/// Concurrent requests that miss together resolve a downstream answer once, not once each.
/// </summary>
public sealed class ResolvedOnceTests
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task GetOrResolveOnceAsync_ConcurrentMisses_ResolveOnce()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var resolutions = 0;
        var release = new TaskCompletionSource<string>();

        Task<string> Ask() => cache.GetOrResolveOnceAsync("reach:a:b", Lifetime, () =>
        {
            Interlocked.Increment(ref resolutions);
            return release.Task;
        }, TestContext.Current.CancellationToken);

        var first = Ask();
        var second = Ask();
        release.SetResult("answer");

        Assert.Equal(["answer", "answer"], await Task.WhenAll(first, second));
        Assert.Equal(1, resolutions);
    }

    // A resolution's work before its first await doesn't hold up callers asking about another key.
    [Fact]
    public async Task GetOrResolveOnceAsync_ASlowStart_DoesNotBlockAnotherKey()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();

        var slow = Task.Run(() => cache.GetOrResolveOnceAsync("reach:a:b", Lifetime, () =>
        {
            entered.Set();
            release.Wait(TestContext.Current.CancellationToken);
            return Task.FromResult("slow");
        }, TestContext.Current.CancellationToken), TestContext.Current.CancellationToken);

        try
        {
            entered.Wait(TestContext.Current.CancellationToken);
            var other = Task.Run(() => cache.GetOrResolveOnceAsync("reach:c:d", Lifetime, () => Task.FromResult("other"),
                TestContext.Current.CancellationToken), TestContext.Current.CancellationToken);

            Assert.Equal("other", await other.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        }
        finally
        {
            release.Set();
        }

        Assert.Equal("slow", await slow);
    }

    [Fact]
    public async Task GetOrResolveOnceAsync_AFailedResolution_IsNotKept()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var attempts = 0;

        Task<string> Ask() => cache.GetOrResolveOnceAsync("reach:a:b", Lifetime, () =>
            ++attempts == 1 ? Task.FromException<string>(new HttpRequestException("down")) : Task.FromResult("answer"),
            TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<HttpRequestException>(Ask);
        Assert.Equal("answer", await Ask());
    }

    [Fact]
    public async Task GetOrResolveOnceAsync_OneCallerGivingUp_DoesNotCancelTheOthers()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var release = new TaskCompletionSource<string>();
        using var impatient = new CancellationTokenSource();

        var abandoned = cache.GetOrResolveOnceAsync("key", Lifetime, () => release.Task, impatient.Token);
        var waiting = cache.GetOrResolveOnceAsync("key", Lifetime, () => release.Task, TestContext.Current.CancellationToken);
        await impatient.CancelAsync();
        release.SetResult("answer");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => abandoned);
        Assert.Equal("answer", await waiting);
    }
}
