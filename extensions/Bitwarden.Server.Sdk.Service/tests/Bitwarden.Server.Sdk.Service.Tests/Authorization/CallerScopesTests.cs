using System.Security.Claims;

namespace Bitwarden.Server.Sdk.Service.Tests.Authorization;

/// <summary>
/// Whether a caller's token carries a scope: one of its <c>scope</c> claims, exactly.
/// </summary>
public sealed class CallerScopesTests
{
    private static ClaimsPrincipal Caller(params string[] scopes) =>
        new(new ClaimsIdentity(scopes.Select(scope => new Claim("scope", scope)), "Bearer"));

    [Theory]
    [InlineData("vault.read")]
    [InlineData("vault.write")]
    public void Holds_AnyOneOfItsScopes_True(string scope)
    {
        Assert.True(CallerScopes.Holds(Caller("vault.read", "vault.write"), scope));
    }

    // Exact and case-sensitive: not a prefix, a differently cased name, or a space-separated list read as one.
    [Theory]
    [InlineData("vault")]
    [InlineData("Vault.Read")]
    [InlineData("vault.read ")]
    [InlineData("vault.delete")]
    public void Holds_AnythingElse_False(string scope)
    {
        Assert.False(CallerScopes.Holds(Caller("vault.read", "vault.write"), scope));
    }

    [Fact]
    public void Holds_NoScopeClaims_False()
    {
        Assert.False(CallerScopes.Holds(Caller(), "vault.read"));
    }

    // A claim of another type naming the scope is not a scope.
    [Fact]
    public void Holds_TheScopeInAnotherClaim_False()
    {
        var caller = new ClaimsPrincipal(new ClaimsIdentity([new Claim("scp", "vault.read")], "Bearer"));

        Assert.False(CallerScopes.Holds(caller, "vault.read"));
    }
}
