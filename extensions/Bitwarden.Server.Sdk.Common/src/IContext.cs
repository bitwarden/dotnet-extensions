namespace Bitwarden.Server.Sdk.Common;

/// <summary>
/// Who and what the current operation concerns.
/// </summary>
/// <remarks>
/// <para>
/// Any member may legitimately be absent. A request for data belonging to no organization has no
/// organization, and a background job acts for no user.
/// </para>
/// <para>
/// This is not authentication. A token says which service is calling and what it may do; this
/// says what the call is about. Every value is asserted by the caller rather than proven, so a
/// service trusts it only as far as it trusts the authenticated caller.
/// </para>
/// <para>
/// Members after <see cref="ProviderId"/> have default implementations, so an implementation that
/// predates them keeps compiling and answers "unknown".
/// </para>
/// </remarks>
public interface IContext
{
    /// <summary>
    /// The organization this operation concerns, if any.
    /// </summary>
    Guid? OrganizationId { get; }

    /// <summary>
    /// The user this operation is made on behalf of, if any.
    /// </summary>
    Guid? UserId { get; }

    /// <summary>
    /// The provider on whose authority this operation is made, if any.
    /// </summary>
    /// <remarks>
    /// A provider administers organizations it is not a member of. A provider administering a
    /// client organization sets all three of organization, user and provider.
    /// </remarks>
    Guid? ProviderId { get; }

    /// <summary>
    /// The address the originating request came from, if known.
    /// </summary>
    /// <remarks>
    /// Only the service that received the user's request can see it, so it is carried to every
    /// service that request reaches. Read for audit records and IP allowlists.
    /// </remarks>
    string? ClientIpAddress => null;

    /// <summary>
    /// The kind of client the originating request was made from, if known.
    /// </summary>
    string? DeviceType => null;

    /// <summary>
    /// The device the originating request was made from, by the identifier its client generated
    /// for itself, if known.
    /// </summary>
    /// <remarks>
    /// Used to skip pushing a change back to the device that made it. Never proof of anything, so
    /// nothing may be gated on it.
    /// </remarks>
    string? DeviceIdentifier => null;

    /// <summary>
    /// The kind of credential the request that started this work presented, if known.
    /// </summary>
    /// <remarks>
    /// Audit-only. No authorization, row filter or cache may read it.
    /// </remarks>
    CallerCredential? Credential => null;

    /// <summary>
    /// On a <see cref="CallerCredential.System"/> call, the person the audit record names as having
    /// acted, if any.
    /// </summary>
    /// <remarks>
    /// Audit-only. <see cref="UserId"/> stays null on such a call.
    /// </remarks>
    Guid? RecordedUserId => null;

    /// <summary>
    /// The provider whose staff member is acting in one of its client organizations, if that is who
    /// is acting.
    /// </summary>
    /// <remarks>
    /// Audit-only. <see cref="ProviderId"/> stays null on such a call, because the staff member is
    /// acting in the organization.
    /// </remarks>
    Guid? RecordedProviderId => null;

    /// <summary>
    /// Every organization the acting person is a confirmed member of, if known.
    /// </summary>
    /// <remarks>
    /// For feature flag targeting only. It grants nothing: no authorization, row filter, cache or
    /// audit record may read it.
    /// </remarks>
    IReadOnlyList<Guid>? UserOrganizationIds => null;
}
