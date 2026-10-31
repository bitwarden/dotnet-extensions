using Bitwarden.Server.Sdk.Service.Endpoints;

namespace Bitwarden.Server.Sdk.Service.Tests.Endpoints;

/// <summary>
/// The marker is one shared instance, so an endpoint carries it once however many times the
/// framework builds it, and the startup check finds it by type.
/// </summary>
public sealed class StandardEndpointMetadataTests
{
    [Fact]
    public void Instance_IsShared()
    {
        Assert.Same(StandardEndpointMetadata.Instance, StandardEndpointMetadata.Instance);
        Assert.NotNull(StandardEndpointMetadata.Instance);
    }
}
