namespace Bitwarden.Server.Sdk.Resources.Endpoints;

/// <summary>
/// Marks an endpoint the standards apply to, for the check at startup.
/// </summary>
internal sealed class StandardEndpointMetadata
{
    public static readonly StandardEndpointMetadata Instance = new();
}
