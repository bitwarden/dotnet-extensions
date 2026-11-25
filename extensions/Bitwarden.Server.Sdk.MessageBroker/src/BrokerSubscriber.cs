using System.Runtime.CompilerServices;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Receives payload family <typeparamref name="TPayload"/> from a topic subscription on RabbitMQ or
/// Azure Service Bus, decoding each message from the broker's <see cref="IMessageReceiver"/>.
/// </summary>
internal sealed class BrokerSubscriber<TPayload, TCeiling> : ISubscriber<TPayload, TCeiling>, ISignalsSubscribed<TPayload, TCeiling>
    where TPayload : PayloadCeiling<TPayload, TCeiling>, IPayloadVariants<TPayload>, IPayloadWireName<TPayload>
    where TCeiling : Payload<TPayload>.ICeiling
{
    private readonly IMessageReceiver _receiver;
    private readonly string _topicName;
    private readonly IMessageSerializer _serializer;
    private readonly MessageBrokerMetrics _metrics;

    public BrokerSubscriber(IMessageReceiver receiver, string topicName, IMessageSerializer serializer, MessageBrokerMetrics metrics)
    {
        _receiver = receiver;
        _topicName = topicName;
        _serializer = serializer;
        _metrics = metrics;
    }

    public IAsyncEnumerable<Envelope<TPayload, TCeiling>> SubscribeAsync(CancellationToken cancellationToken) =>
        SubscribeAsync(static () => { }, cancellationToken);

    public async IAsyncEnumerable<Envelope<TPayload, TCeiling>> SubscribeAsync(
        Action onSubscribed, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var message in _receiver.ReceiveAsync(onSubscribed, cancellationToken))
        {
            // The broker's binding or filter should deliver only this family. If it is wrong, say so
            // here rather than failing to decode another family's message.
            if (message.FamilyWireName is not null && message.FamilyWireName != TPayload.WireName)
            {
                await ReceivedMessageDecoder.DeadLetterAsync(message,
                    $"This subscription on topic '{_topicName}' receives family '{TPayload.WireName}', but the message " +
                    $"is family '{message.FamilyWireName}'. Check the subscription's binding or filter.");
                continue;
            }

            var envelope = await ReceivedMessageDecoder.DecodeOrDeadLetterAsync<TPayload, TCeiling>(message, _serializer, _topicName, _metrics);
            if (envelope is not null)
            {
                yield return envelope;
            }
        }
    }
}
