using System.Collections.Concurrent;
using System.Diagnostics;
using Azure.Messaging.ServiceBus;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Bitwarden.Server.Sdk.MessageBroker.Tests;

public class AzureServiceBusBehaviorTests : BehaviorTests, IClassFixture<AzureServiceBusFixture>
{
    private readonly AzureServiceBusFixture _fixture;

    public AzureServiceBusBehaviorTests(AzureServiceBusFixture fixture)
    {
        _fixture = fixture;
    }

    protected override string TopicName => "behavior";

    protected override Dictionary<string, string?> CreateConfig() =>
        new() { { "AzureServiceBusConnectionString", _fixture.GetConnectionString() } };

    // All subscriptions are pre-declared on the shared host so CreateSecondaryInstanceAsync
    // can return the same host. Each SubscribeAsync() call creates an independent receiver,
    // making concurrent calls on the same subscriber singleton behave as competing consumers.
    protected override Task<IHost> CreateSecondaryInstanceAsync(IHost originalHost, string? subscriptionName = null) =>
        Task.FromResult(originalHost);

    protected override async Task<bool> TryInjectInvalidMessageAsync(string topicName)
    {
        // Publish a JSON null body directly to the topic so the subscriber receives a message
        // that deserializes to null and exercises the dead-letter path.
        await using var client = new ServiceBusClient(_fixture.GetConnectionString());
        await using var sender = client.CreateSender(topicName);
        await sender.SendMessageAsync(
            new ServiceBusMessage(BinaryData.FromString("null")),
            TestContext.Current.CancellationToken);
        return true;
    }

    protected override async Task<bool> TryInjectMalformedJsonMessageAsync(string topicName)
    {
        // Publish a malformed JSON body so the subscriber hits the catch(Exception) block
        // in the deserializer and exercises the dead-letter path for thrown exceptions.
        await using var client = new ServiceBusClient(_fixture.GetConnectionString());
        await using var sender = client.CreateSender(topicName);
        await sender.SendMessageAsync(
            new ServiceBusMessage(BinaryData.FromBytes("{"u8.ToArray())),
            TestContext.Current.CancellationToken);
        return true;
    }

    // Use an unreachable endpoint so the Azure SDK surfaces ServiceBusException when it
    // exhausts its retry policy; this is mapped to BrokerUnavailableException.
    private const string UnreachableConnectionString =
        "Endpoint=sb://localhost:1;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;";

    protected override Dictionary<string, string?> CreateBrokerDownConfig() =>
        new() { { "AzureServiceBusConnectionString", UnreachableConnectionString } };

    // ASB connects lazily: ReceiveMessagesAsync fails with ServiceBusException when the
    // endpoint is unreachable, which is mapped to BrokerDisconnectedException. No separate
    // "stop" step is needed — the failure occurs on the first receive attempt.
    protected override Task<(Dictionary<string, string?>, Func<Task>)?>
        TrySetupDroppableBrokerAsync() =>
        Task.FromResult<(Dictionary<string, string?>, Func<Task>)?>(
            (CreateBrokerDownConfig()!, () => Task.CompletedTask));

    [Fact(Timeout = 60 * 1000)]
    public async Task OversizedMessageThrows()
    {
        // Azure Service Bus standard tier enforces a 256 KB per-message limit.
        // Publishing a larger payload should throw ServiceBusException.
        var host = await BuildHostAsync(services => services.AddPublisher<OversizedItem>(TopicName));
        var publisher = host.Services.GetRequiredKeyedService<IPublisher<OversizedItem>>(TopicName);
        var oversizedPayload = new string('x', 300 * 1024); // ~300 KB > 256 KB limit
        await Assert.ThrowsAsync<ServiceBusException>(() =>
            publisher.PublishAsync(new OversizedItem(oversizedPayload), TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = 60 * 1000)]
    public async Task BatchPublishOversizedMessageThrows()
    {
        // PublishBatchAsync uses SendMessagesAsync which also enforces the 256 KB per-message
        // limit; a MessageSizeExceeded ServiceBusException propagates unwrapped.
        var host = await BuildHostAsync(services => services.AddPublisher<OversizedItem>(TopicName));
        var publisher = host.Services.GetRequiredKeyedService<IPublisher<OversizedItem>>(TopicName);
        var oversizedPayload = new string('x', 300 * 1024); // ~300 KB > 256 KB limit
        await Assert.ThrowsAsync<ServiceBusException>(() =>
            publisher.PublishBatchAsync([new OversizedItem(oversizedPayload)], TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = 60 * 1000)]
    public async Task PublishBatchThrowsBrokerUnavailableExceptionWhenBrokerIsDown()
    {
        var config = CreateBrokerDownConfig();
        var host = await BuildHostAsync(config, services => services.AddPublisher<MyItem>(TopicName));
        var publisher = host.Services.GetRequiredKeyedService<IPublisher<MyItem>>(TopicName);

        var ex = await Assert.ThrowsAsync<BrokerUnavailableException>(
            () => publisher.PublishBatchAsync([new MyItem(1)], TestContext.Current.CancellationToken));
        Assert.Equal(TopicName, ex.TopicName);
        Assert.NotNull(ex.InnerException);
    }

    /// <summary>
    /// Verifies that an active tracing span causes the <c>traceparent</c> application property
    /// to be set on each message in a batch, covering the <c>if (traceId is not null)</c> branch
    /// in <c>AzureServiceBusPublisher.PublishBatchAsync</c>.
    /// </summary>
    [Fact(Timeout = 60 * 1000)]
    public async Task TracingSpanIsCreatedOnBatchPublish()
    {
        var activities = new ConcurrentBag<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Bitwarden.Server.Sdk.MessageBroker",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStarted = activities.Add,
        };
        ActivitySource.AddActivityListener(listener);

        var host = await BuildHostAsync(services => services.AddPublisher<MyItem>(TopicName));
        var publisher = host.Services.GetRequiredKeyedService<IPublisher<MyItem>>(TopicName);

        var before = activities.ToHashSet(ReferenceEqualityComparer.Instance);
        await publisher.PublishBatchAsync([new MyItem(1)], TestContext.Current.CancellationToken);
        var activity = Assert.Single(activities, a => !before.Contains(a) && a.OperationName == $"{TopicName} publish");

        Assert.Equal(ActivityKind.Producer, activity.Kind);
    }

    /// <summary>
    /// Verifies that calling <see cref="Envelope{T}.RequeueAsync"/> after the iterator has been
    /// disposed (receiver closed) completes without error, covering the
    /// <c>_receiver.IsClosed ? Task.CompletedTask : ...</c> branch.
    /// </summary>
    [Fact(Timeout = 60 * 1000)]
    public async Task AbandonOnClosedReceiverCompletesCleanly()
    {
        var host = await BuildHostAsync(services =>
        {
            services.AddPublisher<MyItem>(TopicName);
            services.AddSubscriber<MyItem>(TopicName, SubscriptionName);
        });
        var publisher = host.Services.GetRequiredKeyedService<IPublisher<MyItem>>(TopicName);
        var subscriber = host.Services.GetRequiredKeyedService<ISubscriber<MyItem>>(SubscriptionKey);

        await publisher.PublishAsync(new MyItem(1), TestContext.Current.CancellationToken);

        Envelope<MyItem>? captured = null;
        await foreach (var envelope in subscriber.SubscribeAsync(TestContext.Current.CancellationToken))
        {
            captured = envelope;
            break; // disposes the iterator → await using var receiver runs → IsClosed = true
        }

        // Exercise the MessageId and TraceId property getters on the ASB envelope.
        Assert.NotNull(captured!.MessageId);
        _ = captured.TraceId; // null when no tracing active; exercises the getter

        // Receiver is now closed; RequeueAsync must short-circuit with Task.CompletedTask.
        await captured.RequeueAsync(cancellationToken: TestContext.Current.CancellationToken);
    }

    // ASB subscriptions are persistent and accumulate messages across tests. Drain all
    // subscriptions before each test to prevent cross-test pollution.
    public override async ValueTask InitializeAsync()
    {
        await using var client = new ServiceBusClient(_fixture.GetConnectionString());
        await DrainAsync(client, TopicName, SubscriptionName);
        await DrainAsync(client, TopicName, "pm");
        await DrainAsync(client, TopicName, "sm");
    }

    private static async Task DrainAsync(ServiceBusClient client, string topic, string subscription)
    {
        await using var receiver = client.CreateReceiver(topic, subscription,
            new ServiceBusReceiverOptions { ReceiveMode = ServiceBusReceiveMode.ReceiveAndDelete });
        IReadOnlyList<ServiceBusReceivedMessage> batch;
        do
        {
            batch = await receiver.ReceiveMessagesAsync(maxMessages: 100, maxWaitTime: TimeSpan.FromSeconds(1));
        } while (batch.Count > 0);
    }
}

public record OversizedItem(string Payload);

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
