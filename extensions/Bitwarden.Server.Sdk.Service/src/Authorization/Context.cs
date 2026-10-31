using System.Security.Claims;
using Bitwarden.Server.Sdk.Common;
using Microsoft.AspNetCore.Http;

namespace Bitwarden.Server.Sdk.Service;

/// <summary>
/// Reads <see cref="IContext"/> for the current request, or from the scope a message consumer
/// established when there is no request.
/// </summary>
/// <remarks>
/// <para>
/// The request wins whenever there is one. A <see cref="ContextScope"/> exists for work that has
/// no headers to read; honouring one inside a request would let code substitute an organization
/// the caller never named, so it is consulted only where the request is absent.
/// </para>
/// <para>
/// <b>A product service</b> holds people's sessions, and reads its context from the session it
/// validated and nowhere else: the person is the token's <c>sub</c>, the organization
/// its <c>organization</c> claim, the provider a provider session's <c>provider</c> claim, the
/// credential the kind of token it is; the client's address is the connection's, through the
/// proxies it trusts; the device is the session's signed <c>device</c> claim, then the client's
/// <c>Device-Identifier</c> header. It refuses the four caller-context headers before
/// authentication (<see cref="CallerContextRefusal"/>), so there is nothing here to ignore.
/// </para>
/// <para>
/// <b>A domain service</b> is called by other services, and reads its context from the call's
/// context token (<see cref="BitwardenHeaders.Delegation"/>), which Identity signed for this service
/// and that caller and which carries what the three owner headers and
/// <see cref="BitwardenHeaders.Context"/> say. It believes it only from a caller presenting an
/// internal service token (<c>client_id</c> <c>internal.*</c>, <see cref="InternalCaller"/>). A person's token, a Send's token, or no token at
/// all, and the context is empty, whatever the request carries: Identity's sign-in surface is
/// called by browsers, and what a browser asserts about whose request it is is worth nothing.
/// </para>
/// </remarks>
/// <param name="accessor">Supplies the request being handled.</param>
/// <param name="isProductService">Whether the context comes from the session rather than from headers.</param>
internal sealed class Context(IHttpContextAccessor accessor, bool isProductService = false) : IContext
{
    /// <summary>
    /// The claim naming the person a session token was issued to.
    /// </summary>
    internal const string SubjectClaim = "sub";

    /// <summary>
    /// The claim naming the device a session was signed in on, as its client identifies it.
    /// </summary>
    internal const string DeviceClaim = "device";

    /// <summary>
    /// The claim Identity lists a person's confirmed organizations in on a product token, one value each.
    /// </summary>
    internal const string OrganizationsClaim = "organizations";

    /// <summary>
    /// The claim naming the machine account a Secrets Manager access token belongs to.
    /// </summary>
    internal const string ServiceAccountClaim = "service_account";

    /// <summary>
    /// The header a Bitwarden client names its device type in, as a number.
    /// </summary>
    private const string ClientDeviceTypeHeader = "Device-Type";

    /// <summary>
    /// The header a Bitwarden client names its device in.
    /// </summary>
    private const string ClientDeviceIdentifierHeader = "Device-Identifier";

    /// <summary>
    /// The claim a session token may name its device type in.
    /// </summary>
    private const string DeviceTypeClaim = "devicetype";

    /// <inheritdoc />
    /// <remarks>
    /// In a product service, the organization the session was issued for, or the one
    /// an organization credential or machine account's token is for. Never the request's say.
    /// </remarks>
    public Guid? OrganizationId => Http is { } http
        ? isProductService
            ? Session(http) is { } session ? IssuedFor(session) : null
            : Signed(http)?.Organization
        : ContextScope.Current?.OrganizationId;

    /// <inheritdoc />
    public Guid? UserId => Http is { } http
        ? isProductService
            ? Subject(http)
            : Signed(http)?.User
        : ContextScope.Current?.UserId;

    /// <inheritdoc />
    /// <remarks>
    /// In a product service, the provider a provider's session was issued for: its <c>provider</c>
    /// claim, on a session naming no organization. A provider's staff member's
    /// session for a client organization carries the provider too, as the authority for their
    /// provider role there, but is about the organization: its calls carry the
    /// organization and no provider.
    /// </remarks>
    public Guid? ProviderId => Http is { } http
        ? isProductService
            ? Session(http) is { } session && IssuedFor(session) is null
                && Guid.TryParse(session.FindFirst(CallerRole.ProviderClaim)?.Value, out var provider)
                    ? provider
                    : null
            : Signed(http)?.Provider
        : ContextScope.Current?.ProviderId;

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// In a product service it is the connection's address: the client's own, or, behind a proxy
    /// the service trusts, the address that proxy
    /// was connected from (<see cref="ForwardedClientAddress"/>). Never a header the client sent: a
    /// client does not choose the address an audit record gives it.
    /// </para>
    /// <para>
    /// A domain service reads it from <see cref="BitwardenHeaders.Context"/>, which the calling
    /// service wrote, in canonical form. It never reads its own connection, which only ever shows
    /// the peer that called it.
    /// </para>
    /// </remarks>
    public string? ClientIpAddress => Http is { } http
        ? isProductService
            ? http.Connection.RemoteIpAddress is { } connected
                ? TrustedProxies.Canonical(connected).ToString()
                : null
            : Carried(http)?.ClientIp
        : ContextScope.Current?.ClientIpAddress;

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// Held to a short name of letters, digits, hyphens and underscores. The platform does not know
    /// the vocabulary, but it does know that a device type is not a paragraph.
    /// </para>
    /// <para>
    /// In a product service it is the client's own: the <c>Device-Type</c> header, a device type's
    /// number, or failing that the session token's <c>devicetype</c> claim. A domain
    /// service reads what the calling service carried in <see cref="BitwardenHeaders.Context"/>.
    /// </para>
    /// </remarks>
    public string? DeviceType => Http is { } http
        ? isProductService
            ? ClientDeviceType(http)
            : Carried(http)?.DeviceType
        : ContextScope.Current?.DeviceType;

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// In a product service, the session's signed <c>device</c> claim, then the client's
    /// <c>Device-Identifier</c> header where the session carries none. A domain service reads what
    /// the calling service carried in <see cref="BitwardenHeaders.Context"/>.
    /// </para>
    /// <para>
    /// Held to what a push tag allows (letters, digits, <c>-</c>, <c>_</c> and <c>:</c>) and to
    /// 50 characters, the length of a device's stored identifier. A value with anything else is
    /// dropped rather than stripped, because an identifier with characters removed names some other
    /// device, and the only cost of dropping one is that the device that acted is told too.
    /// </para>
    /// </remarks>
    public string? DeviceIdentifier => Http is { } http
        ? isProductService
            ? ClientDeviceIdentifier(http)
            : Carried(http)?.DeviceIdentifier
        : ContextScope.Current?.DeviceIdentifier;

    /// <inheritdoc />
    /// <remarks>
    /// In a product service, the kind of token it validated: a person's session (<c>sub</c>) is
    /// <see cref="CallerCredential.User"/>; a machine account's token (<c>service_account</c>) is
    /// <see cref="CallerCredential.Machine"/>; an organization's API key (<c>organization</c> and no
    /// <c>sub</c>) is <see cref="CallerCredential.Organization"/>; anything else, a Send's token
    /// included, none. A domain service reads what the calling service carried.
    /// </remarks>
    public CallerCredential? Credential => Http is { } http
        ? isProductService
            ? Session(http) is { } session ? CredentialOf(session) : null
            : Carried(http)?.Credential
        : ContextScope.Current?.Credential;

    /// <inheritdoc />
    /// <remarks>
    /// Never in a product service: a person's request is recorded as the person. A domain service
    /// reads what the calling service carried, on a <see cref="CallerCredential.System"/> call only.
    /// </remarks>
    public Guid? RecordedUserId => Http is { } http
        ? isProductService ? null : Carried(http)?.RecordedUser
        : ContextScope.Current?.RecordedUserId;

    /// <inheritdoc />
    /// <remarks>
    /// In a product service, the provider on a session issued for an organization: a provider's staff
    /// member's session for one of its clients carries the provider as the authority for their provider role
    /// there, and is otherwise about the organization (<see cref="ProviderId"/> is null). A domain
    /// service reads what the calling service carried.
    /// </remarks>
    public Guid? RecordedProviderId => Http is { } http
        ? isProductService
            ? Session(http) is { } session && IssuedFor(session) is not null
                && Guid.TryParse(session.FindFirst(CallerRole.ProviderClaim)?.Value, out var provider)
                    ? provider
                    : null
            : Carried(http)?.RecordedProvider
        : ContextScope.Current?.RecordedProviderId;

    /// <inheritdoc />
    /// <remarks>
    /// In a product service, the person's session's <c>organizations</c> claim, one identifier per value,
    /// on a session issued to a person only; anything in the claim that isn't an identifier is dropped. A
    /// domain service reads what the calling service carried in <see cref="BitwardenHeaders.Context"/>.
    /// </remarks>
    public IReadOnlyList<Guid>? UserOrganizationIds => Http is { } http
        ? isProductService
            ? Subject(http) is not null ? MemberOrganizations(http.User) : null
            : Carried(http)?.Organizations
        : ContextScope.Current?.UserOrganizationIds;

    private HttpContext? Http => accessor.HttpContext;

    /// <summary>
    /// The organizations a person's session lists, by identifier, each once, or null for none.
    /// </summary>
    private static Guid[]? MemberOrganizations(ClaimsPrincipal session)
    {
        Guid[] organizations =
        [
            .. session.FindAll(OrganizationsClaim)
                .Select(claim => Guid.TryParse(claim.Value, out var organization) ? organization : Guid.Empty)
                .Where(organization => organization != Guid.Empty)
                .Distinct(),
        ];

        return organizations.Length > 0 ? organizations : null;
    }

    /// <summary>
    /// The device type the client says it is: the header, then the claim.
    /// </summary>
    private static string? ClientDeviceType(HttpContext http)
    {
        if (byte.TryParse(http.Request.Headers[ClientDeviceTypeHeader].FirstOrDefault(), out var fromHeader))
        {
            return fromHeader.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return Session(http) is { } session
            && byte.TryParse(session.FindFirst(DeviceTypeClaim)?.Value, out var fromClaim)
                ? fromClaim.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : null;
    }

    /// <summary>
    /// The device the client is on: the session's signed claim, then the client's header.
    /// </summary>
    private static string? ClientDeviceIdentifier(HttpContext http)
    {
        var value = Session(http)?.FindFirst(DeviceClaim)?.Value is { Length: > 0 } claimed
            ? claimed
            : http.Request.Headers[ClientDeviceIdentifierHeader].FirstOrDefault();

        return CallerContextHeader.IsDeviceIdentifier(value) ? value : null;
    }

    private static CallerCredential? CredentialOf(ClaimsPrincipal session)
    {
        if (session.FindFirst(SubjectClaim) is not null)
        {
            return CallerCredential.User;
        }

        if (session.FindFirst(ServiceAccountClaim) is not null)
        {
            return CallerCredential.Machine;
        }

        return IssuedFor(session) is not null ? CallerCredential.Organization : null;
    }

    /// <summary>
    /// The organization a validated token was issued for: its <c>organization</c> claim.
    /// </summary>
    private static Guid? IssuedFor(ClaimsPrincipal session) =>
        Guid.TryParse(session.FindFirst(CallerRole.OrganizationClaim)?.Value, out var organization)
            ? organization
            : null;

    /// <summary>
    /// The validated session, or null before authentication has run or without one.
    /// </summary>
    private static ClaimsPrincipal? Session(HttpContext http) =>
        http.User.Identity?.IsAuthenticated == true ? http.User : null;

    /// <summary>
    /// The request, when a domain service believes what its headers assert: the caller presented
    /// an internal service token.
    /// </summary>
    private static HttpRequest? Asserted(HttpContext http) =>
        InternalCaller.Is(http.User) ? http.Request : null;

    /// <summary>
    /// The call's context, when it is believed: as the context token Identity signed for it says,
    /// verified once by <see cref="CallerContextHeaders"/>, which refuses the call before anything
    /// reads it when the token is missing, forged or disagrees with the plain headers.
    /// </summary>
    private static VerifiedContext? Signed(HttpContext http) =>
        Asserted(http) is not null ? CallerContextHeaders.Verified(http) : null;

    /// <summary>
    /// What <see cref="BitwardenHeaders.Context"/> carries, as the context token signed it.
    /// </summary>
    private static CallerContextValue? Carried(HttpContext http) => Signed(http)?.Context;

    /// <summary>
    /// The person the request's validated session was issued to, or null before authentication
    /// has run or when the request presented no session.
    /// </summary>
    internal static Guid? Subject(HttpContext http) =>
        http.User.Identity?.IsAuthenticated == true
        && Guid.TryParse(http.User.FindFirst(SubjectClaim)?.Value, out var value)
            ? value
            : null;
}
