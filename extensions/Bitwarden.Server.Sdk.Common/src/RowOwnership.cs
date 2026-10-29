namespace Bitwarden.Server.Sdk.Common;

/// <summary>
/// Supplies the owner every restricted query is narrowed to.
/// </summary>
/// <remarks>
/// <para>
/// Separate from <see cref="IContext"/> because the answers differ. A context may legitimately
/// have no organization; a query against organization-owned rows may not proceed without one.
/// This is where "absent" becomes "refused".
/// </para>
/// <para>
/// Here rather than beside the Entity Framework machinery that consumes it, because it needs
/// nothing from Entity Framework. Keeping it here is what lets the platform register a default
/// for every service without obliging a service that stores nothing to reference a data
/// package.
/// </para>
/// </remarks>
public interface IRowOwnership
{
    /// <summary>The organization in scope.</summary>
    /// <exception cref="ForbiddenException">There is none.</exception>
    Guid RequireOrganization();

    /// <summary>The user in scope.</summary>
    /// <exception cref="ForbiddenException">There is none.</exception>
    Guid RequireUser();

    /// <summary>The provider in scope.</summary>
    /// <exception cref="ForbiddenException">There is none.</exception>
    Guid RequireProvider();

    /// <summary>The organization in scope, or null when the request names none.</summary>
    /// <remarks>
    /// For rows whose ownership is one of several alternatives — a vault item belongs to a
    /// person or to an organization — where an absent axis is an ordinary state rather than a
    /// refusal. Everywhere else, ask for the owner and let it refuse.
    /// </remarks>
    Guid? Organization();

    /// <summary>The user in scope, or null when the request names none.</summary>
    /// <remarks>The counterpart of <see cref="Organization"/>.</remarks>
    Guid? User();

    /// <summary>The provider in scope, or null when the request names none.</summary>
    /// <remarks>The counterpart of <see cref="Organization"/>.</remarks>
    Guid? Provider();
}

/// <summary>
/// Reads the owner from the current caller's context, and refuses when there is none.
/// </summary>
/// <remarks>
/// <para>
/// Registered by the platform for every service, so a service that scopes its rows the ordinary
/// way registers nothing. A service whose notion of an owner is genuinely its own replaces it
/// from <c>ConfigureServices</c>, where an explicit registration wins over the platform's.
/// </para>
/// <para>
/// Refusing rather than widening is the whole point. A filter that fell back to "no restriction"
/// when no organization was in scope would turn a missing header into a read of every
/// organization's rows — the failure mode row-level security exists to prevent, reached by the
/// code meant to prevent it.
/// </para>
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
