namespace Bitwarden.Server.Sdk.Context.Tests;

public sealed class ContextAssertionTests
{
    private static readonly Guid Organization = Guid.Parse("a0000000-0000-4000-8000-000000000001");

    [Fact]
    public void Equal_ByValue()
    {
        Assert.Equal(
            new ContextAssertion(Organization, null, null, "{\"v\":1}"),
            new ContextAssertion(Organization, null, null, "{\"v\":1}"));
    }

    [Fact]
    public void NotEqual_WhenTheContextDiffers()
    {
        Assert.NotEqual(
            new ContextAssertion(Organization, null, null, "{\"v\":1}"),
            new ContextAssertion(Organization, null, null, "{\"v\":1,\"deviceType\":\"10\"}"));
    }
}
