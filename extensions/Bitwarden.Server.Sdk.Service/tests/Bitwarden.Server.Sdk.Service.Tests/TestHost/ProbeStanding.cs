using System.Collections.Concurrent;
using Bitwarden.Server.Sdk.Authorization;
using Microsoft.AspNetCore.Http;

namespace Bitwarden.Server.Sdk.Service.Tests.TestHost;

/// <summary>
/// The current standing a probe host's product service authorizes from, as a test sets it.
/// </summary>
/// <remarks>
/// Unless a test says otherwise, a person holds what their session names, in no role: a member of its organization in
/// <see cref="OrganizationRole.None"/>, staff of its provider in <see cref="ProviderRole.None"/>, and the session's
/// provider administers its client organization. A test about a role sets it here, as the service that owns
/// organizations would answer it; the session's role claims count for nothing.
/// </remarks>
public sealed class ProbeStanding : ICurrentStanding
{
    private readonly HttpContextAccessor _accessor = new();
    private readonly ConcurrentDictionary<(Guid, Guid), MemberStanding?> _memberships = new();
    private readonly ConcurrentDictionary<Guid, Guid?> _providers = new();
    private readonly ConcurrentDictionary<(Guid, Guid), ProviderRole?> _staff = new();
    private int _reads;

    /// <summary>
    /// How many times the standing was asked.
    /// </summary>
    public int Reads => _reads;

    /// <summary>
    /// The person's membership now, or with null none.
    /// </summary>
    public void Member(Guid person, Guid organization, MemberStanding? standing) => _memberships[(person, organization)] = standing;

    /// <summary>
    /// The provider administering the organization now, or none.
    /// </summary>
    public void ProviderOf(Guid organization, Guid? provider) => _providers[organization] = provider;

    /// <summary>
    /// The person's role on the provider's staff now, or with null none.
    /// </summary>
    public void Staff(Guid person, Guid provider, ProviderRole? role) => _staff[(person, provider)] = role;

    /// <inheritdoc />
    public Task<MemberStanding?> MembershipAsync(Guid person, Guid organization, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _reads);
        return Task.FromResult(_memberships.TryGetValue((person, organization), out var set) ? set : MemberStanding.Of(OrganizationRole.None));
    }

    /// <inheritdoc />
    public Task<Guid?> ProviderOfAsync(Guid organization, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _reads);
        return Task.FromResult(_providers.TryGetValue(organization, out var set)
            ? set
            : Guid.TryParse(_accessor.HttpContext?.User.FindFirst("provider")?.Value, out var provider) ? provider : (Guid?)null);
    }

    /// <inheritdoc />
    public Task<ProviderRole?> ProviderRoleAsync(Guid person, Guid provider, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _reads);
        return Task.FromResult(_staff.TryGetValue((person, provider), out var set) ? set : ProviderRole.None);
    }
}
