using System.Collections.Concurrent;
using System.Diagnostics.Metrics;

namespace Microsoft.Extensions.DependencyInjection;

internal sealed class MessageBrokerMetrics
{
    internal const string MeterName = "Bitwarden.Server.Sdk.MessageBroker";
    internal const string DestinationTag = "messaging.destination.name";
    internal const string PayloadFamilyTag = "messaging.payload.family";
    internal const string VariantNameTag = "messaging.variant.name";

    private readonly Counter<long> _publishedMessages;
    private readonly Counter<long> _consumedMessages;
    private readonly ConcurrentDictionary<(string Topic, string Family), Func<long>> _queueDepthProviders = new();

    public MessageBrokerMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(MeterName);
        _publishedMessages = meter.CreateCounter<long>(
            "messaging.client.published.messages",
            unit: "{message}",
            description: "Number of messages published.");
        _consumedMessages = meter.CreateCounter<long>(
            "messaging.client.consumed.messages",
            unit: "{message}",
            description: "Number of messages received by the consumer.");
        meter.CreateObservableGauge<long>(
            "messaging.channel.queued.messages",
            observeValues: ObserveQueueDepths,
            unit: "{message}",
            description: "Number of messages currently buffered in channel topics.");
    }

    public void RecordPublish(string topicName, string familyWireName, long count = 1) =>
        _publishedMessages.Add(count,
            new KeyValuePair<string, object?>(DestinationTag, topicName),
            new KeyValuePair<string, object?>(PayloadFamilyTag, familyWireName));

    public void RecordConsume(string topicName, string familyWireName, string variantWireName) =>
        _consumedMessages.Add(1,
            new KeyValuePair<string, object?>(DestinationTag, topicName),
            new KeyValuePair<string, object?>(PayloadFamilyTag, familyWireName),
            new KeyValuePair<string, object?>(VariantNameTag, variantWireName));

    public void RegisterQueueDepthProvider(string topicName, string familyWireName, Func<long> getCount)
        => _queueDepthProviders[(topicName, familyWireName)] = getCount;

    private IEnumerable<Measurement<long>> ObserveQueueDepths()
    {
        foreach (var ((topicName, familyWireName), getCount) in _queueDepthProviders)
            yield return new Measurement<long>(getCount(),
                new KeyValuePair<string, object?>(DestinationTag, topicName),
                new KeyValuePair<string, object?>(PayloadFamilyTag, familyWireName));
    }
}
