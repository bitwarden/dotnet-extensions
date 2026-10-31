using Bitwarden.Server.Sdk.Service.JsonApi;
using Bitwarden.Server.Sdk.Service.Querying;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace Bitwarden.Server.Sdk.Service.Tests.Querying;

/// <summary>
/// A sort names only fields the resource marks sortable, each once, and names at least one.
/// </summary>
public sealed class SortBindingTests
{
    [Fact]
    public void Bind_SortableFields_AreReadInOrderUnderTheirPublishedNames()
    {
        var actual = SortBinding.Bind(Query("-CREATEDAT,name"), typeof(Item));

        Assert.Equal("-createdAt,name", actual?.ToString());
    }

    [Fact]
    public void Bind_Absent_IsNull()
    {
        Assert.Null(SortBinding.Bind(new QueryCollection(), typeof(Item)));
    }

    [Fact]
    public void Bind_FieldNotMarkedSortable_IsRefused()
    {
        var actual = Assert.Throws<InvalidSortException>(() => SortBinding.Bind(Query("secret"), typeof(Item)));

        Assert.Contains("'secret' is not a field this collection sorts by", actual.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Bind_FieldNamedTwice_IsRefused()
    {
        var actual = Assert.Throws<InvalidSortException>(() => SortBinding.Bind(Query("name,-Name"), typeof(Item)));

        Assert.Contains("'name' is named more than once", actual.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(",")]
    [InlineData(" , ")]
    public void Bind_NoField_IsRefused(string sort)
    {
        var actual = Assert.Throws<InvalidSortException>(() => SortBinding.Bind(Query(sort), typeof(Item)));

        Assert.Contains("names no field", actual.Message, StringComparison.Ordinal);
    }

    private static QueryCollection Query(string sort) =>
        new(new Dictionary<string, StringValues> { [Sort.Parameter] = sort });

    [Sortable(nameof(CreatedAt), Default = true)]
    private sealed record Item : Resource
    {
        [Sortable]
        public string? Name { get; init; }

        public string? Secret { get; init; }
    }
}
