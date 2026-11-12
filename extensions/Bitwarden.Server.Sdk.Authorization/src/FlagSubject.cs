using System.Security.Claims;
using Bitwarden.Server.Sdk.Context;

namespace Bitwarden.Server.Sdk.Authorization;

/// <summary>
/// The party a request's feature flags are evaluated for, as the token it presented says.
/// </summary>
/// <remarks>
/// <para>
/// For feature flag targeting only. It grants nothing: no authorization, row filter, cache or audit
/// record may read it.
/// </para>
/// <para>
/// The token's <c>type</c> claim names the kind of client it was issued to: <c>Organization</c>,
/// <c>ServiceAccount</c>, <c>AccessConnector</c> or <c>Send</c>. A token without one, or with a value
/// that is none of those, is a <c>User</c>'s: a person's, or nobody's in particular.
/// </para>
/// <list type="bullet">
///   <item><description>
///     <c>User</c>: keyed by the person (<c>sub</c>), or anonymous. Its organizations are those the token
///     lists (<c>organizations</c>), or, for a client that is an organization (<c>client_id</c>
///     <c>organization.{id}</c>) and lists none, that organization (<c>organization</c>).
///   </description></item>
///   <item><description>
///     <c>Organization</c>: keyed by the organization (<c>organization</c>).
///   </description></item>
///   <item><description>
///     <c>ServiceAccount</c>: keyed by the machine account (<c>service_account</c>), else by its
///     organization.
///   </description></item>
///   <item><description>
///     <c>AccessConnector</c>: keyed by the connector (<c>access_connector</c>), its organization listed.
///   </description></item>
///   <item><description>
///     <c>Send</c>: anonymous, with no organizations.
///   </description></item>
/// </list>
/// <para>
/// A kind other than <c>User</c> whose token names no key has no key: nothing is evaluated for that
/// party but its device.
/// </para>
/// </remarks>
/// <param name="Kind">The kind of party.</param>
/// <param name="Key">The party, by identifier; null for nobody in particular.</param>
/// <param name="Organizations">The party's organizations, each once; null for none.</param>
public sealed record FlagSubject(FlagSubjectKind Kind, Guid? Key, IReadOnlyList<Guid>? Organizations)
{
    /// <summary>
    /// The claim naming the kind of client a token was issued to.
    /// </summary>
    public const string TypeClaim = "type";

    /// <summary>
    /// <see cref="TypeClaim"/>'s value on an organization's own token.
    /// </summary>
    public const string OrganizationType = "Organization";

    /// <summary>
    /// <see cref="TypeClaim"/>'s value on a Secrets Manager machine account's token.
    /// </summary>
    public const string ServiceAccountType = "ServiceAccount";

    /// <summary>
    /// <see cref="TypeClaim"/>'s value on a PAM access connector's token.
    /// </summary>
    public const string AccessConnectorType = "AccessConnector";

    /// <summary>
    /// <see cref="TypeClaim"/>'s value on a Send's token.
    /// </summary>
    public const string SendType = "Send";

    /// <summary>
    /// The claim naming the access connector a token acts as.
    /// </summary>
    internal const string AccessConnectorClaim = "access_connector";

    /// <summary>
    /// What an organization's client identifier starts with.
    /// </summary>
    internal const string OrganizationClientPrefix = "organization.";

    /// <summary>
    /// Nobody in particular.
    /// </summary>
    public static FlagSubject Anonymous { get; } = new(FlagSubjectKind.User, null, null);

    /// <summary>
    /// The party a validated token's feature flags are evaluated for.
    /// </summary>
    /// <param name="token">The token's claims.</param>
    public static FlagSubject Of(ClaimsPrincipal token)
    {
        ArgumentNullException.ThrowIfNull(token);

        var organization = Identifier(token, CallerRole.OrganizationClaim);

        return token.FindFirst(TypeClaim)?.Value switch
        {
            SendType => Anonymous,

            OrganizationType => new FlagSubject(FlagSubjectKind.Organization, organization, null),

            ServiceAccountType => new FlagSubject(
                FlagSubjectKind.ServiceAccount,
                Identifier(token, CurrentContext.ServiceAccountClaim) ?? organization,
                null),

            AccessConnectorType => new FlagSubject(
                FlagSubjectKind.AccessConnector,
                Identifier(token, AccessConnectorClaim),
                organization is { } connectorOrganization ? [connectorOrganization] : null),

            _ => new FlagSubject(
                FlagSubjectKind.User,
                Identifier(token, CurrentContext.SubjectClaim),
                Listed(token) ?? (organization is { } named
                    && token.FindFirst("client_id")?.Value?.StartsWith(OrganizationClientPrefix, StringComparison.Ordinal) == true
                        ? [named]
                        : null)),
        };
    }

    /// <summary>
    /// Whether a flag subject is this one: the same kind and key, and the same organizations in any order.
    /// </summary>
    /// <param name="other">The other.</param>
    public bool Equals(FlagSubject? other) =>
        other is not null
        && Kind == other.Kind
        && Key == other.Key
        && (Organizations ?? []).ToHashSet().SetEquals(other.Organizations ?? []);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Kind, Key);

    /// <summary>
    /// The organizations a person's token lists, by identifier, each once, or null for none. Anything
    /// in the claim that isn't an identifier is dropped.
    /// </summary>
    private static Guid[]? Listed(ClaimsPrincipal token)
    {
        Guid[] organizations =
        [
            .. token.FindAll(CurrentContext.OrganizationsClaim)
                .Select(claim => Guid.TryParse(claim.Value, out var organization) ? organization : Guid.Empty)
                .Where(organization => organization != Guid.Empty)
                .Distinct(),
        ];

        return organizations.Length > 0 ? organizations : null;
    }

    private static Guid? Identifier(ClaimsPrincipal token, string claim) =>
        Guid.TryParse(token.FindFirst(claim)?.Value, out var value) && value != Guid.Empty ? value : null;
}
