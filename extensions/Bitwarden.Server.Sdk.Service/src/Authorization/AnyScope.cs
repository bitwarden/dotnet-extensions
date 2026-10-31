namespace Bitwarden.Server.Sdk.Service;

/// <summary>
/// Lets an operation be reached with any one of several scopes, where
/// <see cref="AuthorizationAttribute.Scope"/> names exactly one.
/// </summary>
/// <remarks>
/// <para>
/// For one path with two kinds of caller, which the API standards prefer to two paths. A list read
/// two ways, for example: by a person's product service under a read scope, for the person in
/// <c>Bitwarden-User</c>; and by a seeding tool under an invitations scope with nobody in context,
/// to find an invitation by the address invited.
/// </para>
/// <para>
/// This only admits the caller. Which scope allows what is the operation's to decide, and it must:
/// the attribute says nothing about the acting user or the organization, so the operation checks
/// <see cref="CallerScopes.Holds"/> itself and narrows what each scope may ask. An operation marked
/// with it must declare its organization and user optional, and no role or permission, because the
/// requirement it replaces would have checked them; the service refuses to start otherwise, rather
/// than drop a check silently. Its provider is optional or, as every operation's is unless declared,
/// forbidden, which this still refuses.
/// </para>
/// </remarks>
/// <param name="scopes">The scopes, any one of which admits the caller.</param>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class AnyScopeAttribute(params string[] scopes) : Attribute
{
    /// <summary>
    /// The scopes, any one of which admits the caller.
    /// </summary>
    public IReadOnlyList<string> Scopes { get; } = scopes;
}
