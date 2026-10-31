using Bitwarden.Server.Sdk.Service.Querying;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace Bitwarden.Server.Sdk.Service.Tests.Querying;

/// <summary>
/// The page parameters are read, and the ones no page could be built from are refused, before
/// the handler runs.
/// </summary>
public sealed class PageBindingTests
{
    [Fact]
    public void Bind_PageNumber_IsRead()
    {
        Assert.Equal(new PageNumber(3), PageBinding.Bind(typeof(PageNumber), Query(PageNumber.Parameter, "3"), declaredSize: null));
    }

    // page[number]=2147483647&page[size]=200 starts more rows in than a count can hold.
    [Theory]
    [InlineData("2147483647")]
    [InlineData("10737419")]
    public void Bind_PageNumberPastTheFarthestPage_IsRefused(string number)
    {
        var actual = Assert.Throws<InvalidQueryException>(
            () => PageBinding.Bind(typeof(PageNumber), Query(PageNumber.Parameter, number), declaredSize: null));

        Assert.Equal(PageNumber.Parameter, Assert.Single(actual.Errors).Source?.Parameter);
        Assert.Equal("422", Assert.Single(actual.Errors).Status);
    }

    [Fact]
    public void Bind_PageSizeNotAWholeNumber_IsRefused()
    {
        Assert.Throws<InvalidQueryException>(
            () => PageBinding.Bind(typeof(PageSize), Query(PageSize.Parameter, "-1"), declaredSize: null));
    }

    private static QueryCollection Query(string name, string value) =>
        new(new Dictionary<string, StringValues> { [name] = value });
}
