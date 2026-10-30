namespace Bitwarden.Server.Sdk.Common;

/// <summary>
/// A context stated outright rather than read from a request, for use with
/// <see cref="ContextScope.Begin"/>.
/// </summary>
public sealed record StatedContext : IContext
{
    /// <inheritdoc />
    public Guid? OrganizationId { get; init; }

    /// <inheritdoc />
    public Guid? UserId { get; init; }

    /// <inheritdoc />
    public Guid? ProviderId { get; init; }

    /// <inheritdoc />
    public string? ClientIpAddress { get; init; }

    /// <inheritdoc />
    public string? DeviceType { get; init; }

    /// <inheritdoc />
    public string? DeviceIdentifier { get; init; }

    /// <inheritdoc />
    public CallerCredential? Credential { get; init; }

    /// <inheritdoc />
    public Guid? RecordedUserId { get; init; }

    /// <inheritdoc />
    public Guid? RecordedProviderId { get; init; }

    /// <inheritdoc />
    public IReadOnlyList<Guid>? UserOrganizationIds { get; init; }
}
