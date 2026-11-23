namespace Bitwarden.Server.Sdk.Context.Tests;

public sealed class RequestMemoTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static Func<CancellationToken, Task<RequestMemoRead<int>>> Counting(Func<int> next, RequestMemoKeep keep = RequestMemoKeep.Keep) =>
        _ => Task.FromResult(new RequestMemoRead<int>(next(), 4, keep));

    [Fact]
    public void Current_NoneBegun_Null()
    {
        Assert.Null(RequestMemo.Current);
    }

    [Fact]
    public void Begin_CurrentUntilDisposed_ThenTheOneBefore()
    {
        using var outer = RequestMemo.Begin();
        var inner = RequestMemo.Begin();

        Assert.Same(inner, RequestMemo.Current);
        inner.Dispose();
        Assert.Same(outer, RequestMemo.Current);
    }

    [Fact]
    public async Task RememberAsync_SameKey_ReadOnce()
    {
        using var memo = RequestMemo.Begin();
        var reads = 0;

        var first = await memo.RememberAsync("k", false, Counting(() => ++reads), Token);
        var second = await memo.RememberAsync("k", false, Counting(() => ++reads), Token);

        Assert.Equal((1, RequestMemoOutcome.Miss), first);
        Assert.Equal((1, RequestMemoOutcome.Hit), second);
    }

    [Fact]
    public async Task RememberAsync_Fresh_ReadsAndReplaces()
    {
        using var memo = RequestMemo.Begin();
        var reads = 0;

        await memo.RememberAsync("k", false, Counting(() => ++reads), Token);
        var fresh = await memo.RememberAsync("k", true, Counting(() => ++reads), Token);
        var after = await memo.RememberAsync("k", false, Counting(() => ++reads), Token);

        Assert.Equal(2, fresh.Value);
        Assert.Equal((2, RequestMemoOutcome.Hit), after);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RememberAsync_NotKept_ReadAgain(bool shared)
    {
        var keep = shared ? RequestMemoKeep.Share : RequestMemoKeep.Alone;
        using var memo = RequestMemo.Begin();
        var reads = 0;

        await memo.RememberAsync("k", false, Counting(() => ++reads, keep), Token);
        await memo.RememberAsync("k", false, Counting(() => ++reads, keep), Token);

        Assert.Equal(2, reads);
    }

    // An answer shared but not kept reaches the caller already waiting on it, and nobody after.
    [Fact]
    public async Task RememberAsync_SharedAnswerInFlight_ReachesItsWaiterAndIsNotKept()
    {
        using var memo = RequestMemo.Begin();
        var release = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reads = 0;

        var first = memo.RememberAsync("k", false, async _ =>
        {
            reads++;
            return new RequestMemoRead<int>(await release.Task, 4, RequestMemoKeep.Share);
        }, Token);
        var waiter = memo.RememberAsync("k", false, Counting(() => ++reads), Token);
        release.SetResult(9);

        Assert.Equal((9, RequestMemoOutcome.Joined), await waiter);
        Assert.Equal(9, (await first).Value);
        Assert.Equal(1, reads);

        Assert.Equal((2, RequestMemoOutcome.Miss), await memo.RememberAsync("k", false, Counting(() => ++reads), Token));
    }

    // An answer that may not be shared never reaches the caller waiting on it: that caller reads for itself.
    [Fact]
    public async Task RememberAsync_UnshareableAnswerInFlight_ItsWaiterReadsForItself()
    {
        using var memo = RequestMemo.Begin();
        var release = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

        var first = memo.RememberAsync(
            "k", false, async _ => new RequestMemoRead<int>(await release.Task, 4, RequestMemoKeep.Alone), Token);
        var waiter = memo.RememberAsync("k", false, Counting(() => 2), Token);
        release.SetResult(9);

        Assert.Equal((2, RequestMemoOutcome.Bypassed), await waiter);
        Assert.Equal(9, (await first).Value);
    }

    // A waiting caller that is cancelled stops waiting; the read it waited on goes on and is kept.
    [Fact]
    public async Task RememberAsync_AWaitingCallerCancelled_StopsWaitingAndTheReadIsStillKept()
    {
        using var memo = RequestMemo.Begin();
        using var waiter = new CancellationTokenSource();
        var release = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

        var first = memo.RememberAsync(
            "k", false, async _ => new RequestMemoRead<int>(await release.Task, 4, RequestMemoKeep.Keep), Token);
        var second = memo.RememberAsync("k", false, Counting(() => 7), waiter.Token);
        await waiter.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second.WaitAsync(TimeSpan.FromSeconds(5), Token));
        release.SetResult(9);
        Assert.Equal((9, RequestMemoOutcome.Miss), await first);
        Assert.Equal((9, RequestMemoOutcome.Hit), await memo.RememberAsync("k", false, Counting(() => 8), Token));
    }

    [Fact]
    public async Task Forget_ForgetsTheCurrentMemo()
    {
        using var memo = RequestMemo.Begin();
        var reads = 0;

        await memo.RememberAsync("k", false, Counting(() => ++reads), Token);
        RequestMemo.Forget();
        await memo.RememberAsync("k", false, Counting(() => ++reads), Token);

        Assert.Equal(2, reads);
    }

    [Fact]
    public async Task Forget_InsideANestedMemo_ForgetsTheOuterToo()
    {
        using var outer = RequestMemo.Begin();
        var reads = 0;
        await outer.RememberAsync("k", false, Counting(() => ++reads), Token);

        using (RequestMemo.Begin())
        {
            RequestMemo.Forget();
        }

        var after = await outer.RememberAsync("k", false, Counting(() => ++reads), Token);
        Assert.Equal((2, RequestMemoOutcome.Miss), after);
    }

    [Fact]
    public async Task Dispose_OfANestedMemo_KeepsTheOuter()
    {
        using var outer = RequestMemo.Begin();
        var reads = 0;
        await outer.RememberAsync("k", false, Counting(() => ++reads), Token);

        RequestMemo.Begin().Dispose();

        var after = await outer.RememberAsync("k", false, Counting(() => ++reads), Token);
        Assert.Equal((1, RequestMemoOutcome.Hit), after);
    }

    [Fact]
    public async Task RememberAsync_AfterDispose_ReadsEachTime()
    {
        var memo = RequestMemo.Begin();
        memo.Dispose();
        var reads = 0;

        var first = await memo.RememberAsync("k", false, Counting(() => ++reads), Token);
        await memo.RememberAsync("k", false, Counting(() => ++reads), Token);

        Assert.Equal(RequestMemoOutcome.Bypassed, first.Outcome);
        Assert.Equal(2, reads);
        Assert.Null(RequestMemo.Current);
    }

    // A read cancelled by its own caller fails only that caller: another waiting on it reads for itself.
    [Fact]
    public async Task RememberAsync_TheSharedReadCancelledBySomebodyElse_ReadsForItself()
    {
        using var memo = RequestMemo.Begin();
        using var leader = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var first = memo.RememberAsync<int>("k", false, async token =>
        {
            started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return default;
        }, leader.Token);
        await started.Task.WaitAsync(Token);
        var second = memo.RememberAsync("k", false, Counting(() => 7), Token);
        await leader.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        Assert.Equal((7, RequestMemoOutcome.Bypassed), await second);
    }

    [Fact]
    public async Task RememberAsync_AFlowForkedInside_SharesItUntilItEnds()
    {
        var memo = RequestMemo.Begin();
        var forked = Task.Run(() => RequestMemo.Current, Token);

        Assert.Same(memo, await forked);
        memo.Dispose();
        Assert.Null(await Task.Run(() => RequestMemo.Current, Token));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    public void Begin_NegativeBound_Throws(int maxEntries, long maxBytes)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RequestMemo.Begin(maxEntries, maxBytes));
        Assert.Null(RequestMemo.Current);
    }

    // Past the bound on answers, a call is read as if there were no memo, and what is kept stays kept.
    [Fact]
    public async Task RememberAsync_PastMaxEntries_Bypassed()
    {
        using var memo = RequestMemo.Begin(maxEntries: 1, maxBytes: RequestMemo.DefaultMaxBytes);
        var reads = 0;

        await memo.RememberAsync("a", false, Counting(() => ++reads), Token);
        var past = await memo.RememberAsync("b", false, Counting(() => ++reads), Token);
        await memo.RememberAsync("b", false, Counting(() => ++reads), Token);
        var kept = await memo.RememberAsync("a", false, Counting(() => ++reads), Token);

        Assert.Equal(RequestMemoOutcome.Bypassed, past.Outcome);
        Assert.Equal(3, reads);
        Assert.Equal((1, RequestMemoOutcome.Hit), kept);
    }

    // At the bound on answers, a fresh read of an answer already kept replaces it: the stale one is never answered again.
    [Fact]
    public async Task RememberAsync_FreshAtMaxEntries_ReplacesTheKeptAnswer()
    {
        using var memo = RequestMemo.Begin(maxEntries: 1, maxBytes: RequestMemo.DefaultMaxBytes);
        var reads = 0;

        await memo.RememberAsync("k", false, Counting(() => ++reads), Token);
        var fresh = await memo.RememberAsync("k", true, Counting(() => ++reads), Token);
        var after = await memo.RememberAsync("k", false, Counting(() => ++reads), Token);

        Assert.Equal((2, RequestMemoOutcome.Miss), fresh);
        Assert.Equal((2, RequestMemoOutcome.Hit), after);
    }

    // Parallel reads of different answers never take the memo past its bound on answers.
    [Fact]
    public async Task RememberAsync_ParallelReadsAtMaxEntries_KeepNoMoreThanTheBound()
    {
        using var memo = RequestMemo.Begin(maxEntries: 4, maxBytes: RequestMemo.DefaultMaxBytes);
        var release = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = 0;

        var reads = Enumerable.Range(0, 64).Select(i => Task.Run(() => memo.RememberAsync($"k{i}", false, async _ =>
        {
            Interlocked.Increment(ref started);
            return new RequestMemoRead<int>(await release.Task, 4, RequestMemoKeep.Keep);
        }, Token), Token)).ToArray();
        while (Volatile.Read(ref started) < 64)
        {
            await Task.Delay(1, Token);
        }

        release.SetResult(1);
        var outcomes = await Task.WhenAll(reads);

        Assert.Equal(4, outcomes.Count(outcome => outcome.Outcome == RequestMemoOutcome.Miss));
    }

    // A fresh read that replaces one still in flight: the replaced read's answer isn't kept, so its bytes never count.
    [Fact]
    public async Task RememberAsync_FreshReplacesAReadInFlight_TheReplacedReadsBytesNotCounted()
    {
        using var memo = RequestMemo.Begin(maxEntries: 10, maxBytes: 8);
        var release = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

        var first = memo.RememberAsync("k", false, async _ =>
            new RequestMemoRead<int>(await release.Task, 4, RequestMemoKeep.Keep), Token);
        var fresh = await memo.RememberAsync("k", true, Counting(() => 2), Token);
        release.SetResult(1);
        await first;

        await memo.RememberAsync("other", false, Counting(() => 3), Token);
        var kept = await memo.RememberAsync("other", false, Counting(() => 4), Token);

        Assert.Equal((2, RequestMemoOutcome.Miss), fresh);
        Assert.Equal((2, RequestMemoOutcome.Hit), await memo.RememberAsync("k", false, Counting(() => 5), Token));
        Assert.Equal((3, RequestMemoOutcome.Hit), kept);
    }

    // A fresh read that replaces a kept answer gives that answer's bytes back, so the memo can keep as much again.
    [Fact]
    public async Task RememberAsync_FreshReplacesAKeptAnswer_ItsBytesAreGivenBack()
    {
        using var memo = RequestMemo.Begin(maxEntries: 10, maxBytes: 8);

        await memo.RememberAsync("k", false, Counting(() => 1), Token);
        await memo.RememberAsync("k", true, Counting(() => 2), Token);
        await memo.RememberAsync("other", false, Counting(() => 3), Token);

        Assert.Equal((3, RequestMemoOutcome.Hit), await memo.RememberAsync("other", false, Counting(() => 4), Token));
    }

    // An answer that doesn't fit the bound on bytes, or gives no size, is answered but not kept.
    [Theory]
    [InlineData(5)]
    [InlineData(-1)]
    public async Task RememberAsync_AnswerThatDoesNotFit_NotKept(long size)
    {
        using var memo = RequestMemo.Begin(maxEntries: 10, maxBytes: 4);
        var reads = 0;
        Func<CancellationToken, Task<RequestMemoRead<int>>> read =
            _ => Task.FromResult(new RequestMemoRead<int>(++reads, size, RequestMemoKeep.Keep));

        var first = await memo.RememberAsync("k", false, read, Token);
        var second = await memo.RememberAsync("k", false, read, Token);

        Assert.Equal((1, RequestMemoOutcome.Miss), first);
        Assert.Equal((2, RequestMemoOutcome.Miss), second);
    }

    // Two identical calls in flight at once make one read between them.
    [Fact]
    public async Task RememberAsync_IdenticalCallsInFlight_Joined()
    {
        using var memo = RequestMemo.Begin();
        var release = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reads = 0;

        var first = memo.RememberAsync("k", false, async _ =>
        {
            reads++;
            return new RequestMemoRead<int>(await release.Task, 4, RequestMemoKeep.Keep);
        }, Token);
        var second = memo.RememberAsync("k", false, Counting(() => ++reads), Token);
        release.SetResult(9);

        Assert.Equal((9, RequestMemoOutcome.Miss), await first);
        Assert.Equal((9, RequestMemoOutcome.Joined), await second);
        Assert.Equal(1, reads);
    }

    // A read that fails fails every caller waiting on it, and nothing is kept: the next call reads again.
    [Fact]
    public async Task RememberAsync_ReadFails_FailsItsWaitersAndKeepsNothing()
    {
        using var memo = RequestMemo.Begin();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var first = memo.RememberAsync<int>("k", false, async _ =>
        {
            await release.Task;
            throw new InvalidOperationException("refused");
        }, Token);
        var second = memo.RememberAsync("k", false, Counting(() => 7), Token);
        release.SetResult();

        await Assert.ThrowsAsync<InvalidOperationException>(() => first);
        await Assert.ThrowsAsync<InvalidOperationException>(() => second);
        Assert.Equal((8, RequestMemoOutcome.Miss), await memo.RememberAsync("k", false, Counting(() => 8), Token));
    }

    // A write seen while a read is in flight: the read still answers its caller, but its answer is never kept, and
    // its bytes don't count against what the memo keeps after the write.
    [Fact]
    public async Task RememberAsync_ForgottenWhileInFlight_AnswerNotKept()
    {
        using var memo = RequestMemo.Begin(maxEntries: 10, maxBytes: 4);
        var release = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

        var first = memo.RememberAsync("k", false, async _ =>
            new RequestMemoRead<int>(await release.Task, 4, RequestMemoKeep.Keep), Token);
        RequestMemo.Forget();
        release.SetResult(1);

        Assert.Equal((1, RequestMemoOutcome.Miss), await first);
        Assert.Equal((2, RequestMemoOutcome.Miss), await memo.RememberAsync("k", false, Counting(() => 2), Token));
        Assert.Equal((2, RequestMemoOutcome.Hit), await memo.RememberAsync("k", false, Counting(() => 3), Token));
    }
}
