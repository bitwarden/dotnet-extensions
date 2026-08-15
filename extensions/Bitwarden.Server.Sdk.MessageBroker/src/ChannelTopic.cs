using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>An in-memory fan-out topic that delivers each message to every registered subscriber channel.</summary>
internal sealed class ChannelTopic<T> : IHostedService
{
    private readonly ConcurrentDictionary<string, Channel<Envelope<T>>> _subscribers = new();

    /// <param name="subscriptionNames">
    /// Names pre-registered via <see cref="MessageBrokerServiceCollectionExtensions.AddSubscriber{T}"/>
    /// in the same DI container. Channels are created eagerly so messages published before a subscriber
    /// is resolved are buffered rather than dropped.
    /// </param>
    /// <param name="metrics">Registers a queue-depth observation for this topic.</param>
    /// <param name="topicName">The topic name, used as the <c>messaging.destination.name</c> tag.</param>
    public ChannelTopic(IEnumerable<string> subscriptionNames, MessageBrokerMetrics metrics, string topicName)
    {
        foreach (var name in subscriptionNames)
            _subscribers.TryAdd(name, Channel.CreateUnbounded<Envelope<T>>());

        metrics.RegisterQueueDepthProvider(topicName, () => _subscribers.Values.Sum(c => (long)c.Reader.Count));
    }

    public Channel<Envelope<T>> GetOrAddSubscription(string subscriptionName)
        => _subscribers.GetOrAdd(subscriptionName, _ => Channel.CreateUnbounded<Envelope<T>>());

    public async ValueTask WriteAsync(Func<ChannelWriter<Envelope<T>>, Envelope<T>> factory, CancellationToken cancellationToken = default)
    {
        foreach (var (_, channel) in _subscribers)
            await channel.Writer.WriteAsync(factory(channel.Writer), cancellationToken);
    }

    // IHostedService — seals all channel writers so subscribers exit cleanly on shutdown.
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken)
    {
        foreach (var (_, channel) in _subscribers)
            channel.Writer.TryComplete();
        return Task.CompletedTask;
    }
}
