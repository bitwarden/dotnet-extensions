namespace Bitwarden.Server.Sdk.Common.Tests;

public sealed class BitwardenHeadersTests
{
    [Fact]
    public void Names_AreTheContract()
    {
        Assert.Equal("Bitwarden-Organization", BitwardenHeaders.Organization);
        Assert.Equal("Bitwarden-User", BitwardenHeaders.User);
        Assert.Equal("Bitwarden-Provider", BitwardenHeaders.Provider);
        Assert.Equal("Bitwarden-Context", BitwardenHeaders.Context);
    }

    [Fact]
    public void All_AreTheFourCallerContextHeaders()
    {
        Assert.Equal(
            [BitwardenHeaders.Organization, BitwardenHeaders.User, BitwardenHeaders.Provider, BitwardenHeaders.Context],
            BitwardenHeaders.All);
    }

    [Fact]
    public void All_CarryNoXPrefix()
    {
        Assert.All(BitwardenHeaders.All, name => Assert.False(name.StartsWith("X-", StringComparison.OrdinalIgnoreCase)));
    }
}
