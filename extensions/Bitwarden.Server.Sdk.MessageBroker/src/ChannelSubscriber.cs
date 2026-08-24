using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace Microsoft.Extensions.DependencyInjection;

internal sealed class ChannelSubscriber<T> : ISubscriber<T>
{
    private readonly ChannelReader<Envelope<T>> _reader;
    private readonly string _topicName;
    private readonly MessageBrokerMetrics _metrics;

    public ChannelSubscriber(ChannelReader<Envelope<T>> reader, string topicName, MessageBrokerMetrics metrics)
    {
        _reader = reader;
        _topicName = topicName;
        _metrics = metrics;
    }

    public async IAsyncEnumerable<Envelope<T>> SubscribeAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var item in _reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            _metrics.RecordConsume(_topicName);
            yield return item;
        }
    }
}
