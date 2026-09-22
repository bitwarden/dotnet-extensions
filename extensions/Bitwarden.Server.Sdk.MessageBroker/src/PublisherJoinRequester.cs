using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Builds the outbound <see cref="INegotiationTransport"/> the publisher requester uses to
/// send its <see cref="PublisherJoin"/>. Registered in DI so tests can substitute an
/// in-memory transport and drive <see cref="PublisherJoinRequester"/> end-to-end without a
/// real broker.
/// </summary>
internal delegate INegotiationTransport PublisherNegotiationSenderFactory(string[] topics);

/// <summary>
/// Hosted service that runs at host startup and, for each data-topic this service publishes
/// (as recorded by <see cref="PublisherRoleMarker"/>), sends a <see cref="PublisherJoin"/>
/// requesting admission to the publisher fleet and awaits the ack. Admission is decided on
/// the receive side by <see cref="NegotiationListener"/> — whichever instance currently
/// holds the SAC role for the topic. This class is the request side of that handshake and
/// never inspects <c>INegotiationState</c> directly: only the SAC holder may read it, and
/// the send side has no lock.
/// <para>
/// Any failure — no-go reply, ack timeout, transport error — throws. Host startup fails,
/// the process exits non-zero. This is the deploy-time gate the design doc calls for.
/// </para>
/// <para>
/// Runs AFTER <see cref="NegotiationListenerCoordinator"/> in registration order so the local
/// listener is up before we send. Same-process (single-instance deployment) and cross-process
/// (multi-instance) both flow through the broker's SAC-selected active consumer.
/// </para>
/// <para>
/// The channel backend skips negotiation entirely (one assembly version, no skew), and a
/// service with no <see cref="PublisherRoleMarker"/>s (subscriber-only) has nothing to request
/// on the publisher side.
/// </para>
/// </summary>
internal sealed class PublisherJoinRequester : IHostedService
{
    private readonly IEnumerable<PublisherRoleMarker> _markers;
    private readonly IOptions<MessagingOptions> _messagingOptions;
    private readonly IOptions<NegotiationOptions> _negotiationOptions;
    private readonly PublisherNegotiationSenderFactory _senderFactory;

    public PublisherJoinRequester(
        IEnumerable<PublisherRoleMarker> markers,
        IOptions<MessagingOptions> messagingOptions,
        IOptions<NegotiationOptions> negotiationOptions,
        PublisherNegotiationSenderFactory senderFactory)
    {
        _markers = markers;
        _messagingOptions = messagingOptions;
        _negotiationOptions = negotiationOptions;
        _senderFactory = senderFactory;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var messaging = _messagingOptions.Value;
        var markers = _markers.ToList();
        if (markers.Count == 0
            || (string.IsNullOrEmpty(messaging.AzureServiceBusConnectionString)
                && string.IsNullOrEmpty(messaging.RabbitUri)))
        {
            return;
        }

        var negotiation = _negotiationOptions.Value;
        var topics = markers.Select(m => m.TopicName).Distinct().ToArray();

        // One transport handles sending across all topics — send-side is data-topic-agnostic
        // (the outgoing message's DataTopic carries the routing), and the per-InstanceId reply
        // loop can only exist once per process anyway. For ASB the factory supplies a bound
        // data-topic for construction; the receive path is untouched here since we never call
        // ReceiveRequestsAsync.
        var sender = _senderFactory(topics);
        try
        {
            foreach (var marker in markers)
                await RequestJoinAsync(marker, sender, negotiation, cancellationToken);
        }
        finally
        {
            await ((IAsyncDisposable)sender).DisposeAsync();
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static async Task RequestJoinAsync(
        PublisherRoleMarker marker,
        INegotiationTransport sender,
        NegotiationOptions negotiation,
        CancellationToken cancellationToken)
    {
        var join = new PublisherJoin
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
            ack = await sender.SendJoinAsync(join, linkedCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // AdmissionTimeout fired (the listener didn't reply in time), not the outer
            // host-shutdown token.
            throw new InvalidOperationException(
                $"Publisher join request for data-topic '{marker.TopicName}' timed out after " +
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
                $"Publisher join for data-topic '{marker.TopicName}' rejected by fleet " +
                $"(offending subscribers: {offenders}). Aborting startup.");
        }
    }
}
