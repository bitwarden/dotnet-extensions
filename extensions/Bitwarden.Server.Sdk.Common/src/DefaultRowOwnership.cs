namespace Bitwarden.Server.Sdk.Common;

/// <summary>
/// Reads the owner from the current caller's context, and refuses when there is none.
/// </summary>
/// <remarks>
/// Refusing rather than widening is the point. A filter that fell back to "no restriction" when no
/// organization was in scope would turn a missing header into a read of every organization's rows.
/// </remarks>
/// <param name="context">What the current request is about.</param>
public sealed class DefaultRowOwnership(IContext context) : IRowOwnership
{
    /// <inheritdoc />
    public Guid RequireOrganization() =>
        context.OrganizationId
        ?? throw new ForbiddenException(
            "This data belongs to an organization, and no organization is in scope for this request.");

    /// <inheritdoc />
    public Guid RequireUser() =>
        context.UserId
        ?? throw new ForbiddenException(
            "This data belongs to a user, and no user is in scope for this request.");

    /// <inheritdoc />
    public Guid RequireProvider() =>
        context.ProviderId
        ?? throw new ForbiddenException(
            "This data belongs to a provider, and no provider is in scope for this request.");

    /// <inheritdoc />
    public Guid? Organization() => context.OrganizationId;

    /// <inheritdoc />
    public Guid? User() => context.UserId;

    /// <inheritdoc />
    public Guid? Provider() => context.ProviderId;
}
