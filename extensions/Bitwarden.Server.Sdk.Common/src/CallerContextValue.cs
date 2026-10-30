namespace Bitwarden.Server.Sdk.Common;

/// <summary>
/// What <see cref="BitwardenHeaders.Context"/> carries: version 1 of a closed object.
/// </summary>
/// <remarks>
/// <para>
/// A member belongs here only if the callee can't derive it, it says who acted or where the request
/// came from, and more than one operation reads it.
/// </para>
/// <para>
/// The value is not signed. A callee trusts it exactly as far as it trusts the authenticated service
/// that sent it.
/// </para>
/// </remarks>
public sealed record CallerContextValue
{
    /// <summary>
    /// The address the originating request came from, as the receiving service saw it through the
    /// proxies it trusts. Read for audit records, security mail and IP allowlists.
    /// </summary>
    public string? ClientIp { get; init; }

    /// <summary>
    /// The kind of client the originating request was made from. Read for audit records and security
    /// mail.
    /// </summary>
    public string? DeviceType { get; init; }

    /// <summary>
    /// The client's own identifier for its device. Read to spare that device a push notification about
    /// what it did itself.
    /// </summary>
    public string? DeviceIdentifier { get; init; }

    /// <summary>
    /// The kind of credential the originating request presented. Audit-only.
    /// </summary>
    public CallerCredential? Credential { get; init; }

    /// <summary>
    /// On a <see cref="CallerCredential.System"/> call, the person the audit record names as having
    /// acted. Audit-only.
    /// </summary>
    public Guid? RecordedUser { get; init; }

    /// <summary>
    /// The provider whose staff member is acting in one of its client organizations. Audit-only.
    /// </summary>
    public Guid? RecordedProvider { get; init; }

    /// <summary>
    /// The organizations the acting person is a confirmed member of, for feature flag targeting only.
    /// Never an authority, a row filter or an audit fact. At most
    /// <see cref="CallerContextHeader.MaximumOrganizations"/>.
    /// </summary>
    public IReadOnlyList<Guid>? Organizations { get; init; }

    /// <summary>
    /// Whether there is nothing to carry.
    /// </summary>
    public bool IsEmpty =>
        ClientIp is null && DeviceType is null && DeviceIdentifier is null && Credential is null && RecordedUser is null
        && RecordedProvider is null && Organizations is not { Count: > 0 };
}
