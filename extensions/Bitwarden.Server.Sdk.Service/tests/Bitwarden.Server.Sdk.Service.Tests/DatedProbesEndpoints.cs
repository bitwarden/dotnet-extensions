using Bitwarden.Server.Sdk.Resources;
using Microsoft.AspNetCore.Builder;

namespace Bitwarden.Server.Sdk.Service.Tests;

/// <summary>
/// Answers one <see cref="DatedProbe"/>.
/// </summary>
public static class DatedProbesEndpoints
{
    /// <summary>
    /// Maps the probe.
    /// </summary>
    /// <param name="endpoints">The service's endpoints.</param>
    public static void Map(Microsoft.AspNetCore.Routing.IEndpointRouteBuilder endpoints) =>
        endpoints.MapGroup("api/v1/dated-probes").WithStandards().MapGet("current", GetCurrent);

    /// <summary>
    /// The probe
    /// </summary>
    /// <returns>The probe</returns>
    [StandardReadOne("current")]
    public static ResourceDocument<DatedProbe> GetCurrent() =>
        StandardResponses.ReadOneResponse(new DatedProbe
        {
            Id = "current",
            At = new DateTimeOffset(2026, 10, 11, 14, 0, 0, TimeSpan.FromHours(2)).AddTicks(1_234_567),
            Whole = new DateTimeOffset(2026, 10, 11, 12, 0, 0, TimeSpan.Zero),
        });
}
