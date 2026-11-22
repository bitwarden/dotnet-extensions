namespace Bitwarden.Server.Sdk.Service.Features;

/// <summary>
/// Which kind of client a device type is.
/// </summary>
/// <remarks>
/// Kept as numbers rather than enumerations. The values are Bitwarden's <c>DeviceType</c> and
/// <c>ClientType</c>, which are wire values the clients send and LaunchDarkly targets on, so they
/// do not move.
/// </remarks>
internal static class ClientTypes
{
    internal const int All = 0;
    internal const int Web = 1;
    internal const int Browser = 2;
    internal const int Desktop = 3;
    internal const int Mobile = 4;
    internal const int Cli = 5;

    // Android, iOS, AndroidAmazon.
    private static readonly HashSet<int> MobileTypes = [0, 1, 15];

    // LinuxDesktop, MacOsDesktop, WindowsDesktop, UWP.
    private static readonly HashSet<int> DesktopTypes = [8, 7, 6, 16];

    // WindowsCLI, MacOsCLI, LinuxCLI.
    private static readonly HashSet<int> CliTypes = [23, 24, 25];

    // Chrome, Firefox, Opera, Edge, Vivaldi, Safari and DuckDuckGo extensions.
    private static readonly HashSet<int> BrowserExtensionTypes = [2, 3, 4, 5, 19, 20, 27];

    // Chrome, Firefox, Opera, Edge, IE, Safari, Vivaldi, DuckDuckGo and unknown browsers.
    private static readonly HashSet<int> BrowserTypes = [9, 10, 11, 12, 13, 17, 18, 26, 14];

    public static int For(int deviceType) => deviceType switch
    {
        _ when MobileTypes.Contains(deviceType) => Mobile,
        _ when DesktopTypes.Contains(deviceType) => Desktop,
        _ when CliTypes.Contains(deviceType) => Cli,
        _ when BrowserExtensionTypes.Contains(deviceType) => Browser,
        _ when BrowserTypes.Contains(deviceType) => Web,
        _ => All,
    };
}
