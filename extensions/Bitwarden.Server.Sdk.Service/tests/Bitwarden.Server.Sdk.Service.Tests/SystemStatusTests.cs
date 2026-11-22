using System.Text.Json;

namespace Bitwarden.Server.Sdk.Service.Tests;

/// <summary>
/// A probe's outcome on the wire: <c>HEALTHY</c> with no detail, or <c>UNHEALTHY</c> saying why.
/// </summary>
public sealed class SystemStatusTests
{
    [Fact]
    public void Healthy_IsWrittenWithoutADetail()
    {
        var written = JsonSerializer.Serialize(SystemStatus.Healthy, JsonSerializerOptions.Web);

        Assert.Equal("""{"status":"HEALTHY"}""", written);
    }

    [Fact]
    public void Unhealthy_IsWrittenWithWhy()
    {
        var written = JsonSerializer.Serialize(SystemStatus.Unhealthy("The database is unreachable."), JsonSerializerOptions.Web);

        Assert.Equal("""{"status":"UNHEALTHY","detail":"The database is unreachable."}""", written);
    }

    [Fact]
    public void Healthy_IsOneValue()
    {
        Assert.Same(SystemStatus.Healthy, SystemStatus.Healthy);
        Assert.Equal(new SystemStatus("HEALTHY"), SystemStatus.Healthy);
    }
}
