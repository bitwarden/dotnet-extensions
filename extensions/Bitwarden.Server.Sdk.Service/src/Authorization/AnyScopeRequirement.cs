using Microsoft.AspNetCore.Authorization;

namespace Bitwarden.Server.Sdk.Service;

/// <summary>
/// The requirement an <see cref="AnyScopeAttribute"/> puts on its operation.
/// </summary>
/// <param name="Scopes">Any one of these admits the caller.</param>
/// <param name="ForbidsProvider">Whether a request naming a provider is refused.</param>
internal sealed record AnyScopeRequirement(IReadOnlyList<string> Scopes, bool ForbidsProvider = false)
    : IAuthorizationRequirement, IAuthorizationRequirementData
{
    public IEnumerable<IAuthorizationRequirement> GetRequirements() => [this];
}
