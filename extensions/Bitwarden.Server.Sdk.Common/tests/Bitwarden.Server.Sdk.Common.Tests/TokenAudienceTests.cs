namespace Bitwarden.Server.Sdk.Common.Tests;

// The platform audience is what every service's token names; it must not drift from the value Identity issues.
public sealed class TokenAudienceTests
{
    [Fact]
    public void Platform_IsBitwarden()
    {
        Assert.Equal("bitwarden", TokenAudience.Platform);
    }
}
