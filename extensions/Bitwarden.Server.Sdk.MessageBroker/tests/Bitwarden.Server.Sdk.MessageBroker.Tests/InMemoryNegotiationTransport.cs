using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace Bitwarden.Server.Sdk.MessageBroker.Tests;

/// <summary>
/// Shared in-process routing for <see cref="InMemoryNegotiationTransport"/>. One broker per
/// test scenario; multiple transports registered against the same broker exchange messages
/// via per-data-topic in-memory queues. Ack-based messages (capability, join) route replies
/// back through a captured <see cref="TaskCompletionSource{TResult}"/>; fire-and-forget
/// messages (publisher-leave, subscriber-leave) enqueue without a reply channel.
/// </summary>
internal sealed class InMemoryNegotiationBroker
{
    private readonly ConcurrentDictionary<string, Channel<Pending>> _byTopic = new();

    internal ChannelReader<Pending> Subscribe(string dataTopic) => QueueFor(dataTopic).Reader;

    internal Task<NegotiationAck> DispatchAsync(
        string dataTopic,
        Capability? capability,
        PublisherJoin? join,
        CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<NegotiationAck>(TaskCreationOptions.RunContinuationsAsynchronously);
        QueueFor(dataTopic).Writer.TryWrite(new Pending(capability, join, PublisherLeave: null, SubscriberLeave: null, tcs));
        return tcs.Task.WaitAsync(cancellationToken);
    }

    internal Task DispatchLeaveAsync(
        string dataTopic,
        PublisherLeave? publisherLeave,
        SubscriberLeave? subscriberLeave)
    {
        QueueFor(dataTopic).Writer.TryWrite(new Pending(Capability: null, Join: null, publisherLeave, subscriberLeave, Reply: null));
        return Task.CompletedTask;
    }

    private Channel<Pending> QueueFor(string dataTopic)
        => _byTopic.GetOrAdd(dataTopic, _ => Channel.CreateUnbounded<Pending>());

    internal sealed record Pending(
        Capability? Capability,
        PublisherJoin? Join,
        PublisherLeave? PublisherLeave,
        SubscriberLeave? SubscriberLeave,
        TaskCompletionSource<NegotiationAck>? Reply);
}

/// <summary>
/// Fully in-process <see cref="INegotiationTransport"/> for tests. Bound to one data-topic
/// at construction (matching the Azure Service Bus per-topic shape), so higher-level tests
/// spawn one transport per topic served. Sends route via <see cref="InMemoryNegotiationBroker"/>
/// based on the message's <c>DataTopic</c>; receives drain the queue for this transport's own
/// data-topic.
/// </summary>
internal sealed class InMemoryNegotiationTransport : INegotiationTransport, IAsyncDisposable
{
    private readonly InMemoryNegotiationBroker _broker;
    private readonly string _dataTopic;

    public InMemoryNegotiationTransport(InMemoryNegotiationBroker broker, string dataTopic)
    {
        _broker = broker;
        _dataTopic = dataTopic;
    }

    // No owned resources — the broker owns the queues and outlives the transport.
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public Task<NegotiationAck> SendCapabilityAsync(Capability capability, CancellationToken cancellationToken = default)
        => _broker.DispatchAsync(capability.DataTopic, capability, join: null, cancellationToken);

    public Task<NegotiationAck> SendJoinAsync(PublisherJoin join, CancellationToken cancellationToken = default)
        => _broker.DispatchAsync(join.DataTopic, capability: null, join, cancellationToken);

    public Task SendPublisherLeaveAsync(PublisherLeave leave, CancellationToken cancellationToken = default)
        => _broker.DispatchLeaveAsync(leave.DataTopic, leave, subscriberLeave: null);

    public Task SendSubscriberLeaveAsync(SubscriberLeave leave, CancellationToken cancellationToken = default)
        => _broker.DispatchLeaveAsync(leave.DataTopic, publisherLeave: null, leave);

    public async IAsyncEnumerable<INegotiationInbound> ReceiveRequestsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var pending in _broker.Subscribe(_dataTopic).ReadAllAsync(cancellationToken))
        {
            yield return pending switch
            {
                { Capability: not null } => new CapabilityRequest
                {
                    Capability = pending.Capability,
                    Replier = MakeReplier(pending.Reply!),
                },
                { Join: not null } => new JoinRequest
                {
                    Join = pending.Join,
                    Replier = MakeReplier(pending.Reply!),
                },
                { PublisherLeave: not null } => new PublisherLeaveNotification { Leave = pending.PublisherLeave },
                { SubscriberLeave: not null } => new SubscriberLeaveNotification { Leave = pending.SubscriberLeave },
                _ => throw new InvalidOperationException("Pending broker item had no payload."),
            };
        }
    }

    private static Func<NegotiationAck, CancellationToken, Task> MakeReplier(TaskCompletionSource<NegotiationAck> tcs)
        => (ack, _) => { tcs.TrySetResult(ack); return Task.CompletedTask; };
}
