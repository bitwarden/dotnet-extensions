using System.Text.Json.Serialization;
using Bitwarden.Server.Sdk.Service.JsonApi;
using Bitwarden.Server.Sdk.Service.Querying;

namespace Bitwarden.Server.Sdk.Service.Tests.Querying;

/// <summary>
/// A resource's sorting contract as read from its declarations: the fields under their published
/// names, exactly one default, and every declaration that cannot mean anything refused at startup
/// rather than at the request that trips over it.
/// </summary>
public sealed class SortableFieldsTests
{
    [Fact]
    public void Of_Declared_AreAlphabeticalUnderTheirPublishedNames()
    {
        var actual = SortableFields.Of(typeof(Item));

        Assert.Equal(["createdAt", "display_name", "owner"], actual.Select(field => field.Name));
    }

    [Fact]
    public void Of_Declared_CarryDirectionDefaultAndWhereEachIsStored()
    {
        var actual = SortableFields.Of(typeof(Item)).ToDictionary(field => field.Name);

        Assert.True(actual["createdAt"].Default);
        Assert.Equal(SortDirection.Descending, actual["createdAt"].Direction);
        Assert.Equal(nameof(Item.CreatedAt), actual["createdAt"].Stored);
        Assert.Equal("OwnerId", actual["owner"].Stored);
        Assert.Equal(SortDirection.Ascending, actual["owner"].Direction);
        Assert.Equal(nameof(Item.Label), actual["display_name"].Property.Name);
    }

    [Fact]
    public void Of_IsReadOncePerResource()
    {
        Assert.Same(SortableFields.Of(typeof(Item)), SortableFields.Of(typeof(Item)));
    }

    [Fact]
    public void Of_NothingDeclared_IsEmpty()
    {
        Assert.Empty(SortableFields.Of(typeof(Unsorted)));
    }

    [Fact]
    public void DefaultOf_IsTheDefaultFieldInItsDirection_ForThatResource()
    {
        var actual = SortableFields.DefaultOf(typeof(Item));

        Assert.Equal([SortBy.Descending("createdAt")], actual.Fields);
        Assert.Equal(typeof(Item), actual.Resource);
    }

    [Fact]
    public void DefaultOf_NothingDeclared_ThrowsNamingTheResource()
    {
        var actual = Assert.Throws<InvalidOperationException>(() => SortableFields.DefaultOf(typeof(Unsorted)));

        Assert.Contains("'Unsorted' declares no sortable fields", actual.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(typeof(PagedResourceListDocument<Item>))]
    [InlineData(typeof(CursorPagedResourceListDocument<Item>))]
    [InlineData(typeof(ResourceListDocument<Item>))]
    [InlineData(typeof(ResourceDocument<Item>))]
    [InlineData(typeof(Task<PagedResourceListDocument<Item>>))]
    [InlineData(typeof(ValueTask<ResourceListDocument<Item>>))]
    public void ResourceOf_ADocument_IsItsResource(Type returnType)
    {
        Assert.Equal(typeof(Item), SortableFields.ResourceOf(returnType));
    }

    [Theory]
    [InlineData(typeof(void))]
    [InlineData(typeof(Item))]
    [InlineData(typeof(Task))]
    [InlineData(typeof(Task<int>))]
    [InlineData(typeof(List<Item>))]
    [InlineData(typeof(Task<List<Item>>))]
    public void ResourceOf_AnythingElse_IsNull(Type returnType)
    {
        Assert.Null(SortableFields.ResourceOf(returnType));
    }

    [Theory]
    [InlineData(typeof(PropertyNamingAField), "only the resource may name a field")]
    [InlineData(typeof(ResourceNamingNoField), "without naming a field")]
    [InlineData(typeof(ResourceNamingNoSuchProperty), "has no such property")]
    [InlineData(typeof(DeclaredTwice), "declares 'name' sortable more than once")]
    [InlineData(typeof(NoDefault), "marks 0 of its sortable fields")]
    [InlineData(typeof(TwoDefaults), "marks 2 of its sortable fields")]
    public void Of_DeclarationThatCannotMeanAnything_Throws(Type resource, string expected)
    {
        var actual = Assert.Throws<InvalidOperationException>(() => SortableFields.Of(resource));

        Assert.Contains(expected, actual.Message, StringComparison.Ordinal);
    }

    [Sortable(nameof(CreatedAt), Default = true, Descending = true)]
    private sealed record Item : Resource
    {
        [Sortable(MappedTo = "OwnerId")]
        public string? Owner { get; init; }

        [Sortable]
        [JsonPropertyName("display_name")]
        public string? Label { get; init; }

        public string? Unsortable { get; init; }
    }

    private sealed record Unsorted : Resource
    {
        public string? Name { get; init; }
    }

    private sealed record PropertyNamingAField : Resource
    {
        [Sortable("name", Default = true)]
        public string? Name { get; init; }
    }

    [Sortable]
    private sealed record ResourceNamingNoField : Resource
    {
        [Sortable(Default = true)]
        public string? Name { get; init; }
    }

    [Sortable("Nope")]
    private sealed record ResourceNamingNoSuchProperty : Resource
    {
        [Sortable(Default = true)]
        public string? Name { get; init; }
    }

    [Sortable(nameof(Name))]
    private sealed record DeclaredTwice : Resource
    {
        [Sortable(Default = true)]
        public string? Name { get; init; }
    }

    private sealed record NoDefault : Resource
    {
        [Sortable]
        public string? Name { get; init; }
    }

    private sealed record TwoDefaults : Resource
    {
        [Sortable(Default = true)]
        public string? Name { get; init; }

        [Sortable(Default = true)]
        public string? Label { get; init; }
    }
}
