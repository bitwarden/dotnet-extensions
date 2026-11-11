using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Bitwarden.Server.Sdk.Client.Tests;

/// <summary>
/// More than one client registered for the same service: they share its options, its token and its circuit, so each
/// adds to what the others set rather than replacing it.
/// </summary>
public sealed class BitwardenClientServiceCollectionExtensionsTests
{
    private const string Callee = "callee-service";

    private static readonly Dictionary<string, string?> Configured = new()
    {
        [$"Services:{Callee}:BaseAddress"] = "http://callee.invalid",
        [$"Services:{Callee}:IdentityUri"] = "http://identity.invalid",
        [$"Services:{Callee}:CallerName"] = "caller",
        [$"Services:{Callee}:CallerSecret"] = "made-up-for-tests",
    };

    // One token serves every client of the service, so it carries the scopes all of them need.
    [Fact]
    public void TwoClientsForOneService_ShareTheScopesOfBoth()
    {
        using var services = Services(Configured, services =>
        {
            services.AddBitwardenClient<FirstClient, FirstClient>(Callee, ["callee.read", "callee.write"]);
            services.AddBitwardenClient<SecondClient, SecondClient>(Callee, ["callee.read", "callee.admin"]);
        });

        var options = services.GetRequiredService<IOptionsMonitor<BitwardenClientOptions>>().Get(Callee);

        Assert.Equal(["callee.read", "callee.write", "callee.admin"], options.Scopes);
    }

    // The second registration doesn't apply the conventions again over what the first one's configure set.
    [Fact]
    public void TwoClientsForOneService_KeepTheFirstClientsOverrides()
    {
        using var services = Services(Configured, services =>
        {
            services.AddBitwardenClient<FirstClient, FirstClient>(
                Callee, ["callee.read"], options => options.BaseAddress = "http://override.invalid");
            services.AddBitwardenClient<SecondClient, SecondClient>(Callee, ["callee.read"]);
        });

        var options = services.GetRequiredService<IOptionsMonitor<BitwardenClientOptions>>().Get(Callee);

        Assert.Equal("http://override.invalid", options.BaseAddress);
        Assert.Equal("caller", options.CallerName);
    }

    // One validator per service, so a client that isn't configured is reported once.
    [Fact]
    public void TwoClientsForOneService_ReportAMissingSettingOnce()
    {
        using var services = Services([], services =>
        {
            services.AddBitwardenClient<FirstClient, FirstClient>(Callee, ["callee.read"]);
            services.AddBitwardenClient<SecondClient, SecondClient>(Callee, ["callee.read"]);
        });

        var failure = Assert.Throws<OptionsValidationException>(
            () => services.GetRequiredService<IOptionsMonitor<BitwardenClientOptions>>().Get(Callee));

        Assert.Single(failure.Failures);
    }

    private static ServiceProvider Services(Dictionary<string, string?> configuration, Action<IServiceCollection> register)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(configuration).Build());
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        register(services);
        return services.BuildServiceProvider();
    }

    public sealed class FirstClient(HttpClient http)
    {
        public HttpClient Http { get; } = http;
    }

    public sealed class SecondClient(HttpClient http)
    {
        public HttpClient Http { get; } = http;
    }
}
