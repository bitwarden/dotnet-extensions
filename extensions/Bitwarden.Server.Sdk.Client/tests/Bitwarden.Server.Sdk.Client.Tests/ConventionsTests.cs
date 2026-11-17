using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Bitwarden.Server.Sdk.Client.Tests;

/// <summary>
/// A call's timing comes from <c>Services:{Name}:Timeout</c>, <c>:Retries</c> and <c>:RetryDelay</c> where a deployment
/// sets them, over whatever the client package set in code; unset, the package's values stand.
/// </summary>
public sealed class ConventionsTests
{
    private const string Callee = "widget-service";

    [Fact]
    public void ApplyTiming_Unset_LeavesTheClientsOwnValues()
    {
        var options = new BitwardenClientOptions { Timeout = TimeSpan.FromSeconds(5), Retries = 1, RetryDelay = TimeSpan.FromSeconds(1) };

        Conventions.ApplyTiming(options, Configuration(), Callee);

        Assert.Equal(TimeSpan.FromSeconds(5), options.Timeout);
        Assert.Equal(1, options.Retries);
        Assert.Equal(TimeSpan.FromSeconds(1), options.RetryDelay);
    }

    [Fact]
    public void ApplyTiming_Set_TakesEach()
    {
        var options = new BitwardenClientOptions();

        Conventions.ApplyTiming(options, Configuration(("Timeout", "00:00:10"), ("Retries", "0"), ("RetryDelay", "00:00:00.5")), Callee);

        Assert.Equal(TimeSpan.FromSeconds(10), options.Timeout);
        Assert.Equal(0, options.Retries);
        Assert.Equal(TimeSpan.FromMilliseconds(500), options.RetryDelay);
    }

    [Theory]
    [InlineData("Timeout", "ten seconds")]
    [InlineData("RetryDelay", "1s")]
    [InlineData("Retries", "two")]
    [InlineData("Retries", "1.5")]
    public void ApplyTiming_AValueThatDoesNotParse_NamesItsKey(string setting, string value)
    {
        var failure = Assert.Throws<InvalidOperationException>(
            () => Conventions.ApplyTiming(new BitwardenClientOptions(), Configuration((setting, value)), Callee));

        Assert.Contains($"Services:{Callee}:{setting}", failure.Message, StringComparison.Ordinal);
    }

    // The deployment's setting wins over the client package's own configure, and a setting the validator refuses still
    // stops the service at startup.
    [Fact]
    public void AddBitwardenClient_ConfigurationOverridesTheClientPackage()
    {
        using var services = Services(Configuration(("Timeout", "00:00:05")), options => options.Timeout = TimeSpan.FromMinutes(2));

        Assert.Equal(TimeSpan.FromSeconds(5), services.GetRequiredService<IOptionsMonitor<BitwardenClientOptions>>().Get(Callee).Timeout);
    }

    [Fact]
    public void AddBitwardenClient_RetriesOutOfRange_IsRefused()
    {
        using var services = Services(Configuration(("Retries", "5")));

        Assert.Throws<OptionsValidationException>(() => services.GetRequiredService<IOptionsMonitor<BitwardenClientOptions>>().Get(Callee));
    }

    private static IConfiguration Configuration(params (string Setting, string Value)[] settings)
    {
        var values = new Dictionary<string, string?>
        {
            [$"Services:{Callee}:BaseAddress"] = "http://widget.invalid",
            [$"Services:{Callee}:IdentityUri"] = "http://identity.invalid",
            [$"Services:{Callee}:CallerName"] = "gadget-service",
            [$"Services:{Callee}:CallerSecret"] = "made-up-for-tests",
        };
        foreach (var (setting, value) in settings)
        {
            values[$"Services:{Callee}:{setting}"] = value;
        }

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private static ServiceProvider Services(IConfiguration configuration, Action<BitwardenClientOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(configuration);
        services.AddLogging();
        services.AddBitwardenClient<HttpClient, HttpClient>(Callee, ["widget.read"], configure);
        return services.BuildServiceProvider();
    }
}
