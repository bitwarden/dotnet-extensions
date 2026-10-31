using Microsoft.AspNetCore.Http;

namespace Bitwarden.Server.Sdk.Service.Endpoints;

/// <summary>
/// A response document whose links are built from the request it answers, when it is written.
/// </summary>
internal interface IDeferredLinks
{
    /// <summary>
    /// The document, with its links.
    /// </summary>
    object WithLinks(HttpRequest request);
}
