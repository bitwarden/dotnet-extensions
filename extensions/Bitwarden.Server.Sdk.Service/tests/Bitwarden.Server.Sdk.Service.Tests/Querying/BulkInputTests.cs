using Bitwarden.Server.Sdk.Service.Querying;

namespace Bitwarden.Server.Sdk.Service.Tests.Querying;

/// <summary>
/// A bulk action's input, the same for every resource or one per resource, read by index either way.
/// </summary>
public sealed class BulkInputTests
{
    [Fact]
    public void At_Every_IsTheSameInputAtAnyIndex()
    {
        var actual = new BulkInput<string> { Every = "x" };

        Assert.False(actual.IsPerResource);
        Assert.Equal("x", actual.At(0));
        Assert.Equal("x", actual.At(99));
    }

    [Fact]
    public void At_Each_IsTheInputAtThatIndex()
    {
        var actual = new BulkInput<string> { Each = ["x", "y"] };

        Assert.True(actual.IsPerResource);
        Assert.Equal("y", actual.At(1));
    }

    // An empty array is still per resource: it names no inputs rather than one for all.
    [Fact]
    public void IsPerResource_EmptyEach_IsTrue()
    {
        Assert.True(new BulkInput<string> { Each = [] }.IsPerResource);
    }
}
