using Bitwarden.Core;
using System.Diagnostics;

namespace Bitwarden.Extensions.Configuration.Tests;

public class SecretsManagerConfigurationProviderTests
{
    private static readonly Guid _testingProjectId = Guid.Parse("e9ebeeea-7aea-48c8-9adb-afcc014f1d46");
    private static readonly AccessToken _testingAccessToken = AccessToken.Parse("0.4eaea7be-6a0b-4c0b-861e-b033001532a9.ydNqCpyZ8E7a171FjZn89WhKE1eEQF:2WQh70hSQQZFXm+QteNYsg==");

    [Fact]
    public void Load_Simple_Works()
    {
        using var provider = CreateProvider();

        provider.Load();

        Assert.True(provider.TryGet("Test", out var value));
        Assert.Equal("Test", value);
    }

    [DebuggerFact]
    public async void Load_Reload_Works()
    {
        using var provider = CreateProvider(o => o.ReloadInterval = TimeSpan.FromSeconds(10));
        provider.Load();

        Assert.True(provider.TryGet("ChangableValue", out var originalValue));

        // Go change the value of this
        Debugger.Break();

        // Ensure there has been enough time for the provider to have refreshed values
        await Task.Delay(TimeSpan.FromSeconds(20));

        Assert.True(provider.TryGet("ChangableValue", out var newValue));
        Assert.NotEqual(newValue, originalValue);

        Assert.True(provider.TryGet("Test", out _));
    }

    private static SecretsManagerConfigurationProvider CreateProvider(Action<SecretsManagerConfigurationOptions>? configureOptions = null)
    {
        var options = new SecretsManagerConfigurationOptions
        {
            Environment = BitwardenEnvironment.DevelopmentEnvironment,
            ProjectId = _testingProjectId,
            AccessToken = _testingAccessToken,
        };

        configureOptions?.Invoke(options);
        return new SecretsManagerConfigurationProvider(options);
    }
}
