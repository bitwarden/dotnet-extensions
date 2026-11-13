using System.Text.Json;
using Bitwarden.Server.Sdk.Service.Features;

namespace Bitwarden.Server.Sdk.Service.Tests.Features;

/// <summary>
/// The config resource: type <c>config</c>, no identifier, and its feature flags written as a map of their values.
/// </summary>
public sealed class ConfigTests
{
    [Fact]
    public void TheResource_IsTypedConfig_WithNoIdentifier()
    {
        var config = Config(new Dictionary<string, object>());

        Assert.Equal("config", config.Type);
        Assert.Null(config.Id);
    }

    // Each flag's value as it is: a boolean, a number or a string, under its key.
    [Fact]
    public void TheFlags_AreWrittenAsAMapOfTheirValues()
    {
        var config = Config(new Dictionary<string, object> { ["bool-flag"] = true, ["int-flag"] = 7, ["string-flag"] = "teal" });

        using var written = JsonSerializer.SerializeToDocument(config, JsonSerializerOptions.Web);

        var flags = written.RootElement.GetProperty("featureFlags");
        Assert.True(flags.GetProperty("bool-flag").GetBoolean());
        Assert.Equal(7, flags.GetProperty("int-flag").GetInt32());
        Assert.Equal("teal", flags.GetProperty("string-flag").GetString());
    }

    private static Config Config(IReadOnlyDictionary<string, object> flags) => new()
    {
        Environment = new ConfigEnvironment(),
        Settings = new ConfigSettings(),
        FeatureFlags = flags,
    };
}
