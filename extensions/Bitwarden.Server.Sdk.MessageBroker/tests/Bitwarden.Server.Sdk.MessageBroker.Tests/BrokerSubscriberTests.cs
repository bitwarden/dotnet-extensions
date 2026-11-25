using System.Diagnostics.Metrics;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Options;

namespace Bitwarden.Server.Sdk.MessageBroker.Tests;

/// <summary>
/// What a topic subscription on RabbitMQ or Azure Service Bus does with each message its broker
/// receiver hands it, without a broker.
/// </summary>
public sealed class BrokerSubscriberTests : IDisposable
{
    private const string Topic = "mail-events";

    private readonly ServiceProvider _services;

    public BrokerSubscriberTests()
    {
        _services = new ServiceCollection()
            .AddMetrics()
            .AddOptions()
            .BuildServiceProvider();
    }

    public void Dispose() => _services.Dispose();

    private sealed class Settlement
    {
        public bool Completed { get; set; }
        public bool Requeued { get; set; }
        public bool DeadLettered { get; set; }
        public string? DeadLetterReason { get; set; }
    }

    private sealed class FakeReceiver(params ReceivedMessage[] messages) : IMessageReceiver
    {
        public async IAsyncEnumerable<ReceivedMessage> ReceiveAsync(
            Action onSubscribed, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            onSubscribed();
            foreach (var message in messages)
            {
                await Task.Yield();
                yield return message;
            }
        }
    }

    private static ReceivedMessage Message(string body, Settlement settlement, string? family = "send.mail") => new()
    {
        Body = Encoding.UTF8.GetBytes(body),
        FamilyWireName = family,
        MessageId = Guid.NewGuid().ToString(),
        TraceParent = null,
        DeliveryCount = 1,
        CompleteAsync = _ => { settlement.Completed = true; return Task.CompletedTask; },
        RequeueAsync = _ => { settlement.Requeued = true; return Task.CompletedTask; },
        DeadLetterAsync = (reason, _) => { settlement.DeadLettered = true; settlement.DeadLetterReason = reason; return Task.CompletedTask; },
    };

    private BrokerSubscriber<TPayload, TCeiling> Subscriber<TPayload, TCeiling>(params ReceivedMessage[] messages)
        where TPayload : PayloadCeiling<TPayload, TCeiling>, IPayloadVariants<TPayload>, IPayloadWireName<TPayload>
        where TCeiling : Payload<TPayload>.ICeiling =>
        new(
            new FakeReceiver(messages),
            Topic,
            new SystemTextJsonMessageSerializer(
                _services.GetRequiredService<IOptionsMonitor<MessageBrokerSerializerOptions>>(), Topic),
            new MessageBrokerMetrics(_services.GetRequiredService<IMeterFactory>()));

    private static async Task<List<Envelope<TPayload, TCeiling>>> ReceiveAllAsync<TPayload, TCeiling>(
        BrokerSubscriber<TPayload, TCeiling> subscriber)
        where TPayload : PayloadCeiling<TPayload, TCeiling>, IPayloadVariants<TPayload>, IPayloadWireName<TPayload>
        where TCeiling : Payload<TPayload>.ICeiling
    {
        var envelopes = new List<Envelope<TPayload, TCeiling>>();
        await foreach (var envelope in subscriber.SubscribeAsync(TestContext.Current.CancellationToken))
        {
            envelopes.Add(envelope);
        }
        return envelopes;
    }

    public static TheoryData<string, string> UndecodableMessages()
    {
        var data = new TheoryData<string, string>();
        data.Add(new TheoryDataRow<string, string>(
            "not json at all",
            "The message's body could not be deserialized.")
        { TestDisplayName = "A body that isn't JSON" });
        data.Add(new TheoryDataRow<string, string>(
            """{"$type":"v1","Id":1}""",
            "The message's body could not be deserialized.")
        { TestDisplayName = "A single variant instead of an array of variants" });
        data.Add(new TheoryDataRow<string, string>(
            """[{"$type":"v9","Id":1}]""",
            "No version of the message on the wire is known to this receiver.")
        { TestDisplayName = "Only a version this subscriber doesn't know" });
        return data;
    }

    [Theory]
    [MemberData(nameof(UndecodableMessages))]
    public async Task AMessageThatCannotBeDecoded_IsDeadLetteredWithItsReason(string body, string reason)
    {
        var settlement = new Settlement();

        var envelopes = await ReceiveAllAsync(Subscriber<SendMailPayload, SendMail>(Message(body, settlement)));

        Assert.Empty(envelopes);
        Assert.True(settlement.DeadLettered);
        Assert.False(settlement.Completed || settlement.Requeued);
        Assert.Equal(reason, settlement.DeadLetterReason);
    }

    [Fact]
    public async Task AMessageWhoseVersionCannotReachTheCeiling_IsDeadLetteredWithItsReason()
    {
        var settlement = new Settlement();

        // RenameV1.Upcast() throws on a null name, so no version reaches the RenameV2 ceiling.
        var envelopes = await ReceiveAllAsync(Subscriber<RenamePayload, RenameV2>(
            Message("""[{"$type":"v1","Name":null}]""", settlement, family: RenamePayload.WireName)));

        Assert.Empty(envelopes);
        Assert.True(settlement.DeadLettered);
        Assert.Equal("The message's versions cannot be resolved to RenameV2.", settlement.DeadLetterReason);
    }

    [Fact]
    public async Task AnotherFamilysMessage_IsDeadLetteredWithAReasonNamingBothFamilies()
    {
        var settlement = new Settlement();

        // A valid send.mail body, but labelled send.push, as a wrong binding or filter would deliver it.
        var envelopes = await ReceiveAllAsync(Subscriber<SendMailPayload, SendMail>(
            Message("""[{"$type":"v1","Id":1}]""", settlement, family: SendPushPayload.WireName)));

        Assert.Empty(envelopes);
        Assert.True(settlement.DeadLettered);
        Assert.Equal(
            "This subscription on topic 'mail-events' receives family 'send.mail', but the message is family " +
            "'send.push'. Check the subscription's binding or filter.",
            settlement.DeadLetterReason);
    }

    [Fact]
    public async Task AMessageWithNoFamily_IsStillDecoded()
    {
        var settlement = new Settlement();

        var envelopes = await ReceiveAllAsync(Subscriber<SendMailPayload, SendMail>(
            Message("""[{"$type":"v1","Id":3}]""", settlement, family: null)));

        Assert.Equal(3, Assert.Single(envelopes).Payload.Id);
        Assert.False(settlement.DeadLettered);
    }

    [Fact]
    public async Task AMessageAfterOneSetAside_IsDecodedCountedAndSettledThroughTheMessage()
    {
        using var collector = new MetricCollector<long>(
            _services.GetRequiredService<IMeterFactory>(), MessageBrokerMetrics.MeterName, "messaging.client.consumed.messages");
        var (bad, good) = (new Settlement(), new Settlement());

        var envelopes = await ReceiveAllAsync(Subscriber<SendMailPayload, SendMail>(
            Message("not json at all", bad),
            Message("""[{"$type":"v1","Id":7}]""", good)));

        var envelope = Assert.Single(envelopes);
        Assert.Equal(7, envelope.Payload.Id);
        var measurement = Assert.Single(collector.GetMeasurementSnapshot());
        Assert.Equal(Topic, measurement.Tags[MessageBrokerMetrics.DestinationTag]);
        Assert.Equal(SendMailPayload.WireName, measurement.Tags[MessageBrokerMetrics.PayloadFamilyTag]);

        await envelope.CompleteAsync(TestContext.Current.CancellationToken);

        Assert.True(good.Completed);
        Assert.False(good.DeadLettered || good.Requeued);
    }

    [Fact]
    public async Task TheReceiversSubscribedSignal_ReachesTheConsumerEvenWithNoMessages()
    {
        var subscribed = false;

        await foreach (var _ in Subscriber<SendMailPayload, SendMail>().SubscribeAsync(
            () => subscribed = true, TestContext.Current.CancellationToken))
        {
        }

        Assert.True(subscribed);
    }
}
