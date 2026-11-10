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

    [Fact]
    public void Dispose_OutOfOrder_DoesNotBringBackAnEndedScope()
    {
        var outer = ContextScope.Begin(First);
        var inner = ContextScope.Begin(Second);

        outer.Dispose();
        inner.Dispose();

        Assert.Null(ContextScope.Current);
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
            Assert.Same(First, ContextScope.Current);
            Assert.True(ContextScope.IsEvent);
        }

        Assert.Null(ContextScope.Current);
        Assert.False(ContextScope.IsEvent);
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
