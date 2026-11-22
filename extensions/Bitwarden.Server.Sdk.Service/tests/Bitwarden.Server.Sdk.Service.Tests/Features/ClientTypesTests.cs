using Bitwarden.Server.Sdk.Service.Features;

namespace Bitwarden.Server.Sdk.Service.Tests.Features;

/// <summary>
/// Each device type Bitwarden's clients send maps to the client type feature flags are targeted on; these are wire
/// values, and an unknown device is every client.
/// </summary>
public sealed class ClientTypesTests
{
    [Theory]
    // Android, iOS, AndroidAmazon.
    [InlineData(0, ClientTypes.Mobile)]
    [InlineData(1, ClientTypes.Mobile)]
    [InlineData(15, ClientTypes.Mobile)]
    // Browser extensions.
    [InlineData(2, ClientTypes.Browser)]
    [InlineData(3, ClientTypes.Browser)]
    [InlineData(4, ClientTypes.Browser)]
    [InlineData(5, ClientTypes.Browser)]
    [InlineData(19, ClientTypes.Browser)]
    [InlineData(20, ClientTypes.Browser)]
    [InlineData(27, ClientTypes.Browser)]
    // Desktop apps.
    [InlineData(6, ClientTypes.Desktop)]
    [InlineData(7, ClientTypes.Desktop)]
    [InlineData(8, ClientTypes.Desktop)]
    [InlineData(16, ClientTypes.Desktop)]
    // The web vault, in any browser.
    [InlineData(9, ClientTypes.Web)]
    [InlineData(10, ClientTypes.Web)]
    [InlineData(11, ClientTypes.Web)]
    [InlineData(12, ClientTypes.Web)]
    [InlineData(13, ClientTypes.Web)]
    [InlineData(14, ClientTypes.Web)]
    [InlineData(17, ClientTypes.Web)]
    [InlineData(18, ClientTypes.Web)]
    [InlineData(26, ClientTypes.Web)]
    // The command line.
    [InlineData(23, ClientTypes.Cli)]
    [InlineData(24, ClientTypes.Cli)]
    [InlineData(25, ClientTypes.Cli)]
    // Anything else, the SDK and server among them, is every client.
    [InlineData(21, ClientTypes.All)]
    [InlineData(22, ClientTypes.All)]
    [InlineData(-1, ClientTypes.All)]
    [InlineData(999, ClientTypes.All)]
    public void For_EachDeviceType_ItsClientType(int deviceType, int clientType) =>
        Assert.Equal(clientType, ClientTypes.For(deviceType));

    // The client types are wire values flags are targeted on: they never move.
    [Fact]
    public void TheClientTypes_KeepTheirWireValues() =>
        Assert.Equal(
            [0, 1, 2, 3, 4, 5],
            [ClientTypes.All, ClientTypes.Web, ClientTypes.Browser, ClientTypes.Desktop, ClientTypes.Mobile, ClientTypes.Cli]);
}
