namespace Bitwarden.Server.Sdk.Context.Tests;

public sealed class TokenAudienceTests
{
    [Fact]
    public void Platform_IsBitwarden()
    {
        Assert.Equal("bitwarden", TokenAudience.Platform);
    }
}
