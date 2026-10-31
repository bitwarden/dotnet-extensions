using System.Diagnostics;
using Bitwarden.Server.Sdk.Service.JsonApi;
using Bitwarden.Server.Sdk.Service.Querying;

namespace Bitwarden.Server.Sdk.Service.Tests.Endpoints;

/// <summary>
/// The response documents a handler returns: the resource or resources, the caller's meta, the
/// standards' paging fields and the sparse fieldset the operation was asked for.
/// </summary>
public sealed class StandardResponsesTests
{
    private static readonly Answered First = new() { Id = "a1" };
    private static readonly Answered Second = new() { Id = "a2" };

    [Fact]
    public async Task ServedByPlatform_Called_RefusesNamingTheResource()
    {
        var actual = await Assert.ThrowsAsync<UnreachableException>(StandardResponses.ServedByPlatform<Answered>);

        Assert.Contains("Answered is read by identifier by the platform", actual.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateResponse_Location_Kept()
    {
        var meta = new Dictionary<string, object?> { ["note"] = "n" };

        var actual = StandardResponses.CreateResponse(First, "/api/v1/answered/a1", meta);

        Assert.Same(First, actual.Data);
        Assert.Equal("/api/v1/answered/a1", actual.Location);
        Assert.Same(meta, actual.Meta);
    }

    // The platform derives the address when the handler names none, empty included.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void CreateResponse_NoLocation_LeftToThePlatform(string? location)
    {
        Assert.Null(StandardResponses.CreateResponse(First, location).Location);
    }

    [Theory]
    [InlineData(0, 10, 1)]
    [InlineData(1, 10, 1)]
    [InlineData(10, 10, 1)]
    [InlineData(11, 10, 2)]
    [InlineData(95, 10, 10)]
    public void ReadManyResponse_Offset_PageCountAtLeastOne(int total, int size, int pageCount)
    {
        var actual = StandardResponses.ReadManyResponse(new Paged<Answered>([], total), new Page { Number = 1, Size = size });

        Assert.Equal(pageCount, actual.Meta!["pageCount"]);
    }

    [Fact]
    public void ReadManyResponse_Offset_PagingFieldsWithTheCallersMetaAndPendingLinks()
    {
        var meta = new Dictionary<string, object?> { ["note"] = "n" };
        var requested = new Page { Number = 2, Size = 1 };

        var actual = StandardResponses.ReadManyResponse(new Paged<Answered>([Second], 3), requested, meta);

        Assert.Equal([Second], actual.Data);
        Assert.Equal("n", actual.Meta!["note"]);
        Assert.Equal(3, actual.Meta["pageCount"]);
        Assert.Equal(2, actual.Meta["pageNumber"]);
        Assert.Equal(1, actual.Meta["pageSize"]);
        Assert.Equal(3, actual.Meta["totalCount"]);
        Assert.Equal((requested, 3), actual.PendingLinks);
        Assert.Null(actual.Fields);
        Assert.False(meta.ContainsKey("pageCount"));
    }

    [Fact]
    public void ReadManyResponse_OffsetWithFields_SparseFieldset()
    {
        var actual = StandardResponses.ReadManyResponse(
            new Paged<Answered>([First], 1), new Page(), new Fields<Answered>(["note"]));

        Assert.Equal(["note"], actual.Fields!);
    }

    [Fact]
    public void ReadManyResponse_OffsetWithoutTheRequest_Refused()
    {
        Assert.Throws<ArgumentNullException>(() => StandardResponses.ReadManyResponse(new Paged<Answered>([], 0), null!));
    }

    [Fact]
    public void ReadManyResponse_CursorWithMore_TheCursor()
    {
        var meta = new Dictionary<string, object?> { ["note"] = "n" };

        var actual = StandardResponses.ReadManyResponse(new CursorPaged<Answered>([First], "next"), new CursorPage { Size = 1 }, meta);

        Assert.Equal([First], actual.Data);
        Assert.Equal("n", actual.Meta!["note"]);
        Assert.Equal(1, actual.Meta["pageSize"]);
        Assert.Equal(true, actual.Meta["hasMore"]);
        Assert.Equal("next", actual.Meta["cursor"]);
        Assert.False(meta.ContainsKey("hasMore"));
    }

    [Fact]
    public void ReadManyResponse_CursorAtTheEnd_NoCursor()
    {
        var actual = StandardResponses.ReadManyResponse(new CursorPaged<Answered>([First], null), new CursorPage());

        Assert.Equal(false, actual.Meta!["hasMore"]);
        Assert.False(actual.Meta.ContainsKey("cursor"));
        Assert.Equal(Page.DefaultSize, actual.Meta["pageSize"]);
    }

    [Fact]
    public void ReadManyResponse_CursorWithoutTheRequest_Refused()
    {
        Assert.Throws<ArgumentNullException>(() => StandardResponses.ReadManyResponse(new CursorPaged<Answered>([], null), null!));
    }

    [Fact]
    public void ReadManyResponse_List_TheResourcesAndMeta()
    {
        var meta = new Dictionary<string, object?> { ["note"] = "n" };

        var actual = StandardResponses.ReadManyResponse([First, Second], meta);

        Assert.Equal([First, Second], actual.Data);
        Assert.Same(meta, actual.Meta);
    }

    [Fact]
    public void ReadOneResponse_Resource_ItAndMetaWithEveryField()
    {
        var meta = new Dictionary<string, object?> { ["note"] = "n" };

        var actual = StandardResponses.ReadOneResponse(First, meta);

        Assert.Same(First, actual.Data);
        Assert.Same(meta, actual.Meta);
        Assert.Null(actual.Fields);
    }

    [Fact]
    public void ReadOneResponse_WithFields_SparseFieldset()
    {
        var actual = StandardResponses.ReadOneResponse(First, new Fields<Answered>(["note"]));

        Assert.Same(First, actual.Data);
        Assert.Equal(["note"], actual.Fields!);
    }

    [Fact]
    public void ReadOneResponse_WithDefaultFields_EveryField()
    {
        Assert.Null(StandardResponses.ReadOneResponse(First, default(Fields<Answered>)).Fields);
    }

    [Fact]
    public void ActionResponse_Resource_ItAndMeta()
    {
        var meta = new Dictionary<string, object?> { ["note"] = "n" };

        var actual = StandardResponses.ActionResponse(First, meta);

        Assert.Same(First, actual.Data);
        Assert.Same(meta, actual.Meta);
    }

    [Fact]
    public void UpdateResponse_Resource_ItAndMeta()
    {
        var meta = new Dictionary<string, object?> { ["note"] = "n" };

        var actual = StandardResponses.UpdateResponse(First, meta);

        Assert.Same(First, actual.Data);
        Assert.Same(meta, actual.Meta);
    }

    /// <summary>
    /// A resource the responses carry.
    /// </summary>
    public sealed record Answered : Resource
    {
        /// <summary>
        /// A note.
        /// </summary>
        public string? Note { get; init; }
    }
}
