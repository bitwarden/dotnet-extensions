using System.ComponentModel;
using System.Reflection;
using Bitwarden.Server.Sdk.Service.Querying;
using Microsoft.AspNetCore.Http;

namespace Bitwarden.Server.Sdk.Service.Tests.Querying;

/// <summary>
/// <c>page[size]</c>: what the caller asks for, else the size the operation declares, else the
/// platform's default; and an unset value is the default rather than zero.
/// </summary>
public sealed class PageSizeTests
{
    [Fact]
    public void Default_IsThePlatformsDefaultSize()
    {
        Assert.Equal(Page.DefaultSize, default(PageSize).Value);
    }

    [Fact]
    public void Value_ConvertsAndPrintsAsTheSize()
    {
        var actual = new PageSize(20);

        Assert.Equal(20, (int)actual);
        Assert.Equal("20", actual.ToString());
    }

    [Fact]
    public async Task BindAsync_Size_IsRead()
    {
        Assert.Equal(10, (await PageSize.BindAsync(Request("?page[size]=10"), Parameter(nameof(Declared)))).Value);
    }

    [Fact]
    public async Task BindAsync_AbsentWithADeclaredSize_IsTheDeclaredSize()
    {
        Assert.Equal(25, (await PageSize.BindAsync(Request(string.Empty), Parameter(nameof(Declared)))).Value);
    }

    [Fact]
    public async Task BindAsync_AbsentWithoutADeclaredSize_IsThePlatformsDefault()
    {
        Assert.Equal(Page.DefaultSize, (await PageSize.BindAsync(Request(string.Empty), Parameter(nameof(Undeclared)))).Value);
    }

    [Fact]
    public async Task BindAsync_NotAWholeNumber_IsRefused()
    {
        await Assert.ThrowsAsync<InvalidQueryException>(async () => await PageSize.BindAsync(Request("?page[size]=1.5"), Parameter(nameof(Undeclared))));
    }

    private static DefaultHttpContext Request(string query)
    {
        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString(query);
        return context;
    }

    private static ParameterInfo Parameter(string handler) =>
        typeof(PageSizeTests).GetMethod(handler, BindingFlags.NonPublic | BindingFlags.Static)!.GetParameters()[0];

    private static void Declared([DefaultValue(25)] PageSize size)
    {
    }

    private static void Undeclared(PageSize size)
    {
    }
}
