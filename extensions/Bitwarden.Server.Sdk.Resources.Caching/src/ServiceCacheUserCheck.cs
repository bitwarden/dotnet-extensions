namespace Bitwarden.Server.Sdk.Resources.Caching;

/// <summary>
/// Checks the service cache's login as the host starts, so a deployment whose connection names no Redis user of the
/// service's own fails to start (<see cref="ServiceCacheAcl.RequireUser"/>).
/// </summary>
internal sealed class ServiceCacheUserCheck
{
    /// <summary>
    /// Whether the connection names a user of the service's own; false where none is configured but none is required.
    /// </summary>
    public bool OwnUser { get; set; }
}
