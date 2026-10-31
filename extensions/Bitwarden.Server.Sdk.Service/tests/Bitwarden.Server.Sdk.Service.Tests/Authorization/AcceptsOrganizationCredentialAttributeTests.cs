namespace Bitwarden.Server.Sdk.Service.Tests.Authorization;

/// <summary>
/// [AcceptsOrganizationCredential]: the scope an organization credential must hold, which can't be left out.
/// </summary>
/// <remarks>
/// What the attribute lets a request do is pinned through the pipeline in <see cref="OrganizationCredentialTests"/>.
/// </remarks>
public sealed class AcceptsOrganizationCredentialAttributeTests
{
    [Fact]
    public void Constructor_KeepsTheScope()
    {
        Assert.Equal("api.organization", new AcceptsOrganizationCredentialAttribute("api.organization").Scope);
    }

    // Accepted under no scope would accept every organization credential: refused where it is declared.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_NoScope_Throws(string? scope)
    {
        var refusal = Assert.Throws<ArgumentException>(() => new AcceptsOrganizationCredentialAttribute(scope!));

        Assert.Equal("scope", refusal.ParamName);
    }

    [Fact]
    public void TheDeclaration_IsMadeOnceOnAnOperation()
    {
        var usage = (AttributeUsageAttribute)Attribute.GetCustomAttribute(
            typeof(AcceptsOrganizationCredentialAttribute), typeof(AttributeUsageAttribute))!;

        Assert.Equal(AttributeTargets.Method, usage.ValidOn);
        Assert.False(usage.AllowMultiple);
    }
}
