namespace Bitwarden.Server.Sdk.Common.Tests;

// Work that arrives without a request states its own context. The scope must restore what was
// there before, nest, and stay within the async flow that began it.
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

        // A second dispose must not undo a scope begun after the first.
        using (ContextScope.Begin(Second))
        {
            inner.Dispose();
            Assert.Same(Second, ContextScope.Current);
        }

        Assert.Same(First, ContextScope.Current);
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

            // A scope begun in a child flow is never seen by its parent.
            await BeginInside();
            Assert.Same(First, ContextScope.Current);
        }
    }

    [Fact]
    public void Begin_Null_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => ContextScope.Begin(null!));
    }
}
