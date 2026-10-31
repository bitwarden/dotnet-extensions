using Bitwarden.Server.Sdk.Service.Querying;

namespace Bitwarden.Server.Sdk.Service.Tests.Querying;

/// <summary>
/// A page read by cursor has more after it exactly when it carries a cursor to continue from.
/// </summary>
public sealed class CursorPagedTests
{
    [Fact]
    public void HasMore_WithACursor_IsTrue()
    {
        Assert.True(new CursorPaged<int>([1, 2], "next").HasMore);
    }

    [Fact]
    public void HasMore_WithoutACursor_IsFalse()
    {
        Assert.False(new CursorPaged<int>([1, 2], null).HasMore);
    }
}
