using Bitwarden.Server.Sdk.Service.JsonApi;
using Bitwarden.Server.Sdk.Service.Querying;
using Microsoft.AspNetCore.Http;

namespace Bitwarden.Server.Sdk.Service.Endpoints;

/// <summary>
/// Builds a page's links from the request's own URL.
/// </summary>
internal static class PageLinkBuilder
{
    /// <summary>
    /// The links from one page of a collection to the rest of it.
    /// </summary>
    public static PageLinks Links(HttpRequest request, Page requested, int last) =>
        new()
        {
            First = Link(request, requested, 1),
            Prev = requested.Number > 1 ? Link(request, requested, requested.Number - 1) : null,
            Next = requested.Number < last ? Link(request, requested, requested.Number + 1) : null,
            Last = Link(request, requested, last),
        };

    /// <summary>
    /// One page's link: the request's own address and query, with the page replaced.
    /// </summary>
    /// <remarks>
    /// The address includes the path base, so a link is right for a service mapped below one.
    /// </remarks>
    public static string Link(HttpRequest request, Page requested, int number)
    {
        var query = request.Query
            .Where(parameter => parameter.Key is not ("page[number]" or "page[size]"))
            .SelectMany(parameter => parameter.Value.Select(value =>
                $"{Uri.EscapeDataString(parameter.Key)}={Uri.EscapeDataString(value ?? string.Empty)}"))
            .Append($"page[number]={number}")
            .Append($"page[size]={requested.Size}");

        return $"{request.PathBase.Add(request.Path)}?{string.Join('&', query)}";
    }
}
