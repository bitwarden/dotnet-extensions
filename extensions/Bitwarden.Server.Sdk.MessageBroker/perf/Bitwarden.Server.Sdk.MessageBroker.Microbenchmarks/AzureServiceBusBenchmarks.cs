using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;

namespace Bitwarden.Server.Sdk.MessageBroker.Microbenchmarks;

public class AzureServiceBusBenchmarks : MessageBrokerBenchmarks
{
    private INetwork? _network;
    private IContainer? _sqlContainer;
    private IContainer? _emulatorContainer;
    private string? _configFile;

    private const string SqlPassword = "Password!123";
    private const string SqlAlias = "sqledge";

    protected override async Task StartInfrastructureAsync()
    {
        if (Environment.GetEnvironmentVariable("AZURE_SERVICE_BUS_CONNECTION_STRING") is not null)
            return;

        _network = new NetworkBuilder().Build();
        await _network.CreateAsync();

        _sqlContainer = new ContainerBuilder()
            .WithImage("mcr.microsoft.com/azure-sql-edge")
            .WithEnvironment("ACCEPT_EULA", "Y")
            .WithEnvironment("MSSQL_SA_PASSWORD", SqlPassword)
            .WithNetwork(_network)
            .WithNetworkAliases(SqlAlias)
            .WithWaitStrategy(Wait.ForUnixContainer()
                .UntilMessageIsLogged("SQL Server is now ready for client connections\\."))
            .Build();
        await _sqlContainer.StartAsync();

        _configFile = Path.Combine(Path.GetTempPath(), $"sb-emulator-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(_configFile, """
            {
              "UserConfig": {
                "Namespaces": [
                  {
                    "Name": "sbemulatorns",
                    "Properties": { "MaxAllowedConnections": 100 },
                    "Queues": [],
                    "Topics": [
                      {
                        "Name": "bench",
                        "Properties": {
                          "DefaultMessageTimeToLive": "PT1H",
                          "RequiresDuplicateDetection": false
                        },
                        "Subscriptions": [
                          {
                            "Name": "bench",
                            "Properties": {
                              "DeadLetteringOnMessageExpiration": false,
                              "DefaultMessageTimeToLive": "PT1H",
                              "ForwardDeadLetteredMessagesTo": "",
                              "ForwardTo": "",
                              "LockDuration": "PT1M",
                              "MaxDeliveryCount": 10,
                              "RequiresSession": false
                            }
                          }
                        ]
                      }
                    ]
                  }
                ],
                "Logging": { "Type": "File" }
              }
            }
            """);

        _emulatorContainer = new ContainerBuilder()
            .WithImage("mcr.microsoft.com/azure-messaging/servicebus-emulator:latest")
            .WithEnvironment("ACCEPT_EULA", "Y")
            .WithEnvironment("SQL_SERVER", SqlAlias)
            .WithEnvironment("MSSQL_SA_PASSWORD", SqlPassword)
            .WithNetwork(_network)
            .WithBindMount(_configFile, "/ServiceBus_Emulator/ConfigFiles/Config.json")
            .WithPortBinding(5672, true)
            .WithWaitStrategy(Wait.ForUnixContainer()
                .UntilMessageIsLogged("Emulator Service is Successfully Up!"))
            .Build();
        await _emulatorContainer.StartAsync();
    }

    protected override async Task StopInfrastructureAsync()
    {
        if (_emulatorContainer is not null) await _emulatorContainer.DisposeAsync();
        if (_sqlContainer is not null) await _sqlContainer.DisposeAsync();
        if (_network is not null) await _network.DisposeAsync();
        if (_configFile is not null) File.Delete(_configFile);
    }

    protected override Dictionary<string, string?> CreateConfig()
    {
        var connectionString = Environment.GetEnvironmentVariable("AZURE_SERVICE_BUS_CONNECTION_STRING");
        if (connectionString is null)
        {
            var port = _emulatorContainer!.GetMappedPublicPort(5672);
            connectionString = $"Endpoint=sb://localhost:{port};SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;";
        }

        return new() { { "AzureServiceBusConnectionString", connectionString } };
    }
}
