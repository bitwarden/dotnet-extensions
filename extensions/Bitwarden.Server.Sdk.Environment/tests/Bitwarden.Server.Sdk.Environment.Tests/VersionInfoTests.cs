namespace Bitwarden.Server.Sdk.Environment.Tests;

public class VersionInfoTests
{
    [Theory]
    [InlineData("1.0.0", "1.0.0", null)]
    [InlineData("1.0.0+af18b2952b5ddf910bd2f729a7c89a04b8d67084", "1.0.0", "af18b2952b5ddf910bd2f729a7c89a04b8d67084")]
    [InlineData("1.0.0+af18b", "1.0.0", "af18b")]
    [InlineData("1.0.0-alpha.1", "1.0.0", null)]
    [InlineData("1.0.0-alpha.1+af18b2952b5ddf910bd2f729a7c89a04b8d67084", "1.0.0", "af18b2952b5ddf910bd2f729a7c89a04b8d67084")]
    [InlineData("1.0.0-beta.2+af18b", "1.0.0", "af18b")]
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
    [InlineData("notaversion+af18b2952b5ddf910bd2f729a7c89a04b8d67084")]
    [InlineData("1.0.0+af18b2952z")] // valid hex prefix + non-hex suffix; anchored regex rejects, unanchored would not
    public void TryParse_Fails(string? input)
    {
        Assert.False(VersionInfo.TryParse(input, null, out _));
    }

    [Theory]
    [InlineData("1.0.0", "1.0.0", null)]
    [InlineData("1.0.0+af18b2952b5ddf910bd2f729a7c89a04b8d67084", "1.0.0", "af18b2952b5ddf910bd2f729a7c89a04b8d67084")]
    public void Parse_ValidInput_ReturnsResult(string input, string version, string? gitHash)
    {
        var result = VersionInfo.Parse(input, null);

        Assert.Equal(version, result.Version.ToString());
        Assert.Equal(gitHash, result.GitHash);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("notaversion")]
    public void Parse_InvalidInput_ThrowsFormatException(string? input)
    {
        Assert.Throws<FormatException>(() => VersionInfo.Parse(input, null));
    }
}
