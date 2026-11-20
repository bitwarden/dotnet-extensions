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
}
