using System.Security.Claims;

namespace Bitwarden.Server.Sdk.Service;

/// <summary>
/// Which scopes the caller's token carries.
/// </summary>
public static class CallerScopes
{
    /// <summary>
    /// Whether the token carries a scope.
    /// </summary>
    /// <param name="caller">The authenticated caller.</param>
    /// <param name="scope">The scope.</param>
    /// <returns>True when one of its <c>scope</c> claims is exactly it.</returns>
    public static bool Holds(ClaimsPrincipal caller, string scope) =>
        caller.FindAll("scope").Any(claim => string.Equals(claim.Value, scope, StringComparison.Ordinal));
}
