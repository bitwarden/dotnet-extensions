using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace Microsoft.Extensions.DependencyInjection;

internal sealed class ChannelSubscriber<TPayload, TCeiling> : ISubscriber<TPayload, TCeiling>
    where TPayload : PayloadCeiling<TPayload, TCeiling>, IPayloadVariants<TPayload>
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

    public async IAsyncEnumerable<Envelope<TPayload, TCeiling>> SubscribeAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var item in _reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            _metrics.RecordConsume(_topicName, item.ConsumedVariantWireName);
            yield return item;
        }
    }
}
