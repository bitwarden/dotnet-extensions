using Bitwarden.Server.Sdk.Service.Endpoints;
using Bitwarden.Server.Sdk.Service.Querying;
using Microsoft.AspNetCore.Http;

namespace Bitwarden.Server.Sdk.Service.Tests.Endpoints;

/// <summary>
/// A page's links lead back to the same address and query, with only the page replaced.
/// </summary>
public sealed class PageLinkBuilderTests
{
    [Fact]
    public void Link_RequestQuery_KeptWithThePageReplaced()
    {
        var request = Request(pathBase: string.Empty, "?sort=-name&page[number]=2&page[size]=10");

        var link = PageLinkBuilder.Link(request, new Page { Number = 2, Size = 10 }, 3);

        Assert.Equal("/api/v1/items?sort=-name&page[number]=3&page[size]=10", link);
    }

    // A service mapped below a path base is reached through it, so its links must name it.
    [Fact]
    public void Link_ServiceBelowAPathBase_LinkIncludesIt()
    {
        var request = Request(pathBase: "/vault", "?page[number]=1");

        var links = PageLinkBuilder.Links(request, new Page { Number = 1, Size = 10 }, 2);

        Assert.Equal("/vault/api/v1/items?page[number]=1&page[size]=10", links.First);
        Assert.Equal("/vault/api/v1/items?page[number]=2&page[size]=10", links.Next);
    }

    private static HttpRequest Request(string pathBase, string query)
    {
        var http = new DefaultHttpContext();
        http.Request.PathBase = pathBase;
        http.Request.Path = "/api/v1/items";
        http.Request.QueryString = new QueryString(query);

        return http.Request;
    }
}
