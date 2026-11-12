namespace Bitwarden.Server.Sdk.Context;

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
/// says what the call is about. A host believes a value only as
/// <see cref="BitwardenHeaders.Delegation"/> signs it. A context a service asserted on its own
/// authority is still only that service's word, so the receiving service enforces its own access
/// rules.
/// </para>
/// <para>
/// Members after <see cref="ProviderId"/> have default implementations that answer "unknown", so an
/// implementation needs only the first three.
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
    /// <remarks>
    /// When <see cref="IsEvent"/> is true, the event's actor instead: the person whose request published the event,
    /// there for the record only. The work handling the event acts with its own authority, not on this person's behalf.
    /// </remarks>
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
    /// The organizations of the party feature flags are evaluated for (<see cref="FlagSubjectKind"/>), if
    /// known: every organization a person is a confirmed member of, or the one organization a credential
    /// that isn't a person's acts for, where the flag subject lists one.
    /// </summary>
    /// <remarks>
    /// For feature flag targeting only. It grants nothing: no authorization, row filter, cache or
    /// audit record may read it.
    /// </remarks>
    IReadOnlyList<Guid>? UserOrganizationIds => null;

    /// <summary>
    /// The kind of party feature flags are evaluated for: who made the originating request, as the
    /// token it presented says.
    /// </summary>
    /// <remarks>
    /// For feature flag targeting only. It grants nothing: no authorization, row filter, cache or
    /// audit record may read it.
    /// </remarks>
    FlagSubjectKind FlagSubjectKind => FlagSubjectKind.User;

    /// <summary>
    /// The party feature flags are evaluated for, by identifier: the person, organization, machine
    /// account or access connector of <see cref="FlagSubjectKind"/>. Null for nobody in particular.
    /// </summary>
    /// <remarks>
    /// For feature flag targeting only. It grants nothing: no authorization, row filter, cache or
    /// audit record may read it.
    /// </remarks>
    Guid? FlagSubjectKey => null;

    /// <summary>
    /// The version of the client the originating request was made from, if known, as
    /// <see cref="Version.ToString()"/> writes it.
    /// </summary>
    /// <remarks>
    /// For feature flag targeting only.
    /// </remarks>
    string? ClientVersion => null;

    /// <summary>
    /// Whether the client the originating request was made from said it is a prerelease. Only ever
    /// true alongside a <see cref="ClientVersion"/>.
    /// </summary>
    /// <remarks>
    /// For feature flag targeting only.
    /// </remarks>
    bool ClientVersionIsPrerelease => false;

    /// <summary>
    /// Whether this is the context of an event being handled, as <see cref="ContextScope.BeginForEvent"/> establishes
    /// it, so that <see cref="UserId"/> names the event's actor, for the record only.
    /// </summary>
    /// <remarks>
    /// Never carried between services: no header says a request is an event's, so a request's context is never one.
    /// </remarks>
    bool IsEvent => false;
}
