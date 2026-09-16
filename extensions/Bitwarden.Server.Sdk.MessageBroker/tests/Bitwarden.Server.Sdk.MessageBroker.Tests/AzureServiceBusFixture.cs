using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;

namespace Bitwarden.Server.Sdk.MessageBroker.Tests;

/// <summary>
/// Shared xUnit class-fixture that starts the Azure Service Bus emulator (plus its SQL Edge
/// dependency) in Docker containers, with a pre-provisioned topology used by every ASB-backed
/// test class.
/// </summary>
public class AzureServiceBusFixture : IAsyncLifetime
{
    private INetwork? _network;
    private IContainer? _sqlContainer;
    private IContainer? _emulatorContainer;
    private string? _configFile;

    private const string SqlPassword = "Password!123";
    private const string SqlAlias = "sqledge";

    public string GetConnectionString()
    {
        var port = _emulatorContainer!.GetMappedPublicPort(5672);
        return $"Endpoint=sb://localhost:{port};SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;";
    }

    public async ValueTask InitializeAsync()
    {
        _network = new NetworkBuilder().Build();
        await _network.CreateAsync(TestContext.Current.CancellationToken);

        _sqlContainer = new ContainerBuilder()
            .WithImage("mcr.microsoft.com/azure-sql-edge")
            .WithEnvironment("ACCEPT_EULA", "Y")
            .WithEnvironment("MSSQL_SA_PASSWORD", SqlPassword)
            .WithNetwork(_network)
            .WithNetworkAliases(SqlAlias)
            .WithWaitStrategy(Wait.ForUnixContainer()
                .UntilMessageIsLogged("SQL Server is now ready for client connections\\."))
            .Build();

        await _sqlContainer.StartAsync(TestContext.Current.CancellationToken);

        _configFile = Path.Combine(Path.GetTempPath(), $"sb-emulator-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(_configFile, """
            {
              "UserConfig": {
                "Namespaces": [
                  {
                    "Name": "sbemulatorns",
                    "Properties": {
                      "MaxAllowedConnections": 100
                    },
                    "Queues": [],
                    "Topics": [
                      {
                        "Name": "behavior",
                        "Properties": {
                          "DefaultMessageTimeToLive": "PT1H",
                          "RequiresDuplicateDetection": false
                        },
                        "Subscriptions": [
                          {
                            "Name": "behavior",
                            "Properties": {
                              "DeadLetteringOnMessageExpiration": false,
                              "DefaultMessageTimeToLive": "PT1H",
                              "ForwardDeadLetteredMessagesTo": "",
                              "ForwardTo": "",
                              "LockDuration": "PT30S",
                              "MaxDeliveryCount": 10,
                              "RequiresSession": false
                            }
                          },
                          {
                            "Name": "pm",
                            "Properties": {
                              "DeadLetteringOnMessageExpiration": false,
                              "DefaultMessageTimeToLive": "PT1H",
                              "ForwardDeadLetteredMessagesTo": "",
                              "ForwardTo": "",
                              "LockDuration": "PT30S",
                              "MaxDeliveryCount": 10,
                              "RequiresSession": false
                            }
                          },
                          {
                            "Name": "sm",
                            "Properties": {
                              "DeadLetteringOnMessageExpiration": false,
                              "DefaultMessageTimeToLive": "PT1H",
                              "ForwardDeadLetteredMessagesTo": "",
                              "ForwardTo": "",
                              "LockDuration": "PT30S",
                              "MaxDeliveryCount": 10,
                              "RequiresSession": false
                            }
                          }
                        ]
                      },
                      {
                        "Name": "ctrl",
                        "Properties": {
                          "DefaultMessageTimeToLive": "PT1H",
                          "RequiresDuplicateDetection": false
                        },
                        "Subscriptions": [
                          {
                            "Name": "pub-negotiation",
                            "Properties": {
                              "DeadLetteringOnMessageExpiration": false,
                              "DefaultMessageTimeToLive": "PT1H",
                              "ForwardDeadLetteredMessagesTo": "",
                              "ForwardTo": "",
                              "LockDuration": "PT30S",
                              "MaxDeliveryCount": 10,
                              "RequiresSession": true
                            },
                            "Rules": [
                              {
                                "Name": "$Default",
                                "Properties": {
                                  "FilterType": "Sql",
                                  "SqlFilter": {
                                    "SqlExpression": "[data-topic] IS NOT NULL"
                                  }
                                }
                              }
                            ]
                          },
                          {
                            "Name": "reply-negotiation",
                            "Properties": {
                              "DeadLetteringOnMessageExpiration": false,
                              "DefaultMessageTimeToLive": "PT1H",
                              "ForwardDeadLetteredMessagesTo": "",
                              "ForwardTo": "",
                              "LockDuration": "PT30S",
                              "MaxDeliveryCount": 10,
                              "RequiresSession": true
                            },
                            "Rules": [
                              {
                                "Name": "$Default",
                                "Properties": {
                                  "FilterType": "Correlation",
                                  "CorrelationFilter": {
                                    "To": "reply-negotiation"
                                  }
                                }
                              }
                            ]
                          }
                        ]
                      }
                    ]
                  }
                ],
                "Logging": {
                  "Type": "File"
                }
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

        await _emulatorContainer.StartAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_emulatorContainer != null) await _emulatorContainer.DisposeAsync();
        if (_sqlContainer != null) await _sqlContainer.DisposeAsync();
        if (_network != null) await _network.DisposeAsync();
        if (_configFile != null) File.Delete(_configFile);
    }
}
