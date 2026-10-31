using System.Reflection;
using Bitwarden.Server.Sdk.Service.JsonApi;
using Bitwarden.Server.Sdk.Service.Querying;
using Microsoft.AspNetCore.Http;

namespace Bitwarden.Server.Sdk.Service.Tests.Querying;

/// <summary>
/// A sort orders by at least one field, is written back as the <c>sort</c> parameter, defaults to
/// what the resource declares, and binds against the resource its operation returns.
/// </summary>
public sealed class SortTests
{
    [Fact]
    public void By_Fields_KeepsThemInOrder()
    {
        var actual = Sort.By(SortBy.Descending("createdAt"), SortBy.Ascending("name"));

        Assert.Equal([SortBy.Descending("createdAt"), SortBy.Ascending("name")], actual.Fields);
        Assert.Equal("-createdAt,name", actual.ToString());
        Assert.Null(actual.Resource);
    }

    [Fact]
    public void By_NoField_Throws()
    {
        Assert.Throws<ArgumentException>(() => Sort.By());
        Assert.Throws<ArgumentNullException>(() => Sort.By(null!));
    }

    [Fact]
    public void Default_IsTheFieldTheResourceDeclares_ForThatResource()
    {
        var actual = Sort.Default<Item>();

        Assert.Equal("-createdAt", actual.ToString());
        Assert.Equal(typeof(Item), actual.Resource);
    }

    [Fact]
    public void Default_ResourceDeclaringNoSortableFields_Throws()
    {
        var actual = Assert.Throws<InvalidOperationException>(() => Sort.Default<Unsorted>());

        Assert.Contains("[Sortable(Default = true)]", actual.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BindAsync_OperationReturningAResourceList_ChecksAgainstThatResource()
    {
        var actual = await Sort.BindAsync(Request("?sort=-NAME"), Parameter(nameof(List)));

        Assert.Equal("-name", actual!.ToString());
        Assert.Equal(typeof(Item), actual.Resource);
    }

    [Fact]
    public async Task BindAsync_OperationReturningNoResource_SortsByNothing()
    {
        var actual = await Assert.ThrowsAsync<InvalidSortException>(async () => await Sort.BindAsync(Request("?sort=name"), Parameter(nameof(Nothing))));

        Assert.EndsWith("This collection cannot be sorted.", Assert.Single(actual.Errors).Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BindAsync_Absent_IsNull()
    {
        Assert.Null(await Sort.BindAsync(Request(string.Empty), Parameter(nameof(List))));
    }

    private static DefaultHttpContext Request(string query)
    {
        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString(query);
        return context;
    }

    private static ParameterInfo Parameter(string handler) =>
        typeof(SortTests).GetMethod(handler, BindingFlags.NonPublic | BindingFlags.Static)!.GetParameters()[0];

    private static Task<PagedResourceListDocument<Item>> List(Sort? sort) => throw new NotSupportedException();

    private static void Nothing(Sort? sort)
    {
    }

    [Sortable(nameof(CreatedAt), Default = true, Descending = true)]
    private sealed record Item : Resource
    {
        [Sortable]
        public string? Name { get; init; }
    }

    private sealed record Unsorted : Resource;
}
