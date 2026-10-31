using System.Security.Claims;
using Bitwarden.Server.Sdk.Common;
using Microsoft.AspNetCore.Authorization;

namespace Bitwarden.Server.Sdk.Service.Tests.Authorization;

/// <summary>
/// Admits a caller holding any one of an operation's scopes, refuses a provider where the operation forbids one, and
/// leaves an unauthenticated caller to be challenged.
/// </summary>
public sealed class AnyScopeHandlerTests
{
    private static readonly AnyScopeRequirement ReadOrInvite = new(["vault.read", "invitations.read"]);

    private static ClaimsPrincipal Caller(params string[] scopes) =>
        new(new ClaimsIdentity(scopes.Select(scope => new Claim("scope", scope)), "Bearer"));

    private static async Task<AuthorizationHandlerContext> Handle(
        AnyScopeRequirement requirement, ClaimsPrincipal caller, Guid? provider = null)
    {
        var context = new AuthorizationHandlerContext([requirement], caller, resource: null);
        await new AnyScopeHandler(new StatedContext(provider)).HandleAsync(context);
        return context;
    }

    [Theory]
    [InlineData("vault.read")]
    [InlineData("invitations.read")]
    public async Task Handle_HoldingAnyOneScope_Succeeds(string scope)
    {
        var context = await Handle(ReadOrInvite, Caller(scope));

        Assert.True(context.HasSucceeded);
    }

    // The refusal names every scope that would have admitted the caller.
    [Fact]
    public async Task Handle_HoldingNone_FailsNamingTheScopes()
    {
        var context = await Handle(ReadOrInvite, Caller("vault.write"));

        Assert.True(context.HasFailed);
        Assert.Equal(
            "One of the 'vault.read', 'invitations.read' scopes is required.",
            Assert.Single(context.FailureReasons).Message);
    }

    // Left unsucceeded and unfailed, so the caller is challenged with a 401 rather than forbidden.
    [Fact]
    public async Task Handle_Unauthenticated_NeitherSucceedsNorFails()
    {
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity([new Claim("scope", "vault.read")]));

        var context = await Handle(ReadOrInvite, anonymous);

        Assert.False(context.HasSucceeded);
        Assert.False(context.HasFailed);
    }

    [Fact]
    public async Task Handle_NamingAProviderWhereForbidden_FailsWhateverTheScope()
    {
        var context = await Handle(ReadOrInvite with { ForbidsProvider = true }, Caller("vault.read"), provider: Guid.NewGuid());

        Assert.True(context.HasFailed);
        Assert.Equal(
            $"The '{BitwardenHeaders.Provider}' header is not accepted by this operation.",
            Assert.Single(context.FailureReasons).Message);
    }

    [Fact]
    public async Task Handle_NamingAProviderWhereOptional_Succeeds()
    {
        var context = await Handle(ReadOrInvite, Caller("vault.read"), provider: Guid.NewGuid());

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task Handle_NamingNoProviderWhereForbidden_Succeeds()
    {
        var context = await Handle(ReadOrInvite with { ForbidsProvider = true }, Caller("vault.read"));

        Assert.True(context.HasSucceeded);
    }

    /// <summary>
    /// A request's context naming a provider, or nothing.
    /// </summary>
    private sealed class StatedContext(Guid? provider) : IContext
    {
        public Guid? OrganizationId => null;

        public Guid? UserId => null;

        public Guid? ProviderId => provider;
    }
}
