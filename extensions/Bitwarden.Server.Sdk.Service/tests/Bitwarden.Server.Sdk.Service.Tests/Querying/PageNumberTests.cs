using System.Reflection;
using Bitwarden.Server.Sdk.Service.Querying;
using Microsoft.AspNetCore.Http;

namespace Bitwarden.Server.Sdk.Service.Tests.Querying;

/// <summary>
/// <c>page[number]</c>: the first page unless the caller asks for another, and even an unset
/// value is the first page rather than zero.
/// </summary>
public sealed class PageNumberTests
{
    [Fact]
    public void Default_IsTheFirstPage()
    {
        Assert.Equal(1, default(PageNumber).Value);
        Assert.Equal(PageNumber.Default, new PageNumber().Value);
    }

    [Fact]
    public void Value_ConvertsAndPrintsAsTheNumber()
    {
        var actual = new PageNumber(4);

        Assert.Equal(4, (int)actual);
        Assert.Equal("4", actual.ToString());
    }

    [Fact]
    public async Task BindAsync_Number_IsRead()
    {
        Assert.Equal(3, (await PageNumber.BindAsync(Request("?page[number]=3"), Parameter())).Value);
    }

    [Fact]
    public async Task BindAsync_Absent_IsTheFirstPage()
    {
        Assert.Equal(1, (await PageNumber.BindAsync(Request(string.Empty), Parameter())).Value);
    }

    [Fact]
    public async Task BindAsync_NotAWholeNumber_IsRefused()
    {
        await Assert.ThrowsAsync<InvalidQueryException>(async () => await PageNumber.BindAsync(Request("?page[number]=two"), Parameter()));
    }

    private static DefaultHttpContext Request(string query)
    {
        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString(query);
        return context;
    }

    private static ParameterInfo Parameter() =>
        typeof(PageNumberTests).GetMethod(nameof(Handler), BindingFlags.NonPublic | BindingFlags.Static)!.GetParameters()[0];

    private static void Handler(PageNumber number)
    {
    }
}
