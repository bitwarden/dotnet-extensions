namespace Bitwarden.Server.Sdk.Authorization;

/// <summary>
/// A person's standing as it is now: their confirmed membership of an organization, the provider administering an
/// organization, and their confirmed place on a provider's staff. What a product service authorizes a person from.
/// </summary>
/// <remarks>
/// <para>
/// <b>For product services, and required there.</b> A person's session names the organization or provider it was
/// issued for, and Identity issued it only to a confirmed member or confirmed staff. That was true when the session was
/// exchanged; a person removed or demoted since still holds the session for the rest of its life. So a product service
/// asks this, once per request, before authorization runs, and authorizes from the answer
/// (<see cref="ICallerRole"/>), never from the role the session was issued with. A product service without one
/// registered fails at startup.
/// </para>
/// <para>
/// The service that owns the standing implements it in its client package, from its cache first.
/// A product service can only ask about its own request: the person its session names, and the organization or
/// provider the session names or administers. An implementation may refuse anything else.
/// </para>
/// <para>
/// Each answer is about one party and carries no Vault Data and no key: a role, permissions, an identifier.
/// </para>
/// </remarks>
public interface ICurrentStanding
{
    /// <summary>
    /// The person's confirmed membership of the organization, or null when they have none: not a member, invited or
    /// accepted but not confirmed, revoked, or in a role the platform doesn't know.
    /// </summary>
    /// <param name="person">The person.</param>
    /// <param name="organization">The organization.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>Their role and permissions, or null.</returns>
    Task<MemberStanding?> MembershipAsync(Guid person, Guid organization, CancellationToken cancellationToken = default);

    /// <summary>
    /// The provider administering the organization, or null when none does.
    /// </summary>
    /// <param name="organization">The organization.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The provider, or null.</returns>
    Task<Guid?> ProviderOfAsync(Guid organization, CancellationToken cancellationToken = default);

    /// <summary>
    /// The person's role on the provider's staff, when they are confirmed staff of it; null when they aren't.
    /// </summary>
    /// <param name="person">The person.</param>
    /// <param name="provider">The provider.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>Their provider role, or null.</returns>
    Task<ProviderRole?> ProviderRoleAsync(Guid person, Guid provider, CancellationToken cancellationToken = default);
}
