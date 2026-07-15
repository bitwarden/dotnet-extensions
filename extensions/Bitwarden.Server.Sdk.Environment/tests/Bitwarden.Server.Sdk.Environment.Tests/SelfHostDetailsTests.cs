using Bitwarden.Server.Sdk.Environment.Internals;

namespace Bitwarden.Server.Sdk.Environment.Tests;

public class SelfHostDetailsTests
{
    [Fact]
    public void Default_IsCloud()
    {
        var details = new SelfHostDetails();

        Assert.False(details.SelfHosted);
        Assert.Null(details.SelfHostFlavor);
    }

    [Fact]
    public void MakeSelfHost_SetsSelfHostedAndFlavor()
    {
        var details = new SelfHostDetails();
        details.MakeSelfHost("docker");

        Assert.True(details.SelfHosted);
        Assert.Equal("docker", details.SelfHostFlavor);
    }

    [Fact]
    public void MakeCloud_ClearsSelfHostedAndFlavor()
    {
        var details = new SelfHostDetails();
        details.MakeSelfHost("docker");
        details.MakeCloud();

        Assert.False(details.SelfHosted);
        Assert.Null(details.SelfHostFlavor);
    }

    [Theory]
    [InlineData("docker")]
    [InlineData("kubernetes")]
    [InlineData("custom")]
    public void MakeSelfHost_PreservesFlavor(string flavor)
    {
        var details = new SelfHostDetails();
        details.MakeSelfHost(flavor);

        Assert.Equal(flavor, details.SelfHostFlavor);
    }
}
