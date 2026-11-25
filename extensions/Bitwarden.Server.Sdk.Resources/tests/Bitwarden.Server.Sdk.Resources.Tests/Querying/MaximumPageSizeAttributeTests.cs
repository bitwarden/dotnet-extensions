using System.Reflection;
using Bitwarden.Server.Sdk.Resources.Querying;

namespace Bitwarden.Server.Sdk.Resources.Tests.Querying;

/// <summary>
/// The maximum a parameter declares, read off the <see cref="PageSize"/> itself or, for a
/// <see cref="PageNumber"/>, off the <see cref="PageSize"/> beside it, nullable or not.
/// </summary>
public sealed class MaximumPageSizeAttributeTests
{
    [Fact]
    public void Constructor_LessThanOne_IsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new MaximumPageSizeAttribute(0));
    }

    [Fact]
    public void Of_Declared_TheDeclaredSize()
    {
        Assert.Equal(1000, MaximumPageSizeAttribute.Of(Parameter(nameof(Large), 1)));
    }

    [Fact]
    public void Of_UndeclaredOrNoParameter_TheStandardMaximum()
    {
        Assert.Equal(Page.MaximumSize, MaximumPageSizeAttribute.Of(Parameter(nameof(Undeclared), 1)));
        Assert.Equal(Page.MaximumSize, MaximumPageSizeAttribute.Of(null));
    }

    [Theory]
    [InlineData(nameof(Large))]
    [InlineData(nameof(LargeNullable))]
    public void OfSibling_PageSizeBesideDeclaresOne_TheDeclaredSize(string handler)
    {
        Assert.Equal(1000, MaximumPageSizeAttribute.OfSibling(Parameter(handler, 0)));
    }

    [Fact]
    public void OfSibling_NoPageSizeBeside_TheStandardMaximum()
    {
        Assert.Equal(Page.MaximumSize, MaximumPageSizeAttribute.OfSibling(Parameter(nameof(NumberOnly), 0)));
        Assert.Equal(Page.MaximumSize, MaximumPageSizeAttribute.OfSibling(null));
    }

    private static ParameterInfo Parameter(string handler, int position) =>
        typeof(MaximumPageSizeAttributeTests).GetMethod(handler, BindingFlags.NonPublic | BindingFlags.Static)!.GetParameters()[position];

    private static void Large(PageNumber number, [MaximumPageSize(1000)] PageSize size)
    {
    }

    private static void LargeNullable(PageNumber number, [MaximumPageSize(1000)] PageSize? size)
    {
    }

    private static void Undeclared(PageNumber number, PageSize size)
    {
    }

    private static void NumberOnly(PageNumber number)
    {
    }
}
