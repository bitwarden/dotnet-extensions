using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace Microsoft.Extensions.DependencyInjection;

internal sealed class ChannelSubscriber<TFamily, TCeiling> : ISubscriber<TFamily, TCeiling>
    where TFamily : FamilyCeiling<TFamily, TCeiling>, IFamilyVariants<TFamily>, IFamilyWireName<TFamily>
    where TCeiling : Family<TFamily>.ICeiling
{
    private readonly ChannelReader<Envelope<TFamily, TCeiling>> _reader;
    private readonly string _topicName;
    private readonly MessageBrokerMetrics _metrics;

    public ChannelSubscriber(ChannelReader<Envelope<TFamily, TCeiling>> reader, string topicName, MessageBrokerMetrics metrics)
    {
        _reader = reader;
        _topicName = topicName;
        _metrics = metrics;
    }

    public async IAsyncEnumerable<Envelope<TFamily, TCeiling>> SubscribeAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var item in _reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            _metrics.RecordConsume(_topicName, TFamily.WireName, item.ConsumedVariantWireName);
            yield return item;
        }
    }
}
