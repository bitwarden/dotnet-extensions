using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// DI marker registered once per <c>AddSubscriber</c> call. Enumerating these is the source of
/// truth for the topics this service subscribes to.
/// </summary>
internal sealed record SubscriberRoleMarker(string TopicName, IReadOnlySet<string> WireNames);

/// <summary>
/// Hosted service that runs at host startup and, for each data-topic this service subscribes
/// to (as recorded by <see cref="SubscriberRoleMarker"/>), sends a <see cref="Capability"/>
/// requesting admission from the topic's publisher fleet and awaits the ack.
/// <para>
/// Any failure — no-go reply, ack timeout, transport error — throws. Host startup fails,
/// the process exits non-zero.
/// </para>
/// <para>
/// The channel backend skips negotiation entirely (one assembly version, no skew), and a
/// service with no <see cref="SubscriberRoleMarker"/>s (publisher-only) has nothing to
/// request on the subscriber side.
/// </para>
/// </summary>
internal sealed class SubscriberJoinRequester : IHostedService
{
    private readonly IEnumerable<SubscriberRoleMarker> _markers;
    private readonly IOptions<MessagingOptions> _messagingOptions;
    private readonly IOptions<NegotiationOptions> _negotiationOptions;
    private readonly IServiceProvider _services;

    public SubscriberJoinRequester(
        IEnumerable<SubscriberRoleMarker> markers,
        IOptions<MessagingOptions> messagingOptions,
        IOptions<NegotiationOptions> negotiationOptions,
        IServiceProvider services)
    {
        _markers = markers;
        _messagingOptions = messagingOptions;
        _negotiationOptions = negotiationOptions;
        _services = services;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var messaging = _messagingOptions.Value;
        // A single AddSubscriber<TPayload>(topic, subscriptionName) call registers one marker;
        // multiple subscription-groups on the same topic share one Capability entry (identical
        // wire-names, same InstanceId), so dedupe by topic before sending.
        var markers = _markers
            .GroupBy(m => m.TopicName)
            .Select(g => g.First())
            .ToList();
        if (markers.Count == 0
            || (string.IsNullOrEmpty(messaging.AzureServiceBusConnectionString)
                && string.IsNullOrEmpty(messaging.RabbitUri)))
        {
            return;
        }

        var negotiation = _negotiationOptions.Value;
        var topics = markers.Select(m => m.TopicName).ToArray();

        // One transport instance handles sending across all topics — send-side is
        // data-topic-agnostic (the outgoing message's DataTopic carries the routing), and the
        // per-InstanceId reply loop can only exist once per process anyway.
        var sender = BuildSender(messaging, topics);
        try
        {
            foreach (var marker in markers)
                await RequestCapabilityAsync(marker, sender, negotiation, cancellationToken);
        }
        finally
        {
            await ((IAsyncDisposable)sender).DisposeAsync();
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    // Testable seam: the per-topic request dance, decoupled from transport construction.
    // Tests drive this with an in-memory transport rather than instantiating the whole
    // IHostedService (which would need broker connection strings to construct a real transport).
    internal static async Task RequestCapabilityAsync(
        SubscriberRoleMarker marker,
        INegotiationTransport sender,
        NegotiationOptions negotiation,
        CancellationToken cancellationToken)
    {
        var capability = new Capability
        {
            DataTopic = marker.TopicName,
            InstanceId = negotiation.InstanceId,
            WireNames = [.. marker.WireNames],
        };

        // Wrap the send in AdmissionTimeout so the contract holds regardless of whether the
        // underlying transport also enforces one (Rabbit/ASB do; the in-memory test transport
        // does not).
        using var timeoutCts = new CancellationTokenSource(negotiation.AdmissionTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        NegotiationAck ack;
        try
        {
            ack = await sender.SendCapabilityAsync(capability, linkedCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // AdmissionTimeout fired (no publisher listener replied in time), not the outer
            // host-shutdown token.
            throw new InvalidOperationException(
                $"Subscriber capability request for data-topic '{marker.TopicName}' timed out after " +
                $"{negotiation.AdmissionTimeout} without a reply. Aborting startup.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not InvalidOperationException)
        {
            // Broker unreachable or other transport-level failure. Wrap so the caller sees a
            // consistent typed exception matching what publish would throw for the same topic.
            throw new BrokerUnavailableException(marker.TopicName, ex);
        }

        if (!ack.Go)
        {
            var offenders = string.Join(", ", ack.Offenders.Select(o => o.InstanceId));
            throw new InvalidOperationException(
                $"Subscriber capability for data-topic '{marker.TopicName}' rejected by fleet " +
                $"(offending publishers: {offenders}). Aborting startup.");
        }
    }

    private INegotiationTransport BuildSender(MessagingOptions messaging, string[] topics)
    {
        // Note these transports uses only send, receive needs cache lock protection this
        // class does not enforce
        if (!string.IsNullOrEmpty(messaging.AzureServiceBusConnectionString))
        {
            return new AzureServiceBusNegotiationTransport(_messagingOptions, _negotiationOptions, topics[0]);
        }

        var rabbitConnection = _services.GetRequiredService<RabbitConnection>();
        return new RabbitNegotiationTransport(rabbitConnection, _negotiationOptions, []);
    }
}
