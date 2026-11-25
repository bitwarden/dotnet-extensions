namespace Bitwarden.Server.Sdk.Authorization.Tests;

/// <summary>
/// The scope an organization credential must hold to call an operation, which can't be left out.
/// </summary>
public sealed class AcceptsOrganizationCredentialAttributeTests
{
    [Fact]
    public void Constructor_KeepsTheScope()
    {
        Assert.Equal("widgets.organization", new AcceptsOrganizationCredentialAttribute("widgets.organization").Scope);
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
