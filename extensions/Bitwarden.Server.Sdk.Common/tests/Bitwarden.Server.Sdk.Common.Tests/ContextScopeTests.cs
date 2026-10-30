namespace Bitwarden.Server.Sdk.Common.Tests;

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
}
