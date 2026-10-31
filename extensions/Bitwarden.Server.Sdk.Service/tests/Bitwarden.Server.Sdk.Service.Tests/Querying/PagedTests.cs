using Bitwarden.Server.Sdk.Service.Querying;

namespace Bitwarden.Server.Sdk.Service.Tests.Querying;

/// <summary>
/// An offset page carries its rows and the size of the whole collection, which can be larger than
/// the rows it holds.
/// </summary>
public sealed class PagedTests
{
    [Fact]
    public void Deconstruct_IsTheRowsAndTheTotal()
    {
        var (items, total) = new Paged<string>(["a", "b"], 12);

        Assert.Equal(["a", "b"], items);
        Assert.Equal(12, total);
    }
}
