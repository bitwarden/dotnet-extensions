using Bitwarden.Server.Sdk.Authorization;
using Bitwarden.Server.Sdk.Context;
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
/// flags, so one LaunchDarkly project targets them all and a party sees the same flags whichever
/// server they reached:
/// </para>
/// <list type="bullet">
///   <item><description>
///     Outside a request, a single anonymous context under the shared anonymous key.
///   </description></item>
///   <item><description>
///     Inside one, a multi-context. A <c>device</c> context keyed by the device identifier,
///     when there is one. Then one context for the party the request was made by
///     (<see cref="IContext.FlagSubjectKind"/> and <see cref="IContext.FlagSubjectKey"/>):
///   </description></item>
///   <item><description>
///     A <see cref="FlagSubjectKind.User"/>: a user context keyed by the person, or by the shared
///     anonymous key, marked anonymous, for nobody in particular, with <c>organizations</c>
///     (<see cref="IContext.UserOrganizationIds"/>): every organization the person is a confirmed
///     member of, or the one an organization's own client acts for. Absent when there are none.
///   </description></item>
///   <item><description>
///     A <see cref="FlagSubjectKind.Organization"/>: an <c>organization</c> context keyed by the
///     organization. A <see cref="FlagSubjectKind.ServiceAccount"/>: a <c>service-account</c> context
///     keyed by the machine account, or by its organization. A
///     <see cref="FlagSubjectKind.AccessConnector"/>: an <c>access-connector</c> context keyed by the
///     connector, with its organization in <c>organizations</c>. Each only with a key.
///   </description></item>
///   <item><description>
///     On each of them, <c>client-version</c> and <c>client-version-is-prerelease</c> from the
///     client's version, and <c>device-type</c> and <c>client-type</c> from the device type.
///   </description></item>
/// </list>
/// <para>
/// <b>Where it comes from.</b> Only <see cref="IContext"/>: a product service reads it from the
/// client's token and headers, and a service below it from what the calling service carried, so a
/// flag evaluates for the same party whichever service checks it. A request a client made to a
/// service that isn't a product service directly, such as a sign-in, carries no context: its device,
/// device type and version come from the client's own headers (<see cref="ClientHeaders"/>), and the
/// party is nobody in particular.
/// </para>
/// <para>
/// <b>What leaves the platform.</b> The context is sent to LaunchDarkly when an SDK key is
/// configured: the identifiers of the party, the device and the organizations, and the client's
/// version and type. No Vault Data, and nothing a client sent as free text. The device identifier is
/// held to the same short pattern <see cref="IContext.DeviceIdentifier"/> applies, and dropped
/// otherwise, so a header cannot carry arbitrary text to a third party.
/// </para>
/// </remarks>
/// <param name="accessor">Supplies the request being handled.</param>
/// <param name="context">Who made the request, and from where.</param>
internal sealed class ServiceContextBuilder(IHttpContextAccessor accessor, IContext context) : IContextBuilder
{
    /// <summary>
    /// The shared key for every evaluation made for nobody in particular.
    /// </summary>
    internal const string AnonymousKey = "25a15cac-58cf-4ac0-ad0f-b17c4bd92294";

    internal const string DeviceKind = "device";
    internal const string OrganizationKind = "organization";
    internal const string ServiceAccountKind = "service-account";
    internal const string AccessConnectorKind = "access-connector";

    internal const string ClientVersionAttribute = "client-version";
    internal const string ClientVersionIsPrereleaseAttribute = "client-version-is-prerelease";
    internal const string DeviceTypeAttribute = "device-type";
    internal const string ClientTypeAttribute = "client-type";
    internal const string OrganizationsAttribute = "organizations";

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

        var client = Client.Read(http, context);
        var builder = LdContext.MultiBuilder();

        if (client.DeviceIdentifier is { } device)
        {
            var ldDevice = LdContext.Builder(device).Kind(DeviceKind);
            client.Apply(ldDevice);
            builder.Add(ldDevice.Build());
        }

        if (Subject(client) is { } subject)
        {
            builder.Add(subject);
        }

        return builder.Build();
    }

    /// <summary>
    /// The context of the party the request was made by, or none for a kind other than a user's
    /// without a key.
    /// </summary>
    private LdContext? Subject(Client client)
    {
        var key = context.FlagSubjectKey;

        switch (context.FlagSubjectKind)
        {
            case FlagSubjectKind.Organization when key is { } organization:
                return Keyed(organization, OrganizationKind, organizations: false);

            case FlagSubjectKind.ServiceAccount when key is { } serviceAccount:
                return Keyed(serviceAccount, ServiceAccountKind, organizations: false);

            case FlagSubjectKind.AccessConnector when key is { } connector:
                return Keyed(connector, AccessConnectorKind, organizations: true);

            case FlagSubjectKind.User:
                // One user context for a person and for nobody in particular alike.
                var ldUser = key is { } user
                    ? LdContext.Builder(user.ToString())
                    : LdContext.Builder(AnonymousKey).Anonymous(true);

                ldUser.Kind(ContextKind.Default);
                client.Apply(ldUser);
                SetOrganizations(ldUser);
                return ldUser.Build();

            default:
                return null;
        }

        LdContext Keyed(Guid identifier, string kind, bool organizations)
        {
            var ldSubject = LdContext.Builder(identifier.ToString()).Kind(kind);
            client.Apply(ldSubject);
            if (organizations)
            {
                SetOrganizations(ldSubject);
            }

            return ldSubject.Build();
        }
    }

    private void SetOrganizations(ContextBuilder builder)
    {
        if (context.UserOrganizationIds is { Count: > 0 } organizations)
        {
            builder.Set(
                OrganizationsAttribute,
                LdValue.ArrayFrom(organizations.Select(organization => LdValue.Of(organization.ToString()))));
        }
    }

    /// <summary>
    /// Where the request came from: its device, and the attributes every context in the request carries.
    /// </summary>
    private readonly record struct Client(string? DeviceIdentifier, string? Version, bool IsPrerelease, byte? DeviceType)
    {
        /// <summary>
        /// As the context says, and, where it says nothing, as the client's own headers do: a request a
        /// client made to this service directly, with no context to carry them.
        /// </summary>
        public static Client Read(HttpContext http, IContext context)
        {
            var session = http.User.Identity?.IsAuthenticated == true ? http.User : null;

            var device = ContextHeader.IsDeviceIdentifier(context.DeviceIdentifier)
                ? context.DeviceIdentifier
                : ClientHeaders.DeviceIdentifier(http, session);

            var version = context.ClientVersion is { } carried && ContextHeader.IsClientVersion(carried)
                ? new ClientVersion(carried, context.ClientVersionIsPrerelease)
                : ClientHeaders.ClientVersion(http) is { } sent
                    ? new ClientVersion(sent, ClientHeaders.IsPrerelease(http))
                    : null;

            var deviceType = ClientHeaders.DeviceTypeNumber(context.DeviceType)
                ?? ClientHeaders.DeviceTypeNumber(ClientHeaders.DeviceType(http, session));

            return new Client(device, version?.Value, version?.IsPrerelease == true, deviceType);
        }

        public void Apply(ContextBuilder builder)
        {
            if (Version is not null)
            {
                builder.Set(ClientVersionAttribute, Version);
                builder.Set(ClientVersionIsPrereleaseAttribute, IsPrerelease);
            }

            if (DeviceType is { } type)
            {
                builder.Set(DeviceTypeAttribute, type);
                builder.Set(ClientTypeAttribute, ClientTypes.For(type));
            }
        }
    }

    private sealed record ClientVersion(string Value, bool IsPrerelease);
}
