using Bitwarden.Server.Sdk.Service.Querying;

namespace Bitwarden.Server.Sdk.Service.Tests.Querying;

/// <summary>
/// The shape of a filter parameter, read off its declared type: one value, a list of values in any
/// of the collection types a handler may declare, or a range.
/// </summary>
public sealed class FilterValuesTests
{
    [Fact]
    public void Of_NotAFilter_IsNull()
    {
        Assert.Null(FilterShape.Of(typeof(int)));
        Assert.Null(FilterShape.Of(typeof(List<int>)));
    }

    [Fact]
    public void Of_OneValue_IsThatTypeAlone()
    {
        Assert.Equal(new FilterShape(typeof(Guid), Many: false), FilterShape.Of(typeof(Filter<Guid>)));
    }

    [Theory]
    [InlineData(typeof(Filter<int[]>))]
    [InlineData(typeof(Filter<List<int>>))]
    [InlineData(typeof(Filter<IList<int>>))]
    [InlineData(typeof(Filter<IReadOnlyList<int>>))]
    [InlineData(typeof(Filter<ICollection<int>>))]
    [InlineData(typeof(Filter<IReadOnlyCollection<int>>))]
    [InlineData(typeof(Filter<IEnumerable<int>>))]
    public void Of_ACollection_IsManyOfItsElement(Type type)
    {
        Assert.Equal(new FilterShape(typeof(int), Many: true), FilterShape.Of(type));
    }

    [Fact]
    public void Of_ARange_IsARangeOfItsBound()
    {
        Assert.Equal(new FilterShape(typeof(DateTime), Many: false, Range: true), FilterShape.Of(typeof(Filter<Range<DateTime>>)));
    }

    // A generic type that is not one of the collections is a single value of that type.
    [Fact]
    public void Of_AnotherGeneric_IsOneValue()
    {
        Assert.Equal(new FilterShape(typeof(int?), Many: false), FilterShape.Of(typeof(Filter<int?>)));
    }
}
