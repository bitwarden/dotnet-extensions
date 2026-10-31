using Bitwarden.Server.Sdk.Service.Endpoints;
using Bitwarden.Server.Sdk.Service.JsonApi;
using Microsoft.AspNetCore.Http;

namespace Bitwarden.Server.Sdk.Service.Tests.Endpoints;

/// <summary>
/// A create's <c>Location</c>, when the handler names none, is the request's own address and the
/// new resource's identifier.
/// </summary>
public sealed class StandardEndpointFilterTests
{
    [Fact]
    public void Succeeded_Created_LocationIsTheCollectionAndId()
    {
        var http = Created(pathBase: string.Empty);

        Assert.Equal("/api/v1/widgets/abc", http.Response.Headers.Location.ToString());
    }

    // A service mapped below a path base is reached through it, so the address must name it.
    [Fact]
    public void Succeeded_CreatedBelowAPathBase_LocationIncludesIt()
    {
        var http = Created(pathBase: "/vault");

        Assert.Equal("/vault/api/v1/widgets/abc", http.Response.Headers.Location.ToString());
    }

    private static DefaultHttpContext Created(string pathBase)
    {
        var http = new DefaultHttpContext();
        http.Request.PathBase = pathBase;
        http.Request.Path = "/api/v1/widgets/";

        StandardEndpointFilter.Succeeded(
            http, new ResourceDocument<Widget> { Data = new Widget { Id = "abc" } }, StatusCodes.Status201Created);

        return http;
    }
}
