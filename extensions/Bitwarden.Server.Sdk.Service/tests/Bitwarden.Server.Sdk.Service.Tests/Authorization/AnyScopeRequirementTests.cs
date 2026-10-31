using Microsoft.AspNetCore.Authorization;

namespace Bitwarden.Server.Sdk.Service.Tests.Authorization;

/// <summary>
/// The requirement [AnyScope] puts on an operation: it is its own requirement, and leaves a provider allowed unless told.
/// </summary>
public sealed class AnyScopeRequirementTests
{
    [Fact]
    public void GetRequirements_IsItself()
    {
        var requirement = new AnyScopeRequirement(["vault.read", "invitations.read"]);

        Assert.Same(requirement, Assert.Single(requirement.GetRequirements()));
    }

    [Fact]
    public void ForbidsProvider_DefaultsToFalse()
    {
        Assert.False(new AnyScopeRequirement(["vault.read"]).ForbidsProvider);
    }

    // Metadata the authorization policy builder reads, as well as the requirement its handler handles.
    [Fact]
    public void IsBothRequirementAndRequirementData()
    {
        var requirement = new AnyScopeRequirement(["vault.read"], ForbidsProvider: true);

        Assert.IsAssignableFrom<IAuthorizationRequirement>(requirement);
        Assert.IsAssignableFrom<IAuthorizationRequirementData>(requirement);
        Assert.True(requirement.ForbidsProvider);
    }
}
