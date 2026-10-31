using System.Text.Json;
using Bitwarden.Server.Sdk.Service.Features;

namespace Bitwarden.Server.Sdk.Service.Tests.Features;

/// <summary>
/// Where a deployment is: nothing known until configured, and written under the names clients read.
/// </summary>
public sealed class ConfigEnvironmentTests
{
    [Fact]
    public void Defaults_NothingKnown()
    {
        var environment = new ConfigEnvironment();

        Assert.Null(environment.CloudRegion);
        Assert.Null(environment.Vault);
        Assert.Null(environment.FillAssistRules);
    }

    [Fact]
    public void Written_UnderTheNamesClientsRead()
    {
        var environment = new ConfigEnvironment
        {
            CloudRegion = "US",
            Vault = "https://vault.example",
            FillAssistRules = "https://rules.example/fill-assist.json",
        };

        using var written = JsonSerializer.SerializeToDocument(environment, JsonSerializerOptions.Web);

        Assert.Equal(
            ["cloudRegion", "vault", "fillAssistRules"],
            written.RootElement.EnumerateObject().Select(property => property.Name));
        Assert.Equal("US", written.RootElement.GetProperty("cloudRegion").GetString());
    }

    [Fact]
    public void Equality_IsByValue() =>
        Assert.Equal(new ConfigEnvironment { CloudRegion = "EU" }, new ConfigEnvironment { CloudRegion = "EU" });
}
