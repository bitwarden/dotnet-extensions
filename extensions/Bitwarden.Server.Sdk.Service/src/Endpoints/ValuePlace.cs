namespace Bitwarden.Server.Sdk.Service.Endpoints;

/// <summary>
/// Which part of the request a simple value is read from.
/// </summary>
internal enum ValuePlace
{
    /// <summary>
    /// The route if the template names it, otherwise the query, as the framework decides.
    /// </summary>
    RouteOrQuery,
    Route,
    Query,
    Header,
}
