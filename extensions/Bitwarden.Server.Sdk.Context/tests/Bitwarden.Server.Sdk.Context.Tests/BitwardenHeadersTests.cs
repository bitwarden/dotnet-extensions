namespace Bitwarden.Server.Sdk.Context.Tests;

public sealed class BitwardenHeadersTests
{
    [Fact]
    public void Names_AreTheContract()
    {
        Assert.Equal("Bitwarden-Organization", BitwardenHeaders.Organization);
        Assert.Equal("Bitwarden-User", BitwardenHeaders.User);
        Assert.Equal("Bitwarden-Provider", BitwardenHeaders.Provider);
        Assert.Equal("Bitwarden-Context", BitwardenHeaders.Context);
        Assert.Equal("Bitwarden-Delegation", BitwardenHeaders.Delegation);
        Assert.Equal("Bitwarden-Call-Chain", BitwardenHeaders.CallChain);
    }

    // Not context: never signed into a context token, never refused as one.
    [Fact]
    public void CallChain_IsNotAContextHeader()
    {
        Assert.DoesNotContain(BitwardenHeaders.CallChain, BitwardenHeaders.All);
    }

    [Fact]
    public void All_AreTheFiveContextHeaders()
    {
        Assert.Equal(
            [BitwardenHeaders.Organization, BitwardenHeaders.User, BitwardenHeaders.Provider, BitwardenHeaders.Context, BitwardenHeaders.Delegation],
            BitwardenHeaders.All);
    }

    [Fact]
    public void All_CarryNoXPrefix()
    {
        Assert.All(BitwardenHeaders.All, name => Assert.False(name.StartsWith("X-", StringComparison.OrdinalIgnoreCase)));
    }
}
