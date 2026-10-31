using System.Text.Json;
using System.Text.Json.Serialization;
using Bitwarden.Server.Sdk.Service.Endpoints;
using Bitwarden.Server.Sdk.Service.JsonApi;
using Bitwarden.Server.Sdk.Service.Querying;
using Microsoft.AspNetCore.Http;

namespace Bitwarden.Server.Sdk.Service.Tests.JsonApi;

/// <summary>
/// The wire shape of a page, and the links it gains from the request it answers.
/// </summary>
public sealed class PagedResourceListDocumentTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new ResourceDocumentJsonConverterFactory() },
    };

    [Fact]
    public void Serialize_APage_DataMetaAndLinks()
    {
        var document = new PagedResourceListDocument<Widget>
        {
            Data = [new Widget { Id = "1", Name = "a" }],
            Meta = new Dictionary<string, object?> { ["pageCount"] = 3, ["pageNumber"] = 2, ["pageSize"] = 1, ["totalCount"] = 3 },
            Links = new PageLinks { First = "f", Prev = "p", Next = "n", Last = "l" },
        };

        Assert.Equal(
            """{"data":[{"id":"1","type":"widget","attributes":{"name":"a"}}],"meta":{"pageCount":3,"pageNumber":2,"pageSize":1,"totalCount":3},"links":{"first":"f","prev":"p","next":"n","last":"l"}}""",
            JsonSerializer.Serialize(document, Options));
    }

    [Fact]
    public void Serialize_PendingLinksAndFields_NeverOnTheWire()
    {
        var document = new PagedResourceListDocument<Widget>
        {
            Data = [new Widget { Id = "1", Name = "a", Size = 1 }],
            Fields = new HashSet<string> { "name" },
            PendingLinks = (new Page { Number = 1, Size = 1 }, 1),
        };

        Assert.Equal(
            """{"data":[{"id":"1","type":"widget","attributes":{"name":"a"}}]}""",
            JsonSerializer.Serialize(document, Options));
    }

    [Fact]
    public void WithLinks_Pending_LinksBuiltFromTheRequest_PendingCleared()
    {
        var document = new PagedResourceListDocument<Widget>
        {
            Data = [],
            PendingLinks = (new Page { Number = 2, Size = 10 }, 3),
        };

        var actual = Assert.IsType<PagedResourceListDocument<Widget>>(((IDeferredLinks)document).WithLinks(Request()));

        Assert.Null(actual.PendingLinks);
        Assert.Equal(
            new PageLinks
            {
                First = "/widgets?filter=x&page[number]=1&page[size]=10",
                Prev = "/widgets?filter=x&page[number]=1&page[size]=10",
                Next = "/widgets?filter=x&page[number]=3&page[size]=10",
                Last = "/widgets?filter=x&page[number]=3&page[size]=10",
            },
            actual.Links);
    }

    [Fact]
    public void WithLinks_NothingPending_TheSameDocument()
    {
        var document = new PagedResourceListDocument<Widget> { Data = [] };

        Assert.Same(document, ((IDeferredLinks)document).WithLinks(Request()));
    }

    [Fact]
    public void Fields_AreTheSparseDocumentsFields()
    {
        var fields = new HashSet<string> { "name" };

        ISparseDocument document = new PagedResourceListDocument<Widget> { Data = [], Fields = fields };

        Assert.Same(fields, document.Fields);
    }

    private static HttpRequest Request()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/widgets";
        context.Request.QueryString = new QueryString("?filter=x&page[number]=2&page[size]=10");

        return context.Request;
    }

    public sealed record Widget : Resource
    {
        public string? Name { get; init; }

        public int? Size { get; init; }
    }
}
