using Bitwarden.Server.Sdk.Environment.Internals;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Bitwarden.Server.Sdk.Environment.Tests;

/// <summary>
/// Shows how consumers can wire up <see cref="SelfHostDetails"/> from their own
/// <see cref="IConfiguration"/> without any changes to the <c>AddBitwardenEnvironment()</c> call.
/// The existing DI registration stays the same; callers add a separate
/// <c>AddOptions&lt;SelfHostDetails&gt;().Configure&lt;IConfiguration&gt;()</c> that reads
/// from their already-registered config.
/// </summary>
public class AddBitwardenEnvironmentTests
{
    private static IServiceProvider BuildServices(IConfiguration configuration,
        Action<IServiceCollection>? addOptions = null)
    {
        var hostEnvironment = Substitute.For<IHostEnvironment>();
        hostEnvironment.ApplicationName.Returns(
            typeof(AddBitwardenEnvironmentTests).Assembly.GetName().Name!);

        var services = new ServiceCollection();
        services.AddSingleton<IHostEnvironment>(hostEnvironment);
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<IConfiguration>(configuration);
        services.AddBitwardenEnvironment();

        addOptions?.Invoke(services);

        return services.BuildServiceProvider();
    }

    [Fact]
    public void SelfHosted_WhenConfiguredFromIConfiguration_IsTrue()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection([
                new("BitwardenSettings:SelfHosted", "true"),
                new("BitwardenSettings:SelfHostFlavor", "docker"),
            ])
            .Build();

        var sp = BuildServices(configuration, services =>
            services.AddOptions<SelfHostDetails>()
                .Configure<IConfiguration>((opts, config) =>
                {
                    if (config["BitwardenSettings:SelfHosted"] == "true")
                        opts.MakeSelfHost(config["BitwardenSettings:SelfHostFlavor"] ?? string.Empty);
                    else
                        opts.MakeCloud();
                }));

        var env = sp.GetRequiredService<IBitwardenEnvironment>();

        Assert.True(env.SelfHosted);
        Assert.Equal("docker", env.SelfHostFlavor);
    }

    [Fact]
    public void SelfHosted_WhenConfiguredAsCloudFromIConfiguration_IsFalse()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection([
                new("BitwardenSettings:SelfHosted", "false"),
            ])
            .Build();

        var sp = BuildServices(configuration, services =>
            services.AddOptions<SelfHostDetails>()
                .Configure<IConfiguration>((opts, config) =>
                {
                    if (config["BitwardenSettings:SelfHosted"] == "true")
                        opts.MakeSelfHost(config["BitwardenSettings:SelfHostFlavor"] ?? string.Empty);
                    else
                        opts.MakeCloud();
                }));

        var env = sp.GetRequiredService<IBitwardenEnvironment>();

        Assert.False(env.SelfHosted);
        Assert.Null(env.SelfHostFlavor);
    }

    [Fact]
    public void SelfHosted_WhenNoConfigureCallMade_DefaultsToCloud()
    {
        // No AddOptions<SelfHostDetails>() call — existing callers need no changes
        var sp = BuildServices(new ConfigurationBuilder().Build());

        var env = sp.GetRequiredService<IBitwardenEnvironment>();

        Assert.False(env.SelfHosted);
        Assert.Null(env.SelfHostFlavor);
    }
}
