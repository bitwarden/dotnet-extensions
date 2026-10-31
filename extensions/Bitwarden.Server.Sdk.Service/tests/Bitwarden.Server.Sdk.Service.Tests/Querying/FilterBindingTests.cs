using Bitwarden.Server.Sdk.Service.Querying;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace Bitwarden.Server.Sdk.Service.Tests.Querying;

/// <summary>
/// <c>filter[name]</c> read from the query into the shape the handler declares: one value taken
/// whole, a list from repeated parameters or commas, or a range; and every value that cannot be
/// one of those refused at the parameter.
/// </summary>
public sealed class FilterBindingTests
{
    private const string Parameter = "filter[id]";

    [Fact]
    public void Bind_Absent_IsNull()
    {
        Assert.Null(FilterBinding.Bind(typeof(Filter<int>), "id", new QueryCollection()));
    }

    [Fact]
    public void Bind_NotAFilter_IsAWiringError()
    {
        Assert.Throws<InvalidOperationException>(() => FilterBinding.Bind(typeof(int), "id", Query("1")));
    }

    [Fact]
    public void Bind_OneValue_IsTrimmedAndConverted()
    {
        var actual = (Filter<int>)FilterBinding.Bind(typeof(Filter<int>), "id", Query(" 7 "))!;

        Assert.Equal(7, actual.Value);
    }

    // Taken whole, as the standards say a single-valued parameter is.
    [Fact]
    public void Bind_OneTextValueWithACommaInIt_KeepsTheComma()
    {
        var actual = (Filter<string>)FilterBinding.Bind(typeof(Filter<string>), "id", Query("Smith, Jr"))!;

        Assert.Equal("Smith, Jr", actual.Value);
    }

    [Fact]
    public void Bind_OneValueSentTwice_IsRefused()
    {
        var actual = Assert.Throws<InvalidFilterException>(() => FilterBinding.Bind(typeof(Filter<int>), "id", Query("1", "2")));

        Assert.Contains("accepts one value", actual.Message, StringComparison.Ordinal);
        Assert.Equal(Parameter, Assert.Single(actual.Errors).Source!.Parameter);
    }

    [Fact]
    public void Bind_OneValueThatWillNotConvertAndHasACommaInIt_SaysItTakesOne()
    {
        var actual = Assert.Throws<InvalidFilterException>(() => FilterBinding.Bind(typeof(Filter<int>), "id", Query("1,2")));

        Assert.Contains("accepts one value", actual.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Bind_OneValueThatWillNotConvert_SaysWhatItExpects()
    {
        var actual = Assert.Throws<InvalidFilterException>(() => FilterBinding.Bind(typeof(Filter<Guid>), "id", Query("x")));

        Assert.Contains("identifier", actual.Message, StringComparison.Ordinal);
        Assert.Equal(Parameter, Assert.Single(actual.Errors).Source!.Parameter);
    }

    [Theory]
    [InlineData(typeof(Filter<Guid>), "expects an identifier")]
    [InlineData(typeof(Filter<int>), "expects an int32")]
    [InlineData(typeof(Filter<DateTimeOffset>), "expects a datetimeoffset")]
    public void Bind_OneValueThatWillNotConvert_NamesTheTypeWithTheRightArticle(Type filter, string expected)
    {
        var actual = Assert.Throws<InvalidFilterException>(() => FilterBinding.Bind(filter, "id", Query("x")));

        Assert.Contains(expected, actual.Message, StringComparison.Ordinal);
    }

    // By the name it is published under, not its CLR name.
    [Fact]
    public void Bind_Enumeration_IsReadByItsPublishedName()
    {
        var actual = (Filter<Shade>)FilterBinding.Bind(typeof(Filter<Shade>), "id", Query("DARK_BLUE"))!;

        Assert.Equal(Shade.DarkBlue, actual.Value);
        Assert.Throws<InvalidFilterException>(() => FilterBinding.Bind(typeof(Filter<Shade>), "id", Query("DarkBlue")));
    }

    [Fact]
    public void Bind_Many_ReadsRepeatedParametersAndCommasAlike_SkippingEmpties()
    {
        var actual = (Filter<string[]>)FilterBinding.Bind(typeof(Filter<string[]>), "id", Query("a,,b", @"c\,d"))!;

        Assert.Equal(["a", "b", "c,d"], actual.Value);
    }

    [Fact]
    public void Bind_ManyEndingInALoneBackslash_IsRefused()
    {
        var actual = Assert.Throws<InvalidFilterException>(() => FilterBinding.Bind(typeof(Filter<string[]>), "id", Query(@"a,b\")));

        Assert.Contains("backslash", actual.Message, StringComparison.Ordinal);
        Assert.Equal(Parameter, Assert.Single(actual.Errors).Source!.Parameter);
    }

    [Fact]
    public void Bind_Range_IsItsBoundsAndInclusivity()
    {
        var actual = (Filter<Range<int>>)FilterBinding.Bind(typeof(Filter<Range<int>>), "id", Query("[1,*)"))!;

        Assert.Equal(1, actual.Value.Lower);
        Assert.True(actual.Value.LowerInclusive);
        Assert.Null(actual.Value.Upper);
    }

    [Theory]
    [InlineData("1,5")]
    [InlineData("[x,5]")]
    [InlineData("[1,y]")]
    public void Bind_RangeThatIsNotOne_IsRefused(string value)
    {
        var actual = Assert.Throws<InvalidFilterException>(() => FilterBinding.Bind(typeof(Filter<Range<int>>), "id", Query(value)));

        Assert.Contains("expects a range", actual.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("[5,1]")]
    [InlineData("[3,3)")]
    public void Bind_RangeThatHoldsNothing_IsRefused(string value)
    {
        var actual = Assert.Throws<InvalidFilterException>(() => FilterBinding.Bind(typeof(Filter<Range<int>>), "id", Query(value)));

        Assert.Contains("describes no values", actual.Message, StringComparison.Ordinal);
        Assert.Equal(Parameter, Assert.Single(actual.Errors).Source!.Parameter);
    }

    [Fact]
    public void Bind_RangeSentTwice_IsRefused()
    {
        Assert.Throws<InvalidFilterException>(() => FilterBinding.Bind(typeof(Filter<Range<int>>), "id", Query("[1,2]", "[3,4]")));
    }

    private static QueryCollection Query(params string[] values) =>
        new(new Dictionary<string, StringValues> { [Parameter] = values });

    public enum Shade
    {
        Light,
        DarkBlue,
    }
}
