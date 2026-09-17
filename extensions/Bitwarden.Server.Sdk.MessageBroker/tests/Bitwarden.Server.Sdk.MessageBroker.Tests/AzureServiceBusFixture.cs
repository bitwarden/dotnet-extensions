using Azure.Messaging.ServiceBus;
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
                            "Name": "request-negotiation",
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
                          },
                          {
                            "Name": "request-shortlock",
                            "Properties": {
                              "DeadLetteringOnMessageExpiration": false,
                              "DefaultMessageTimeToLive": "PT1H",
                              "ForwardDeadLetteredMessagesTo": "",
                              "ForwardTo": "",
                              "LockDuration": "PT5S",
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
                            "Name": "reply-shortlock",
                            "Properties": {
                              "DeadLetteringOnMessageExpiration": false,
                              "DefaultMessageTimeToLive": "PT1H",
                              "ForwardDeadLetteredMessagesTo": "",
                              "ForwardTo": "",
                              "LockDuration": "PT5S",
                              "MaxDeliveryCount": 10,
                              "RequiresSession": true
                            },
                            "Rules": [
                              {
                                "Name": "$Default",
                                "Properties": {
                                  "FilterType": "Correlation",
                                  "CorrelationFilter": {
                                    "To": "reply-shortlock"
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

    /// <summary>
    /// Drains any residual messages on a non-session subscription so a test starts from empty.
    /// Uses <see cref="ServiceBusReceiveMode.ReceiveAndDelete"/> so messages are consumed
    /// without needing to complete each one individually.
    /// </summary>
    public async Task DrainSubscriptionAsync(string topic, string subscription)
    {
        await using var client = new ServiceBusClient(GetConnectionString());
        await using var receiver = client.CreateReceiver(topic, subscription,
            new ServiceBusReceiverOptions { ReceiveMode = ServiceBusReceiveMode.ReceiveAndDelete });
        IReadOnlyList<ServiceBusReceivedMessage> batch;
        do
        {
            batch = await receiver.ReceiveMessagesAsync(maxMessages: 100, maxWaitTime: TimeSpan.FromSeconds(1));
        } while (batch.Count > 0);
    }

    /// <summary>
    /// Drains any residual messages on a session-enabled subscription so a test starts from
    /// empty. Session receivers must be acquired one at a time with a short cancellation so we
    /// exit fast when nothing is left.
    /// </summary>
    public async Task DrainSessionsAsync(string topic, string subscription)
    {
        await using var client = new ServiceBusClient(GetConnectionString());
        while (true)
        {
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
            ServiceBusSessionReceiver receiver;
            try
            {
                receiver = await client.AcceptNextSessionAsync(
                    topic,
                    subscription,
                    new ServiceBusSessionReceiverOptions { ReceiveMode = ServiceBusReceiveMode.ReceiveAndDelete },
                    timeoutCts.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ServiceBusException ex) when (ex.Reason == ServiceBusFailureReason.ServiceTimeout)
            {
                break;
            }

            await using (receiver)
            {
                IReadOnlyList<ServiceBusReceivedMessage> batch;
                do
                {
                    batch = await receiver.ReceiveMessagesAsync(maxMessages: 100, maxWaitTime: TimeSpan.FromSeconds(1));
                } while (batch.Count > 0);
            }
        }
    }
}
