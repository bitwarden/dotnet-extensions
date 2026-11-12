namespace Bitwarden.Server.Sdk.Context;

/// <summary>
/// What an event says, as <see cref="ContextScope.BeginForEvent"/> establishes it: the event's context, reporting
/// <see cref="IContext.IsEvent"/>.
/// </summary>
/// <param name="inner">What the event says.</param>
internal sealed class EventContext(IContext inner) : IContext
{
    /// <inheritdoc />
    public Guid? OrganizationId => inner.OrganizationId;

    /// <inheritdoc />
    public Guid? UserId => inner.UserId;

    /// <inheritdoc />
    public Guid? ProviderId => inner.ProviderId;

    /// <inheritdoc />
    public string? ClientIpAddress => inner.ClientIpAddress;

    /// <inheritdoc />
    public string? DeviceType => inner.DeviceType;

    /// <inheritdoc />
    public string? DeviceIdentifier => inner.DeviceIdentifier;

    /// <inheritdoc />
    public CallerCredential? Credential => inner.Credential;

    /// <inheritdoc />
    public Guid? RecordedUserId => inner.RecordedUserId;

    /// <inheritdoc />
    public Guid? RecordedProviderId => inner.RecordedProviderId;

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
