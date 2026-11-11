using Bitwarden.Server.Sdk.Resources;
using Bitwarden.Server.Sdk.Resources.Querying;

namespace Bitwarden.Server.Sdk.Service.Tests.Endpoints;

/// <summary>
/// Handlers taking an expression, with and without the attribute that checks it.
/// </summary>
public static class FilteredEndpoints
{
    /// <summary>
    /// Find widgets
    /// </summary>
    /// <param name="request">The search.</param>
    /// <returns>Nothing</returns>
    [StandardAction]
    public static string FindWidgets(Search<Widget> request) => string.Empty;

    /// <summary>
    /// Tidy widgets
    /// </summary>
    /// <param name="request">Which to remove.</param>
    /// <returns>Nothing</returns>
    [StandardAction]
    public static string TidyWidgets(BulkDelete<Widget> request) => string.Empty;

    /// <summary>
    /// Match widgets
    /// </summary>
    /// <param name="request">The expression.</param>
    /// <returns>Nothing</returns>
    [StandardSearch("api/v1/widgets")]
    public static string MatchWidgets(Query request) => string.Empty;

    /// <summary>
    /// Search widgets
    /// </summary>
    /// <param name="request">The search.</param>
    /// <returns>The widgets</returns>
    [StandardSearch("api/v1/widgets")]
    public static ResourceListDocument<Widget> SearchWidgets(Search<Widget> request) =>
        new() { Data = [] };
}
