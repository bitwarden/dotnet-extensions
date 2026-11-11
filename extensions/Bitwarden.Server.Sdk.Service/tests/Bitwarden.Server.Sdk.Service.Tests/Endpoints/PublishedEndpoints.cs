using Bitwarden.Server.Sdk.Resources;

namespace Bitwarden.Server.Sdk.Service.Tests.Endpoints;

/// <summary>
/// One standard read.
/// </summary>
public static class PublishedEndpoints
{
    /// <summary>
    /// Read a widget
    /// </summary>
    /// <param name="id">The widget.</param>
    /// <returns>The widget</returns>
    [StandardReadOne]
    public static ResourceDocument<Widget> GetWidget(string id) =>
        StandardResponses.ReadOneResponse(new Widget { Id = id });
}
