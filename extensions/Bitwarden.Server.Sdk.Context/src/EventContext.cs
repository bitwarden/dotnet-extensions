namespace Bitwarden.Server.Sdk.Context;

/// <summary>
/// What an event says, as <see cref="ContextScope.BeginForEvent"/> establishes it: the event's context, reporting
/// <see cref="IContext.IsEvent"/>. Its handler acts with its own authority, so the event's user and provider are
/// recorded (<see cref="IContext.RecordedUserId"/>, <see cref="IContext.RecordedProviderId"/>) on a
/// <see cref="CallerCredential.System"/> credential, never acting.
/// </summary>
/// <param name="inner">What the event says.</param>
internal sealed class EventContext(IContext inner) : IContext
{
    /// <inheritdoc />
    public Guid? OrganizationId => inner.OrganizationId;

    /// <inheritdoc />
    public Guid? UserId => null;

    /// <inheritdoc />
    public Guid? ProviderId => null;

    /// <inheritdoc />
    public string? ClientIpAddress => inner.ClientIpAddress;

    /// <inheritdoc />
    public string? DeviceType => inner.DeviceType;

    /// <inheritdoc />
    public string? DeviceIdentifier => inner.DeviceIdentifier;

    /// <inheritdoc />
    public CallerCredential? Credential => CallerCredential.System;

    /// <inheritdoc />
    public Guid? RecordedUserId => inner.UserId ?? inner.RecordedUserId;

    /// <inheritdoc />
    public Guid? RecordedProviderId => inner.ProviderId ?? inner.RecordedProviderId;

    /// <inheritdoc />
    public IReadOnlyList<Guid>? UserOrganizationIds => inner.UserOrganizationIds;

    /// <inheritdoc />
    public FlagSubjectKind FlagSubjectKind => inner.FlagSubjectKind;

    /// <inheritdoc />
    public Guid? FlagSubjectKey => inner.FlagSubjectKey;

    /// <inheritdoc />
    public string? ClientVersion => inner.ClientVersion;

    /// <inheritdoc />
    public bool ClientVersionIsPrerelease => inner.ClientVersionIsPrerelease;

    /// <inheritdoc />
    public bool IsEvent => true;
}
