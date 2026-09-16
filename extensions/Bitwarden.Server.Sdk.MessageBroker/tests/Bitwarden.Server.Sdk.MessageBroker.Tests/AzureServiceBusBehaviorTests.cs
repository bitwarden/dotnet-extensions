using Azure.Messaging.ServiceBus;
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

    // ASB topology is pre-declared in the emulator JSON config, so the multi-family topic name
    // must match the name declared there — a dynamic "{TopicName}-events" would not exist on
    // the broker. The base class default works for Channel and Rabbit, which provision topology
    // on demand.
    protected override string MultiFamilyTopicName => "user-events";

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
        var host = await BuildHostAsync(services => services.AddPublisher<OversizedPayload, OversizedVariant>(TopicName));
        var publisher = host.Services.GetRequiredKeyedService<Publisher<OversizedPayload, OversizedVariant>>(MessageBrokerKeys.Publisher<OversizedPayload>(TopicName));
        var oversizedPayload = new string('x', 300 * 1024); // ~300 KB > 256 KB limit
        await Assert.ThrowsAsync<ServiceBusException>(() =>
            publisher.Publish(new OversizedVariant(oversizedPayload)).SendAsync(TestContext.Current.CancellationToken));
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
            services.AddPublisher<MyItemPayload, MyItem>(TopicName);
            services.AddSubscriber<MyItemPayload, MyItem>(TopicName, SubscriptionName);
        });
        var publisher = host.Services.GetRequiredKeyedService<Publisher<MyItemPayload, MyItem>>(PublisherKey);
        var subscriber = host.Services.GetRequiredKeyedService<ISubscriber<MyItemPayload, MyItem>>(SubscriptionKey);

        await publisher.Publish(new MyItem(1)).SendAsync(TestContext.Current.CancellationToken);

        Envelope<MyItemPayload, MyItem>? captured = null;
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

    /// <summary>
    /// A misconfigured subscription — its Correlation filter selects <c>user.deleted</c>, but the
    /// client is registered as a <see cref="UserCreatedPayload"/> subscriber. The broker filter
    /// passes the mismatched message through; the subscriber's defensive x-bw-payload-family
    /// check must catch it, dead-letter the message, and never yield it to the consumer.
    /// </summary>
    [Fact(Timeout = 60 * 1000)]
    public async Task MismatchedFamilyIsDeadLetteredByDefensiveCheck()
    {
        const string MisconfiguredTopic = "misconfigured";
        const string MisfilteredSubscription = "misfiltered";

        var host = await BuildHostAsync(services =>
        {
            services.AddPublisher<UserDeletedPayload, UserDeleted>(MisconfiguredTopic);
            services.AddSubscriber<UserCreatedPayload, UserCreated>(MisconfiguredTopic, MisfilteredSubscription);
        });

        var deletedPublisher = host.Services.GetRequiredKeyedService<Publisher<UserDeletedPayload, UserDeleted>>(
            MessageBrokerKeys.Publisher<UserDeletedPayload>(MisconfiguredTopic));
        var misfiltered = host.Services.GetRequiredKeyedService<ISubscriber<UserCreatedPayload, UserCreated>>(
            $"{MisconfiguredTopic}/{MisfilteredSubscription}");

        await deletedPublisher.Publish(new UserDeleted(Guid.NewGuid(), "test")).SendAsync(TestContext.Current.CancellationToken);

        // Prove the subscriber never yields the mismatched message. The cancellation fires once
        // we've given the receiver enough time to see (and dead-letter) the message.
        using var receiveCts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        receiveCts.CancelAfter(TimeSpan.FromSeconds(5));
        var receivedAny = false;
        try
        {
            await foreach (var _ in misfiltered.SubscribeAsync(receiveCts.Token))
            {
                receivedAny = true;
                break;
            }
        }
        catch (OperationCanceledException) { /* expected: subscriber drained nothing yieldable */ }

        Assert.False(receivedAny, "Defensive check should have dead-lettered the mismatched family before yielding.");

        // The message must land in the subscription's dead-letter queue.
        await using var sbClient = new ServiceBusClient(_fixture.GetConnectionString());
        await using var dlqReceiver = sbClient.CreateReceiver(MisconfiguredTopic, MisfilteredSubscription,
            new ServiceBusReceiverOptions { SubQueue = SubQueue.DeadLetter });
        var deadLettered = await dlqReceiver.ReceiveMessageAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.NotNull(deadLettered);
        Assert.Equal(UserDeletedPayload.WireName, deadLettered.ApplicationProperties["x-bw-payload-family"]);
    }
}

public sealed record OversizedVariant(string Data) : OversizedPayload.ISole;

public class OversizedPayload : Payload<OversizedPayload, OversizedVariant, OversizedVariant>, IPayloadVariants<OversizedPayload>, IPayloadWireName<OversizedPayload>
{
    public static string WireName => "oversized";
    public static IReadOnlyList<(Type, string)> Variants => [(typeof(OversizedVariant), nameof(OversizedVariant))];
}
