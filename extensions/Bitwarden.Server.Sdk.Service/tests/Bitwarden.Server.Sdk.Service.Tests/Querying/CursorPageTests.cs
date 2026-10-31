using Bitwarden.Server.Sdk.Service.Querying;

namespace Bitwarden.Server.Sdk.Service.Tests.Querying;

/// <summary>
/// A cursor page is sized by the same rules as an offset page.
/// </summary>
public sealed class CursorPageTests
{
    [Fact]
    public void Size_MoreThanTheMaximum_IsTheMaximum()
    {
        Assert.Equal(Page.MaximumSize, new CursorPage { Size = int.MaxValue }.Size);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Size_LessThanOne_IsRefused(int size)
    {
        Assert.Throws<InvalidQueryException>(() => new CursorPage { Size = size });
    }
}
