using System.Reflection;
using Bitwarden.Server.Sdk.Service.Querying;
using Microsoft.AspNetCore.Http;

namespace Bitwarden.Server.Sdk.Service.Tests.Querying;

/// <summary>
/// A list filter binds as whichever collection type the handler declares it as.
/// </summary>
public sealed class FilterTests
{
    [Fact]
    public async Task BindAsync_Array_IsAnArrayOfTheValues()
    {
        var actual = await Filter<int[]>.BindAsync(Request("?filter[id]=1,2&filter[id]=3"), Parameter<int[]>());

        Assert.Equal([1, 2, 3], actual!.Value);
    }

    [Fact]
    public async Task BindAsync_ReadOnlyList_IsAListOfTheValues()
    {
        var actual = await Filter<IReadOnlyList<int>>.BindAsync(Request("?filter[id]=1,2"), Parameter<IReadOnlyList<int>>());

        Assert.Equal([1, 2], actual!.Value);
    }

    [Fact]
    public async Task BindAsync_Enumerable_IsTheValues()
    {
        var actual = await Filter<IEnumerable<int>>.BindAsync(Request("?filter[id]=4"), Parameter<IEnumerable<int>>());

        Assert.Equal([4], actual!.Value);
    }

    [Fact]
    public async Task BindAsync_List_IsAListOfTheValues()
    {
        var actual = await Filter<List<int>>.BindAsync(Request("?filter[id]=1,x,2"), Parameter<List<int>>());

        Assert.Equal([1, 2], actual!.Value);
    }

    [Fact]
    public async Task BindAsync_Absent_IsNull()
    {
        Assert.Null(await Filter<int[]>.BindAsync(Request(string.Empty), Parameter<int[]>()));
    }

    private static DefaultHttpContext Request(string query)
    {
        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString(query);
        return context;
    }

    private static ParameterInfo Parameter<T>() =>
        typeof(FilterTests).GetMethod(nameof(Handler), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(typeof(T)).GetParameters()[0];

    private static void Handler<T>(Filter<T>? id)
    {
    }
}
