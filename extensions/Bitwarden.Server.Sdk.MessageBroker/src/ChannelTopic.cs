using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>An in-memory fan-out topic that delivers each message to every registered subscriber channel.</summary>
internal sealed class ChannelTopic<TPayload, TCeiling> : IHostedService
    where TPayload : PayloadCeiling<TPayload, TCeiling>, IPayloadVariants<TPayload>
    where TCeiling : Payload<TPayload>.ICeiling
{
    private readonly string _topicName;
    private readonly ConcurrentDictionary<string, Subscription> _subscribers = new();

    /// <param name="subscriptionNames">
    /// Names pre-registered via <see cref="MessageBrokerServiceCollectionExtensions.AddSubscriber{TPayload,TCeiling}"/>
    /// in the same DI container. Channels are created eagerly so messages published before a subscriber
    /// is resolved are buffered rather than dropped.
    /// </param>
    /// <param name="escrowRegistrations">
    /// Per-subscription escrow registrations. Each registers startup recovery and shutdown drain
    /// callbacks so <see cref="IHostedService"/> startup/stop handles escrow without a separate
    /// hosted service.
    /// </param>
    /// <param name="metrics">Registers a queue-depth observation for this topic.</param>
    /// <param name="topicName">The topic name, used as the <c>messaging.destination.name</c> tag.</param>
    public ChannelTopic(IEnumerable<string> subscriptionNames, IEnumerable<ChannelEscrowRegistration<TPayload, TCeiling>> escrowRegistrations, MessageBrokerMetrics metrics, string topicName)
    {
        _topicName = topicName;

        foreach (var name in subscriptionNames)
            _subscribers.TryAdd(name, new Subscription());

        foreach (var reg in escrowRegistrations)
            if (reg.TopicName == topicName)
                reg.RegisterWith(this);

        metrics.RegisterQueueDepthProvider(topicName, () => _subscribers.Values.Sum(s => (long)s.Channel.Reader.Count));
    }

    public Channel<Envelope<TPayload, TCeiling>> GetOrAddSubscription(string subscriptionName)
        => _subscribers.GetOrAdd(subscriptionName, _ => new Subscription()).Channel;

    /// <inheritdoc cref="ChannelEscrowRegistration{TPayload, TCeiling}.RegisterWith"/>
    public void SetEscrowFallback(string subscriptionName, Func<Envelope<TPayload, TCeiling>, CancellationToken, Task> fallback)
        => _subscribers.GetOrAdd(subscriptionName, _ => new Subscription()).EscrowFallback = fallback;

    /// <inheritdoc cref="ChannelEscrowRegistration{TPayload, TCeiling}.RegisterWith"/>
    public void SetStartupRecovery(string subscriptionName, Func<ChannelWriter<Envelope<TPayload, TCeiling>>, CancellationToken, Task> recovery)
        => _subscribers.GetOrAdd(subscriptionName, _ => new Subscription()).StartupRecovery = recovery;

    /// <inheritdoc cref="ChannelEscrowRegistration{TPayload, TCeiling}.RegisterWith"/>
    public void SetShutdownDrain(string subscriptionName, Func<IReadOnlyList<Envelope<TPayload, TCeiling>>, CancellationToken, Task> drain)
        => _subscribers.GetOrAdd(subscriptionName, _ => new Subscription()).ShutdownDrain = drain;

    public async ValueTask WriteAsync(Func<ChannelWriter<Envelope<TPayload, TCeiling>>, Func<Envelope<TPayload, TCeiling>, CancellationToken, Task>?, Envelope<TPayload, TCeiling>> factory, CancellationToken cancellationToken = default)
    {
        foreach (var (_, sub) in _subscribers)
            await sub.Channel.Writer.WriteAsync(factory(sub.Channel.Writer, sub.EscrowFallback), cancellationToken);
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var (_, sub) in _subscribers)
            if (sub.StartupRecovery is not null)
                await sub.StartupRecovery(sub.Channel.Writer, cancellationToken);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        foreach (var (_, sub) in _subscribers)
        {
            if (sub.ShutdownDrain is not null)
            {
                var remaining = new List<Envelope<TPayload, TCeiling>>();
                while (sub.Channel.Reader.TryRead(out var envelope))
                    remaining.Add(envelope);
                if (remaining.Count > 0)
                    await sub.ShutdownDrain(remaining, cancellationToken);
            }

            sub.Channel.Writer.TryComplete();
        }
    }

    private sealed class Subscription
    {
        public Channel<Envelope<TPayload, TCeiling>> Channel { get; } = System.Threading.Channels.Channel.CreateUnbounded<Envelope<TPayload, TCeiling>>();
        public Func<Envelope<TPayload, TCeiling>, CancellationToken, Task>? EscrowFallback;
        public Func<ChannelWriter<Envelope<TPayload, TCeiling>>, CancellationToken, Task>? StartupRecovery;
        public Func<IReadOnlyList<Envelope<TPayload, TCeiling>>, CancellationToken, Task>? ShutdownDrain;
    }
}
