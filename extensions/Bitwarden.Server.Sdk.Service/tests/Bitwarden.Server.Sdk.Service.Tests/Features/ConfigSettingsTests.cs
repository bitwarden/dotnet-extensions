using System.Text.Json;
using Bitwarden.Server.Sdk.Service.Features;

namespace Bitwarden.Server.Sdk.Service.Tests.Features;

/// <summary>
/// How a deployment is set up: every switch off until configured, and written under the names clients read.
/// </summary>
public sealed class ConfigSettingsTests
{
    [Fact]
    public void Defaults_EverySwitchOff()
    {
        var settings = new ConfigSettings();

        Assert.False(settings.DisableUserRegistration);
        Assert.False(settings.SuppressOnboardingInterstitials);
        Assert.False(settings.SelfHosted);
    }

    [Fact]
    public void Written_UnderTheNamesClientsRead()
    {
        var settings = new ConfigSettings { DisableUserRegistration = true, SelfHosted = true };

        using var written = JsonSerializer.SerializeToDocument(settings, JsonSerializerOptions.Web);

        Assert.True(written.RootElement.GetProperty("disableUserRegistration").GetBoolean());
        Assert.False(written.RootElement.GetProperty("suppressOnboardingInterstitials").GetBoolean());
        Assert.True(written.RootElement.GetProperty("selfHosted").GetBoolean());
    }

    [Fact]
    public void Equality_IsByValue()
    {
        Assert.Equal(new ConfigSettings { SelfHosted = true }, new ConfigSettings { SelfHosted = true });
        Assert.NotEqual(new ConfigSettings { SelfHosted = true }, new ConfigSettings());
    }
}
