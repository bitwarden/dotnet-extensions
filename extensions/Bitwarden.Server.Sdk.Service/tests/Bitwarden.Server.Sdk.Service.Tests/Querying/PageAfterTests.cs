using System.Reflection;
using Bitwarden.Server.Sdk.Service.Querying;
using Microsoft.AspNetCore.Http;

namespace Bitwarden.Server.Sdk.Service.Tests.Querying;

/// <summary>
/// <c>page[after]</c>: the cursor as sent, or none for the first page, read from the last value
/// when the parameter is repeated.
/// </summary>
public sealed class PageAfterTests
{
    [Fact]
    public async Task BindAsync_Cursor_IsItsValue()
    {
        var actual = await PageAfter.BindAsync(Request("?page[after]=abc"), Parameter());

        Assert.Equal("abc", actual.Value);
        Assert.Equal("abc", (string?)actual);
        Assert.Equal("abc", actual.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("?page[after]=")]
    public async Task BindAsync_AbsentOrEmpty_IsTheFirstPage(string query)
    {
        var actual = await PageAfter.BindAsync(Request(query), Parameter());

        Assert.Null(actual.Value);
        Assert.Equal(string.Empty, actual.ToString());
    }

    [Fact]
    public async Task BindAsync_Repeated_TakesTheLast()
    {
        var actual = await PageAfter.BindAsync(Request("?page[after]=a&page[after]=b"), Parameter());

        Assert.Equal("b", actual.Value);
    }

    [Fact]
    public void Parameter_IsTheCursorParameter()
    {
        Assert.Equal("page[after]", PageAfter.Parameter);
    }

    private static DefaultHttpContext Request(string query)
    {
        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString(query);
        return context;
    }

    private static ParameterInfo Parameter() =>
        typeof(PageAfterTests).GetMethod(nameof(Handler), BindingFlags.NonPublic | BindingFlags.Static)!.GetParameters()[0];

    private static void Handler(PageAfter after)
    {
    }
}
