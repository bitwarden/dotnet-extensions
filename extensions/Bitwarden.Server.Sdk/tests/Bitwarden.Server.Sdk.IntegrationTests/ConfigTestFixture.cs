using DotNet.Testcontainers.Containers;

namespace Bitwarden.Server.Sdk.IntegrationTests;

public sealed class ConfigTestFixture
{
    public const string LegacyEntryPointDebug = "legacy-entrypoint-debug";
    public const string LegacyEntryPointRelease = "legacy-entrypoint-release";
    public const string MinimalDebug = "minimal-debug";
    public const string MinimalRelease = "minimal-release";

    public ConfigTestFixture()
    {
        var legacyEntrypoint = """
        var _ = Host
            .CreateDefaultBuilder()
            .ConfigureWebHostDefaults(_ => {})
            .UseBitwardenSdk()
            .ConfigureAppConfiguration((_, config) =>
            {
                Print(config);
            })
            .Build();
        """;
        CreateImage(LegacyEntryPointDebug, useRelease: false, legacyEntrypoint);
        CreateImage(LegacyEntryPointRelease, useRelease: true, legacyEntrypoint);

        var minimalEntrypoint = """
        var builder = WebApplication.CreateBuilder();
        builder.UseBitwardenSdk();
        Print(builder.Configuration);
        """;

        CreateImage(MinimalDebug, useRelease: false, minimalEntrypoint);
        CreateImage(MinimalRelease, useRelease: true, minimalEntrypoint);
    }

    private static void CreateImage(string name, bool useRelease, string setupCode)
    {
        using var project = new TempDotNetProject();

        // Include a label that will make this image get auto cleaned up by test containers.
        project.WithItem("ContainerLabel", ResourceReaper.ResourceReaperSessionLabel, new Dictionary<string, string?>
        {
            { "Value", ResourceReaper.DefaultSessionId.ToString("D") },
        });

        project.WithFile("Program.cs", $$"""
            using Microsoft.Extensions.Configuration.EnvironmentVariables;
            using Microsoft.Extensions.Configuration.Memory;
            using Microsoft.Extensions.Configuration.Json;

            {{setupCode}}

            static void Print(IConfigurationBuilder builder)
            {
                foreach (var source in builder.Sources)
                {
                    PrintSource(source);
                }
            }

            static void PrintSource(IConfigurationSource source)
            {
                if (source is EnvironmentVariablesConfigurationSource env)
                {
                    Console.WriteLine($"Environment: {(env.Prefix == null ? "*" : env.Prefix)}");
                }
                else if (source is MemoryConfigurationSource)
                {
                    Console.WriteLine("Memory");
                }
                else if (source is JsonConfigurationSource json)
                {
                    Console.WriteLine($"Json: {json.Path}");
                }
                else if (source is ChainedConfigurationSource chained)
                {
                    Console.WriteLine("Chained");
                    if (chained.Configuration is IConfigurationRoot root)
                    {
                        foreach (var provider in root.Providers)
                        {
                            Console.WriteLine($"    {provider.GetType().Name}");
                        }
                    }
                }
                else
                {
                    Console.WriteLine($"Other: {source.GetType().Name}");
                }
            }

            Console.WriteLine("Done");
            """
        );

        var result = project.MsBuild(
            restore: true,
            targets: ["Publish", "PublishContainer"],
            extraProperties: new Dictionary<string, string>
            {
                { "ContainerRepository", name },
                { "ContainerFamily", "alpine" },
                { "Configuration", useRelease ? "Release" : "Debug" },
                { "UserSecretsId", "test-secrets" },
            });

        Assert.True(result.Succeeded, result.GetConsoleLog());
    }
}
