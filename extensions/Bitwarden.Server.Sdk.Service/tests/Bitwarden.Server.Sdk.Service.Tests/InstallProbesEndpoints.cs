using Bitwarden.Server.Sdk.Resources;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace Bitwarden.Server.Sdk.Service.Tests;

/// <summary>
/// One operation a self-hosted install has, and one a cloud install has.
/// </summary>
public static class InstallProbesEndpoints
{
    /// <summary>
    /// Maps the probes.
    /// </summary>
    /// <param name="endpoints">The service's endpoints.</param>
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var probes = endpoints.MapGroup("api/v1/install-probes").WithStandards();
        probes.MapGet("self-hosted-only", GetSelfHostedOnly);
        probes.MapGet("cloud-only", GetCloudOnly);
    }

    /// <summary>
    /// Only on a self-hosted install
    /// </summary>
    /// <returns>The probe</returns>
    [StandardReadOne("self-hosted-only")]
    [SelfHostedOnly]
    public static ResourceDocument<InstallProbe> GetSelfHostedOnly() =>
        StandardResponses.ReadOneResponse(new InstallProbe { Id = "self-hosted-only" });

    /// <summary>
    /// Only on a cloud install
    /// </summary>
    /// <returns>The probe</returns>
    [StandardReadOne("cloud-only")]
    [NotSelfHostedOnly]
    public static ResourceDocument<InstallProbe> GetCloudOnly() =>
        StandardResponses.ReadOneResponse(new InstallProbe { Id = "cloud-only" });
}
