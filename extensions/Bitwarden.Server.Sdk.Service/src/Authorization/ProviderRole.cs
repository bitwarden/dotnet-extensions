namespace Bitwarden.Server.Sdk.Service;

/// <summary>
/// What a caller may do in the provider their session is scoped to.
/// </summary>
/// <remarks>
/// Ordered by reach, like <see cref="OrganizationRole"/>. A provider has only two roles and no
/// custom one: a service user works on the provider's client organizations, and an
/// administrator additionally runs the provider itself: who works there, and which clients it
/// takes on.
/// </remarks>
public enum ProviderRole
{
    /// <summary>
    /// Not a member of the provider in scope, or no provider is in scope.
    /// </summary>
    None = 0,

    /// <summary>
    /// Works on the provider's client organizations.
    /// </summary>
    ServiceUser = 1,

    /// <summary>
    /// Additionally runs the provider itself.
    /// </summary>
    ProviderAdmin = 2,
}
