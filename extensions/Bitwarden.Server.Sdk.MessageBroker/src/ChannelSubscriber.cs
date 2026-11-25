using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Receives one payload family from an in-memory subscription channel.
/// </summary>
internal sealed class ChannelSubscriber<TPayload, TCeiling> : ISubscriber<TPayload, TCeiling>, ISignalsSubscribed<TPayload, TCeiling>
    where TPayload : PayloadCeiling<TPayload, TCeiling>, IPayloadVariants<TPayload>, IPayloadWireName<TPayload>
    where TCeiling : Payload<TPayload>.ICeiling
{
    private readonly ChannelReader<Envelope<TPayload, TCeiling>> _reader;
    private readonly string _topicName;
    private readonly MessageBrokerMetrics _metrics;

    public ChannelSubscriber(ChannelReader<Envelope<TPayload, TCeiling>> reader, string topicName, MessageBrokerMetrics metrics)
    {
        _reader = reader;
        _topicName = topicName;
        _metrics = metrics;
    }

    public IAsyncEnumerable<Envelope<TPayload, TCeiling>> SubscribeAsync(CancellationToken cancellationToken) =>
        SubscribeAsync(static () => { }, cancellationToken);

    public async IAsyncEnumerable<Envelope<TPayload, TCeiling>> SubscribeAsync(
        Action onSubscribed, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // In process, the subscription is there as soon as it is read.
        onSubscribed();
        await foreach (var item in _reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            _metrics.RecordConsume(_topicName, TPayload.WireName, item.ConsumedVariantWireName);
            yield return item;
        }
    }
}
