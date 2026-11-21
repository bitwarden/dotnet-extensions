using System.Globalization;
using System.Security.Claims;
using Bitwarden.Server.Sdk.Context;
using Microsoft.AspNetCore.Http;

namespace Bitwarden.Server.Sdk.Authorization;

/// <summary>
/// What a Bitwarden client says about itself in its request: its device, its device type and its version.
/// </summary>
/// <remarks>
/// <para>
/// Read where the client's request arrives: by a product service for its <see cref="IContext"/>, and for
/// the feature flag context of a request a client made directly to a service that isn't one.
/// </para>
/// <para>
/// None of it is proof of anything, so nothing may be gated on it.
/// </para>
/// </remarks>
public static class ClientHeaders
{
    /// <summary>
    /// The header a Bitwarden client names its device type in, as a number or a name.
    /// </summary>
    internal const string DeviceTypeHeader = "Device-Type";

    /// <summary>
    /// The header a Bitwarden client names its device in.
    /// </summary>
    internal const string DeviceIdentifierHeader = "Device-Identifier";

    /// <summary>
    /// The header a Bitwarden client names its version in.
    /// </summary>
    internal const string ClientVersionHeader = "Bitwarden-Client-Version";

    /// <summary>
    /// The header a Bitwarden client says it is a prerelease in, with <c>1</c>.
    /// </summary>
    internal const string IsPrereleaseHeader = "Is-Prerelease";

    /// <summary>
    /// The claim a session token may name its device type in.
    /// </summary>
    internal const string DeviceTypeClaim = "devicetype";

    /// <summary>
    /// The claim naming the device a session was signed in on, as its client identifies it.
    /// </summary>
    internal const string DeviceClaim = "device";

    /// <summary>
    /// Bitwarden's device types by name, as the clients' <c>DeviceType</c> enumeration names them. A client
    /// may send either the name or the number, and the number is what is carried and targeted on.
    /// </summary>
    private static readonly Dictionary<string, byte> DeviceTypeNames = new(StringComparer.Ordinal)
    {
        ["Android"] = 0,
        ["iOS"] = 1,
        ["ChromeExtension"] = 2,
        ["FirefoxExtension"] = 3,
        ["OperaExtension"] = 4,
        ["EdgeExtension"] = 5,
        ["WindowsDesktop"] = 6,
        ["MacOsDesktop"] = 7,
        ["LinuxDesktop"] = 8,
        ["ChromeBrowser"] = 9,
        ["FirefoxBrowser"] = 10,
        ["OperaBrowser"] = 11,
        ["EdgeBrowser"] = 12,
        ["IEBrowser"] = 13,
        ["UnknownBrowser"] = 14,
        ["AndroidAmazon"] = 15,
        ["UWP"] = 16,
        ["SafariBrowser"] = 17,
        ["VivaldiBrowser"] = 18,
        ["VivaldiExtension"] = 19,
        ["SafariExtension"] = 20,
        ["SDK"] = 21,
        ["Server"] = 22,
        ["WindowsCLI"] = 23,
        ["MacOsCLI"] = 24,
        ["LinuxCLI"] = 25,
        ["DuckDuckGoBrowser"] = 26,
        ["DuckDuckGoExtension"] = 27,
    };

    /// <summary>
    /// The device type the client says it is, as its number: the <c>Device-Type</c> header, then the
    /// session's <c>devicetype</c> claim. Each is a device type's number or its name.
    /// </summary>
    /// <param name="http">The request.</param>
    /// <param name="session">The validated session, if there is one.</param>
    public static string? DeviceType(HttpContext http, ClaimsPrincipal? session)
    {
        if (DeviceTypeNumber(http.Request.Headers[DeviceTypeHeader].FirstOrDefault()) is { } fromHeader)
        {
            return fromHeader.ToString(CultureInfo.InvariantCulture);
        }

        return DeviceTypeNumber(session?.FindFirst(DeviceTypeClaim)?.Value) is { } fromClaim
            ? fromClaim.ToString(CultureInfo.InvariantCulture)
            : null;
    }

    /// <summary>
    /// A device type's number, from its number or its name; null for anything else.
    /// </summary>
    /// <param name="value">What the client sent.</param>
    public static byte? DeviceTypeNumber(string? value) =>
        byte.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
            ? number
            : value is not null && DeviceTypeNames.TryGetValue(value, out var named)
                ? named
                : null;

    /// <summary>
    /// The device the client is on: the session's signed <c>device</c> claim, then the client's
    /// <c>Device-Identifier</c> header, held to what <see cref="ContextHeader.IsDeviceIdentifier"/> allows.
    /// </summary>
    /// <param name="http">The request.</param>
    /// <param name="session">The validated session, if there is one.</param>
    public static string? DeviceIdentifier(HttpContext http, ClaimsPrincipal? session)
    {
        var value = session?.FindFirst(DeviceClaim)?.Value is { Length: > 0 } claimed
            ? claimed
            : http.Request.Headers[DeviceIdentifierHeader].FirstOrDefault();

        return ContextHeader.IsDeviceIdentifier(value) ? value : null;
    }

    /// <summary>
    /// The client's version, as <see cref="System.Version.ToString()"/> writes it, when the
    /// <c>Bitwarden-Client-Version</c> header is one <see cref="System.Version.TryParse(string?, out System.Version?)"/>
    /// reads and <see cref="ContextHeader.IsClientVersion"/> allows.
    /// </summary>
    /// <param name="http">The request.</param>
    public static string? ClientVersion(HttpContext http) =>
        System.Version.TryParse(http.Request.Headers[ClientVersionHeader].FirstOrDefault(), out var version)
        && version.ToString() is var written
        && ContextHeader.IsClientVersion(written)
            ? written
            : null;

    /// <summary>
    /// Whether the client says it is a prerelease: <c>Is-Prerelease</c> is <c>1</c>. Only alongside a
    /// version (<see cref="ClientVersion"/>).
    /// </summary>
    /// <param name="http">The request.</param>
    public static bool IsPrerelease(HttpContext http) =>
        ClientVersion(http) is not null && http.Request.Headers[IsPrereleaseHeader].FirstOrDefault() == "1";
}
