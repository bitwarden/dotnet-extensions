namespace Bitwarden.Server.Sdk.Context.Tests;

public sealed class ContextScopeTests
{
    private static readonly StatedContext First = new() { OrganizationId = Guid.NewGuid() };
    private static readonly StatedContext Second = new() { OrganizationId = Guid.NewGuid() };

    [Fact]
    public void Current_WithNoScope_IsNull()
    {
        Assert.Null(ContextScope.Current);
    }

    [Fact]
    public void Begin_IsCurrentUntilDisposed()
    {
        using (ContextScope.Begin(First))
        {
            Assert.Same(First, ContextScope.Current);
        }

        Assert.Null(ContextScope.Current);
    }

    [Fact]
    public void Begin_Nested_RestoresTheOuterScope()
    {
        using (ContextScope.Begin(First))
        {
            using (ContextScope.Begin(Second))
            {
                Assert.Same(Second, ContextScope.Current);
            }

            Assert.Same(First, ContextScope.Current);
        }
    }

    [Fact]
    public void Dispose_Twice_RestoresOnlyOnce()
    {
        using var outer = ContextScope.Begin(First);
        var inner = ContextScope.Begin(Second);
        inner.Dispose();

        using (ContextScope.Begin(Second))
        {
            inner.Dispose();
            Assert.Same(Second, ContextScope.Current);
        }

        Assert.Same(First, ContextScope.Current);
    }

    // Disposing the outer scope first leaves the inner one current until it is disposed too, and then neither is.
    [Fact]
    public void Dispose_OutOfOrder_DoesNotBringBackAnEndedScope()
    {
        var outer = ContextScope.Begin(First);
        var inner = ContextScope.Begin(Second);

        outer.Dispose();
        Assert.Same(Second, ContextScope.Current);

        inner.Dispose();
        Assert.Null(ContextScope.Current);
    }

    [Fact]
    public void Dispose_OutOfOrder_ForAnEvent_IsNotAnEvent()
    {
        var outer = ContextScope.BeginForEvent(First);
        var inner = ContextScope.BeginForEvent(Second);

        outer.Dispose();
        Assert.True(ContextScope.IsEvent);

        inner.Dispose();
        Assert.Null(ContextScope.Current);
        Assert.False(ContextScope.IsEvent);
    }

    // Work started inside a scope and not awaited, such as a send in the background, keeps the context it started with.
    [Fact]
    public async Task Begin_WorkStartedInside_KeepsTheContextAfterTheScopeIsDisposed()
    {
        var disposed = new TaskCompletionSource();
        Task<(IContext? Context, bool IsEvent)> child;

        using (ContextScope.BeginForEvent(First))
        {
            child = Task.Run(async () =>
            {
                await disposed.Task;
                return (ContextScope.Current, ContextScope.IsEvent);
            }, TestContext.Current.CancellationToken);
        }

        Assert.Null(ContextScope.Current);
        disposed.SetResult();

        var (context, isEvent) = await child;
        Assert.Equal(First.OrganizationId, context?.OrganizationId);
        Assert.True(context?.IsEvent);
        Assert.True(isEvent);
    }

    // A handler's background work that names somebody on purpose is back to handling the event once that scope ends.
    [Fact]
    public async Task Begin_WorkStartedInsideAnEventsScope_IsTheEventsAgainAfterAScopeOfItsOwn()
    {
        var disposed = new TaskCompletionSource();
        Task<bool> child;

        using (ContextScope.BeginForEvent(First))
        {
            child = Task.Run(async () =>
            {
                await disposed.Task;

                using (ContextScope.Begin(Second))
                {
                    Assert.False(ContextScope.IsEvent);
                }

                return ContextScope.IsEvent && ContextScope.Current?.OrganizationId == First.OrganizationId;
            }, TestContext.Current.CancellationToken);
        }

        disposed.SetResult();

        Assert.True(await child);
    }

    [Fact]
    public async Task Begin_WorkStartedInside_KeepsAContextThatIsNotAnEvents()
    {
        var disposed = new TaskCompletionSource();
        Task<IContext?> child;

        using (ContextScope.Begin(First))
        {
            child = Task.Run(async () =>
            {
                await disposed.Task;
                return ContextScope.Current;
            }, TestContext.Current.CancellationToken);
        }

        disposed.SetResult();

        Assert.Same(First, await child);
    }

    [Fact]
    public async Task Begin_FlowsIntoAwaitedWork_AndNotOutOfIt()
    {
        async Task<IContext?> ReadInside()
        {
            await Task.Yield();
            return ContextScope.Current;
        }

        async Task BeginInside()
        {
            await Task.Yield();
            ContextScope.Begin(Second);
        }

        using (ContextScope.Begin(First))
        {
            Assert.Same(First, await ReadInside());

            await BeginInside();
            Assert.Same(First, ContextScope.Current);
        }
    }

    [Fact]
    public void Begin_Null_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => ContextScope.Begin(null!));
    }

    [Fact]
    public void IsEvent_WithNoScope_IsFalse()
    {
        Assert.False(ContextScope.IsEvent);
    }

    [Fact]
    public void BeginForEvent_IsCurrentAndAnEventUntilDisposed()
    {
        using (ContextScope.BeginForEvent(First))
        {
            Assert.Equal(First.OrganizationId, ContextScope.Current?.OrganizationId);
            Assert.True(ContextScope.Current?.IsEvent);
            Assert.True(ContextScope.IsEvent);
        }

        Assert.Null(ContextScope.Current);
        Assert.False(ContextScope.IsEvent);
    }

    [Fact]
    public void BeginForEvent_AContextAlreadyAnEvents_IsCurrentAsItIs()
    {
        using (ContextScope.BeginForEvent(First))
        {
            var events = ContextScope.Current!;

            using (ContextScope.BeginForEvent(events))
            {
                Assert.Same(events, ContextScope.Current);
            }
        }
    }

    // A handler naming somebody on purpose begins its own scope inside the event's: that one is not the event's.
    [Fact]
    public void Begin_InsideAnEventsScope_IsNotAnEventUntilDisposed()
    {
        using (ContextScope.BeginForEvent(First))
        {
            using (ContextScope.Begin(Second))
            {
                Assert.False(ContextScope.IsEvent);
                Assert.False(ContextScope.Current?.IsEvent);
            }

            Assert.True(ContextScope.IsEvent);
        }
    }

    [Fact]
    public void BeginForEvent_Null_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => ContextScope.BeginForEvent(null!));
    }
}
