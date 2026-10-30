namespace Bitwarden.Server.Sdk.Common.Tests;

public sealed class CallerContextAssertionTests
{
    private static readonly Guid Organization = Guid.Parse("a0000000-0000-4000-8000-000000000001");

    [Fact]
    public void Equal_ByValue()
    {
        Assert.Equal(
            new CallerContextAssertion(Organization, null, null, "{\"v\":1}"),
            new CallerContextAssertion(Organization, null, null, "{\"v\":1}"));
    }

    [Fact]
    public void NotEqual_WhenTheContextDiffers()
    {
        Assert.NotEqual(
            new CallerContextAssertion(Organization, null, null, "{\"v\":1}"),
            new CallerContextAssertion(Organization, null, null, "{\"v\":1,\"deviceType\":\"10\"}"));
    }
}
