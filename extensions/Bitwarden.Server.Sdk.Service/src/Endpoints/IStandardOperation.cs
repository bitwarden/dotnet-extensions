namespace Bitwarden.Server.Sdk.Service;

/// <summary>
/// Marks an action as one of the standard resource operations.
/// </summary>
/// <remarks>
/// Carrying one of these is what lets the platform configure an action by convention: its verb and
/// route, the status code it succeeds with, its operation identifier, and the response type shown
/// in the OpenAPI document. An action may still add <c>[EndpointName]</c> or
/// <c>[ProducesResponseType]</c> of its own; an explicit value always wins over the convention.
/// </remarks>
public interface IStandardOperation
{
    /// <summary>
    /// The status code this kind of operation returns when it succeeds.
    /// </summary>
    int SuccessStatusCode { get; }

    /// <summary>
    /// Whether the operation only reads.
    /// </summary>
    /// <remarks>
    /// What decides which of a service's two scopes it demands when it declares none of its
    /// own. Reads and searches are reads; everything else changes something, including a bulk
    /// form of it and an action whose body happens to be empty.
    /// </remarks>
    bool IsRead => false;
}
