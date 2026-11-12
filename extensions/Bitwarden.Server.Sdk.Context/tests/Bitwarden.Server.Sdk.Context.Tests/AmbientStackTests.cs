namespace Bitwarden.Server.Sdk.Context.Tests;

/// <summary>
/// The stack <see cref="ContextScope"/> and <see cref="CallChain"/> keep their current value in.
/// </summary>
public sealed class AmbientStackTests
{
    [Fact]
    public void Push_Nested_RestoresTheOuterEntry()
    {
        var stack = new AmbientStack<string>();

        using (stack.Push("outer"))
        {
            using (stack.Push("inner"))
            {
                Assert.Equal("inner", stack.Current);
            }

            Assert.Equal("outer", stack.Current);
        }

        Assert.Null(stack.Current);
    }

    // Disposing the outer entry first leaves the inner one current until it is disposed too, and then neither is.
    [Fact]
    public void Dispose_OutOfOrder_DoesNotBringBackADisposedEntry()
    {
        var stack = new AmbientStack<string>();
        var outer = stack.Push("outer");
        var inner = stack.Push("inner");

        outer.Dispose();
        Assert.Equal("inner", stack.Current);

        inner.Dispose();
        Assert.Null(stack.Current);
    }

    // Restoring walks past every disposed entry, however many, to the nearest one still open.
    [Fact]
    public void Dispose_OutOfOrder_RestoresTheNearestOpenEntry()
    {
        var stack = new AmbientStack<string>();

        using (stack.Push("open"))
        {
            var first = stack.Push("first");
            var second = stack.Push("second");
            var third = stack.Push("third");

            first.Dispose();
            second.Dispose();
            third.Dispose();

            Assert.Equal("open", stack.Current);
        }
    }

    [Fact]
    public async Task Push_WorkStartedInside_KeepsTheValueAfterTheEntryIsDisposed()
    {
        var stack = new AmbientStack<string>();
        var disposed = new TaskCompletionSource();
        Task<string?> child;

        using (stack.Push("parent"))
        {
            child = Task.Run(async () =>
            {
                await disposed.Task;
                return stack.Current;
            }, TestContext.Current.CancellationToken);
        }

        Assert.Null(stack.Current);
        disposed.SetResult();

        Assert.Equal("parent", await child);
    }

    // A forked flow that pushes and disposes an entry of its own returns to the value it started with, even after the
    // parent disposed that value's entry.
    [Fact]
    public async Task Push_WorkStartedInside_ReturnsToTheValueItStartedWithAfterAnEntryOfItsOwn()
    {
        var stack = new AmbientStack<string>();
        var disposed = new TaskCompletionSource();
        Task<(string? Inside, string? After)> child;

        using (stack.Push("parent"))
        {
            child = Task.Run(async () =>
            {
                await disposed.Task;

                string? inside;
                using (stack.Push("own"))
                {
                    inside = stack.Current;
                }

                return (inside, stack.Current);
            }, TestContext.Current.CancellationToken);
        }

        disposed.SetResult();

        Assert.Equal(("own", "parent"), await child);
    }
}
