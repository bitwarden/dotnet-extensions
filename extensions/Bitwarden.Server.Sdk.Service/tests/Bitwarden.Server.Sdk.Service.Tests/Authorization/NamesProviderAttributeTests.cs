namespace Bitwarden.Server.Sdk.Service.Tests.Authorization;

/// <summary>
/// [NamesProvider]: the route value an operation names a provider in, and where it may be declared.
/// </summary>
/// <remarks>
/// What the attribute lets a request do is pinned through the pipeline in <see cref="RequestNamedOwnerTests"/>.
/// </remarks>
public sealed class NamesProviderAttributeTests
{
    // The path's {providerId}, as the API standards name a provider's identifier.
    [Fact]
    public void RouteValue_IsProviderId()
    {
        Assert.Equal("providerId", NamesProviderAttribute.RouteValue);
    }

    [Fact]
    public void TheDeclaration_IsMadeOnceOnAnOperation()
    {
        var usage = (AttributeUsageAttribute)Attribute.GetCustomAttribute(
            typeof(NamesProviderAttribute), typeof(AttributeUsageAttribute))!;

        Assert.Equal(AttributeTargets.Method, usage.ValidOn);
        Assert.False(usage.AllowMultiple);
    }
}
