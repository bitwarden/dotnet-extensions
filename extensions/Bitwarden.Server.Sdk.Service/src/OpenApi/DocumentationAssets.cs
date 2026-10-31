using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Bitwarden.Server.Sdk.Service.OpenApi;

/// <summary>
/// Serves the Bitwarden branding the documentation UI references.
/// </summary>
/// <remarks>
/// The assets are embedded in this assembly and served by the service itself, so a service gets
/// Bitwarden-branded documentation without copying files into its own <c>wwwroot</c>, and nothing is
/// fetched from an external origin to render it.
/// </remarks>
internal static class DocumentationAssets
{
    private static readonly Assembly Assembly = typeof(DocumentationAssets).Assembly;
    private const string ResourcePrefix = "Bitwarden.Server.Sdk.Service.Assets.";

    internal const string StylesheetPath = "/docs/bitwarden-swagger.css";
    internal const string LogoPath = "/docs/bitwarden-logo.svg";

    /// <summary>
    /// The shield alone, for placing beside a title rather than above it.
    /// </summary>
    internal const string ShieldPath = "/docs/bitwarden-shield.svg";
    internal const string FaviconPath = "/docs/favicon.ico";

    /// <summary>
    /// Maps the branding assets, excluded from the OpenAPI document.
    /// </summary>
    public static void MapDocumentationAssets(this IEndpointRouteBuilder endpoints)
    {
        Map(endpoints, StylesheetPath, "bitwarden-swagger.css", "text/css");
        Map(endpoints, LogoPath, "bitwarden-logo.svg", "image/svg+xml");
        Map(endpoints, ShieldPath, "bitwarden-shield.svg", "image/svg+xml");
        Map(endpoints, FaviconPath, "favicon.ico", "image/x-icon");
    }

    private static void Map(IEndpointRouteBuilder endpoints, string path, string fileName, string contentType)
    {
        endpoints.MapGet(path, () =>
        {
            var stream = Assembly.GetManifestResourceStream(ResourcePrefix + fileName);
            return stream is null ? Results.NotFound() : Results.Stream(stream, contentType);
        })
        .ExcludeFromDescription()
        .AllowAnonymous();
    }
}
