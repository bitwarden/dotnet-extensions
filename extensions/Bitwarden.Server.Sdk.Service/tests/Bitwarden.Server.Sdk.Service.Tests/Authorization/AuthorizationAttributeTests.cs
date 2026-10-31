using Microsoft.AspNetCore.Authorization;

namespace Bitwarden.Server.Sdk.Service.Tests.Authorization;

/// <summary>
/// [Authorization]: what an operation demands of its caller, which inherits whatever it leaves unsaid and demands
/// nothing until the platform has settled it.
/// </summary>
public sealed class AuthorizationAttributeTests
{
    // Unset, so an operation saying nothing about a context member inherits its class's or service's answer; no
    // role, permission or provider role; and a token required.
    [Fact]
    public void Defaults_InheritTheContextAndDemandNoRole()
    {
        var declared = new AuthorizationAttribute();

        Assert.Null(declared.Scope);
        Assert.Equal(ContextRequirement.Unset, declared.Organization);
        Assert.Equal(ContextRequirement.Unset, declared.User);
        Assert.Equal(ContextRequirement.Unset, declared.Provider);
        Assert.Equal(OrganizationRole.None, declared.OrganizationRole);
        Assert.Equal(OrganizationPermission.None, declared.Permission);
        Assert.Equal(ProviderRole.None, declared.ProviderRole);
        Assert.False(declared.Anonymous);
        Assert.False(declared.Settled);
    }

    // Requirements compose as "and": a declared attribute demanding something would stop an operation relaxing its
    // class, so only the settled one demands anything.
    [Fact]
    public void GetRequirements_Declared_DemandsNothing()
    {
        Assert.Empty(new AuthorizationAttribute { Scope = "vault.write", Organization = ContextRequirement.Required }.GetRequirements());
    }

    [Fact]
    public void GetRequirements_Settled_IsOneRequirementCarryingIt()
    {
        var settled = new AuthorizationAttribute { Scope = "vault.write", Settled = true };

        var requirement = Assert.IsType<ContextRequirements>(Assert.Single(settled.GetRequirements()));
        Assert.Same(settled, requirement.Options);
    }

    // Still the framework's [Authorize], so anything reading authorization metadata sees an authorized endpoint.
    [Fact]
    public void IsAnAuthorizeAttribute_WithNoPolicyOfItsOwn()
    {
        var declared = new AuthorizationAttribute { Scope = "vault.read" };

        Assert.IsAssignableFrom<IAuthorizeData>(declared);
        Assert.Null(declared.Policy);
        Assert.Null(declared.Roles);
    }

    // On a class to set what its handlers share, or on one operation; once at either.
    [Fact]
    public void TheDeclaration_IsMadeOnceOnAClassOrAnOperation()
    {
        var usage = (AttributeUsageAttribute)Attribute.GetCustomAttribute(
            typeof(AuthorizationAttribute), typeof(AttributeUsageAttribute))!;

        Assert.Equal(AttributeTargets.Class | AttributeTargets.Method, usage.ValidOn);
        Assert.False(usage.AllowMultiple);
    }
}
