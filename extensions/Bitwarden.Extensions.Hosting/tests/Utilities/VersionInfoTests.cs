namespace Bitwarden.Extensions.Hosting.Tests.Utilities;

public class VersionInfoTests
{
    [Theory]
    [InlineData("1.0.0", "1.0.0", null)]
    [InlineData("1.0.0+af18b2952b5ddf910bd2f729a7c89a04b8d67084", "1.0.0", "af18b2952b5ddf910bd2f729a7c89a04b8d67084")]
    [InlineData("1.0.0+af18b", "1.0.0", "af18b")]
    public void TryParse_Works(string input, string version, string? gitHash)
    {
        var success = VersionInfo.TryParse(input, null, out var versionInfo);

        Assert.True(success);
        Assert.NotNull(versionInfo);
        Assert.Equal(version, versionInfo.Version.ToString());
        Assert.Equal(gitHash, versionInfo.GitHash);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("1.0.0+")]
    [InlineData("1.0.0+af18")]
    [InlineData("1.0.0+XXXXXXX")]
    public void TryParse_Fails(string? input)
    {
        Assert.False(VersionInfo.TryParse(input, null, out _));
    }
}
