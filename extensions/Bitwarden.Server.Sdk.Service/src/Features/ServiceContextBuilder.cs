using System.Text.RegularExpressions;
using Bitwarden.Server.Sdk.Common;
using Bitwarden.Server.Sdk.Features;
using LaunchDarkly.Sdk;
using Microsoft.AspNetCore.Http;
using LdContext = LaunchDarkly.Sdk.Context;

namespace Bitwarden.Server.Sdk.Service.Features;

/// <summary>
/// Builds the LaunchDarkly context a request's feature flags are evaluated for.
/// </summary>
/// <remarks>
/// <para>
/// Kinds, keys and attribute names are fixed and shared by every Bitwarden server that evaluates
/// flags, so one LaunchDarkly project targets them all and a person sees the same flags whichever
/// server they reached:
/// </para>
/// <list type="bullet">
///   <item><description>
///     Outside a request, a single anonymous context under the shared anonymous key.
///   </description></item>
///   <item><description>
///     Inside one, a multi-context. A <c>device</c> context keyed by the device identifier,
///     when there is one. A user context keyed by the acting user, or by the shared anonymous
///     key, marked anonymous, when the request has none.
///   </description></item>
///   <item><description>
///     On each of them, <c>client-version</c> and <c>client-version-is-prerelease</c> from the
///     client's headers, and <c>device-type</c> and <c>client-type</c> from the device type.
///   </description></item>
///   <item><description>
///     On the user context, <c>organizations</c>: every organization the person is a confirmed
///     member of (<see cref="IContext.UserOrganizationIds"/>), from the token's <c>organizations</c>
///     claim, which Identity puts on every product token. Below the product layer the person's
///     token does not travel, and the list is the one the product service carried on its call.
///     Absent when there are none.
///   </description></item>
/// </list>
/// <para>
/// <b>What it leaves out.</b> There are no organization, service-account or Send contexts: Identity
/// issues those credentials (an organization's API key, a machine account's access token, a Send
/// access token), but this builder makes only the device and user contexts.
/// </para>
/// <para>
/// <b>What leaves the platform.</b> The context is sent to LaunchDarkly when an SDK key is
/// configured: the user's identifier, the device's identifier, and the
/// client's version and type, and the organizations the person belongs to, by identifier. No
/// Vault Data, and nothing a client sent as free text. The device identifier is held to the same
/// short pattern <see cref="IContext.DeviceIdentifier"/> applies, and dropped otherwise, so a
/// header cannot carry arbitrary text to a third party.
/// </para>
/// </remarks>
/// <param name="accessor">Supplies the request being handled.</param>
/// <param name="context">The acting user.</param>
internal sealed partial class ServiceContextBuilder(IHttpContextAccessor accessor, IContext context) : IContextBuilder
{
    /// <summary>
    /// The shared key for every evaluation made for nobody in particular.
    /// </summary>
    internal const string AnonymousKey = "25a15cac-58cf-4ac0-ad0f-b17c4bd92294";

    internal const string DeviceKind = "device";

    internal const string ClientVersionAttribute = "client-version";
    internal const string ClientVersionIsPrereleaseAttribute = "client-version-is-prerelease";
    internal const string DeviceTypeAttribute = "device-type";
    internal const string ClientTypeAttribute = "client-type";
    internal const string OrganizationsAttribute = "organizations";

    /// <summary>
    /// The claim a session token names its device in.
    /// </summary>
    internal const string DeviceClaim = "device";

    /// <summary>
    /// The claim a session token may name the device type in. When it is absent, the device type
    /// comes from the <c>Device-Type</c> header alone.
    /// </summary>
    internal const string DeviceTypeClaim = "devicetype";

    internal const string DeviceIdentifierHeader = "Device-Identifier";
    internal const string DeviceTypeHeader = "Device-Type";
    internal const string ClientVersionHeader = "Bitwarden-Client-Version";
    internal const string IsPrereleaseHeader = "Is-Prerelease";

    /// <inheritdoc />
    public LdContext Build()
    {
        if (accessor.HttpContext is not { } http)
        {
            return LdContext.Builder(AnonymousKey)
                .Kind(ContextKind.Default)
                .Anonymous(true)
                .Build();
        }

        var client = Client.Read(http);
        var builder = LdContext.MultiBuilder();

        if (DeviceIdentifier(http) is { } device)
        {
            var ldDevice = LdContext.Builder(device).Kind(DeviceKind);
            client.Apply(ldDevice);
            builder.Add(ldDevice.Build());
        }

        // One user context for a person's session and for a request with no session alike.
        var ldUser = context.UserId is { } user
            ? LdContext.Builder(user.ToString())
            : LdContext.Builder(AnonymousKey).Anonymous(true);

        ldUser.Kind(ContextKind.Default);
        client.Apply(ldUser);

        if (context.UserId is not null && context.UserOrganizationIds is { Count: > 0 } organizations)
        {
            ldUser.Set(
                OrganizationsAttribute,
                LdValue.ArrayFrom(organizations.Select(organization => LdValue.Of(organization.ToString()))));
        }

        builder.Add(ldUser.Build());

        return builder.Build();
    }

    /// <remarks>
    /// In order: the session token's <c>device</c> claim, then the
    /// <c>Device-Identifier</c> header. Then, below the product layer, the identifier a product
    /// service carried in <see cref="IContext.DeviceIdentifier"/>, which is the same value one
    /// hop on.
    /// </remarks>
    private string? DeviceIdentifier(HttpContext http)
    {
        var claimed = http.User.Identity?.IsAuthenticated == true
            ? http.User.FindFirst(DeviceClaim)?.Value
            : null;

        foreach (var candidate in (string?[])[claimed, http.Request.Headers[DeviceIdentifierHeader].FirstOrDefault()])
        {
            if (!string.IsNullOrWhiteSpace(candidate) && DeviceIdentifierPattern().IsMatch(candidate))
            {
                return candidate;
            }
        }

        return context.DeviceIdentifier;
    }

    /// <summary>
    /// The attributes every context in the request carries.
    /// </summary>
    private readonly record struct Client(Version? Version, bool IsPrerelease, byte? DeviceType)
    {
        public static Client Read(HttpContext http)
        {
            var headers = http.Request.Headers;

            var version = Version.TryParse(headers[ClientVersionHeader].FirstOrDefault(), out var parsed)
                ? parsed
                : null;

            // The header wins over the claim.
            var claimedType = http.User.Identity?.IsAuthenticated == true
                ? http.User.FindFirst(DeviceTypeClaim)?.Value
                : null;
            byte? deviceType = byte.TryParse(headers[DeviceTypeHeader].FirstOrDefault(), out var fromHeader)
                ? fromHeader
                : byte.TryParse(claimedType, out var fromClaim)
                    ? fromClaim
                    : null;

            return new Client(version, headers[IsPrereleaseHeader].FirstOrDefault() == "1", deviceType);
        }

        public void Apply(ContextBuilder builder)
        {
            if (Version is not null)
            {
                builder.Set(ClientVersionAttribute, Version.ToString());
                builder.Set(ClientVersionIsPrereleaseAttribute, IsPrerelease);
            }

            if (DeviceType is { } type)
            {
                builder.Set(DeviceTypeAttribute, type);
                builder.Set(ClientTypeAttribute, ClientTypes.For(type));
            }
        }
    }

    [GeneratedRegex("^[A-Za-z0-9_:-]{1,50}$")]
    private static partial Regex DeviceIdentifierPattern();
}
