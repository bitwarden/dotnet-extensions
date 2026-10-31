namespace Bitwarden.Server.Sdk.Service.Tests.Authorization;

/// <summary>
/// [NamesOrganization]: the route value an operation names an organization in, and where it may be declared.
/// </summary>
/// <remarks>
/// What the attribute lets a request do is pinned through the pipeline in <see cref="RequestNamedOwnerTests"/>.
/// </remarks>
public sealed class NamesOrganizationAttributeTests
{
    // The path's {organizationId}, as the API standards name an organization's identifier.
    [Fact]
    public void RouteValue_IsOrganizationId()
    {
        Assert.Equal("organizationId", NamesOrganizationAttribute.RouteValue);
    }

    [Fact]
    public void TheDeclaration_IsMadeOnceOnAnOperation()
    {
        var usage = (AttributeUsageAttribute)Attribute.GetCustomAttribute(
            typeof(NamesOrganizationAttribute), typeof(AttributeUsageAttribute))!;

        Assert.Equal(AttributeTargets.Method, usage.ValidOn);
        Assert.False(usage.AllowMultiple);
    }
}
