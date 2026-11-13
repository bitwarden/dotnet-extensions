namespace Bitwarden.Server.Sdk.Authorization.Tests.CallChains;

/// <summary>
/// An accepted re-entry is a reviewed exception, so it always says why.
/// </summary>
public sealed class AcceptsReentryAttributeTests
{
    [Fact]
    public void Constructor_KeepsTheReason()
    {
        Assert.Equal(
            "The widget's price is read back before the order commits.",
            new AcceptsReentryAttribute("The widget's price is read back before the order commits.").Reason);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_NoReason_Throws(string? reason)
    {
        var refusal = Assert.Throws<ArgumentException>(() => new AcceptsReentryAttribute(reason!));

        Assert.Equal("reason", refusal.ParamName);
    }

    [Fact]
    public void TheDeclaration_IsMadeOnceOnAnOperation()
    {
        var usage = (AttributeUsageAttribute)Attribute.GetCustomAttribute(
            typeof(AcceptsReentryAttribute), typeof(AttributeUsageAttribute))!;

        Assert.Equal(AttributeTargets.Method, usage.ValidOn);
        Assert.False(usage.AllowMultiple);
    }
}
