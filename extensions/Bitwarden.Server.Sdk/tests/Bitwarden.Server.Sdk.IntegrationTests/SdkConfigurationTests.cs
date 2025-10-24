using DotNet.Testcontainers.Builders;
using Microsoft.Extensions.Logging;

namespace Bitwarden.Server.Sdk.IntegrationTests;

public class SdkConfigurationTests : IClassFixture<ConfigTestFixture>
{
    public static IEnumerable<TheoryDataRow<string, string, string>> ConfigTestData()
    {
        // Release cloud install should not get self hosted install
        yield return new TheoryDataRow<string, string, string>(
            // Image name
            ConfigTestFixture.MinimalRelease,
            // Environment variables
            "",
            // Expected config
            """
            Memory
            Environment: ASPNETCORE_
            Memory
            Environment: DOTNET_
            Json: appsettings.json
            Json: appsettings.Production.json
            Environment: *
            Chained
                MemoryConfigurationProvider
            """
        );

        // Debug builds running in development should use user secrets
        yield return new TheoryDataRow<string, string, string>(
            // Image name
            ConfigTestFixture.MinimalDebug,
            // Environment variables
            "ASPNETCORE_ENVIRONMENT:Development",
            // Expected config
            """
            Memory
            Environment: ASPNETCORE_
            Memory
            Environment: DOTNET_
            Json: appsettings.json
            Json: appsettings.Development.json
            Json: secrets.json
            Environment: *
            Chained
                MemoryConfigurationProvider
            """
        );

        // Self hosted installs should get self hosted json inserted
        yield return new TheoryDataRow<string, string, string>(
            // Image name
            ConfigTestFixture.MinimalRelease,
            // Environment variables
            "GlobalSettings__SelfHosted:true",
            // Expected config
            """
            Memory
            Environment: ASPNETCORE_
            Memory
            Environment: DOTNET_
            Json: appsettings.json
            Json: appsettings.Production.json
            Json: appsettings.SelfHosted.json
            Environment: *
            Chained
                MemoryConfigurationProvider
            """
        );

        // Debug self hosted installs should get self hosted json inserted
        yield return new TheoryDataRow<string, string, string>(
            // Image name
            ConfigTestFixture.MinimalDebug,
            // Environment variables
            """
            GlobalSettings__SelfHosted:true
            ASPNETCORE_ENVIRONMENT:Development
            """,
            // Expected config
            """
            Memory
            Environment: ASPNETCORE_
            Memory
            Environment: DOTNET_
            Json: appsettings.json
            Json: appsettings.Development.json
            Json: appsettings.SelfHosted.json
            Json: secrets.json
            Environment: *
            Chained
                MemoryConfigurationProvider
            """
        );

        // Old entrypoint style self-host debug
        yield return new TheoryDataRow<string, string, string>(
            // Setup code
            ConfigTestFixture.LegacyEntryPointDebug,
            // Environment variables
            """
            globalSettings__selfHosted:true
            ASPNETCORE_ENVIRONMENT:Development
            """,
            // Expected config
            """
            Chained
                MemoryConfigurationProvider
                MemoryConfigurationProvider
                EnvironmentVariablesConfigurationProvider
                ChainedConfigurationProvider
            Json: appsettings.json
            Json: appsettings.Development.json
            Json: appsettings.SelfHosted.json
            Json: secrets.json
            Environment: *
            """
        );

        // Old entrypoint style cloud debug
        yield return new TheoryDataRow<string, string, string>(
            // Image name
            ConfigTestFixture.LegacyEntryPointDebug,
            // Environment variables
            """
            ASPNETCORE_ENVIRONMENT:Development
            """,
            // Expected config
            """
            Chained
                MemoryConfigurationProvider
                MemoryConfigurationProvider
                EnvironmentVariablesConfigurationProvider
                ChainedConfigurationProvider
            Json: appsettings.json
            Json: appsettings.Development.json
            Json: secrets.json
            Environment: *
            """
        );

        // Old entrypoint style cloud release
        yield return new TheoryDataRow<string, string, string>(
            // Image name
            ConfigTestFixture.LegacyEntryPointRelease,
            // Environment variables
            "",
            // Expected config
            """
            Chained
                MemoryConfigurationProvider
                MemoryConfigurationProvider
                EnvironmentVariablesConfigurationProvider
                ChainedConfigurationProvider
            Json: appsettings.json
            Json: appsettings.Production.json
            Environment: *
            """
        );
    }

    [Theory]
    [MemberData(nameof(ConfigTestData))]
    public async Task SelfHostedConfigWorks(string imageName, string environmentVariableString, string expectedConfig)
    {
        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddProvider(new XUnitLoggerProvider());
        });

        var environmentVariables = environmentVariableString.Split('\n')
            .Select(line => line.Split(':'))
            .Where(v => v.Length == 2)
            .ToDictionary(v => v[0], v => v[1]);

        await using var testContainer = new ContainerBuilder()
            .WithImage(imageName)
            .WithLogger(loggerFactory.CreateLogger("Example"))
            .WithEnvironment(environmentVariables)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Done"))
            .Build();

        await testContainer.StartAsync(TestContext.Current.CancellationToken);

        var (stdout, _) = await testContainer.GetLogsAsync(timestampsEnabled: false, ct: TestContext.Current.CancellationToken);
        Assert.Equal($"{expectedConfig}\nDone", stdout.TrimEnd());
    }
}
