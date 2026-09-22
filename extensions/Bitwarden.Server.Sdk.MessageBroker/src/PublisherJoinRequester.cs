using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
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
/// never inspects <c>INegotiationState</c> for admission decisions: only the SAC holder may
/// read it, and the send side has no lock.
/// <para>
/// Any failure — no-go reply, ack timeout, transport error — throws. Host startup fails,
/// the process exits non-zero. This is the deploy-time gate the design doc calls for.
/// </para>
/// <para>
/// After a successful startup admission the service keeps its sender alive and republishes
/// each <see cref="PublisherJoin"/> every <see cref="NegotiationOptions.HeartbeatInterval"/>
/// so the fleet cache does not expire this instance's entry. Heartbeat failures log a warning
/// and continue; the cache entry's TTL is the correctness backstop.
/// </para>
/// <para>
/// On graceful shutdown the service fires a <see cref="PublisherLeave"/> per admitted marker so
/// the SAC-holding listener removes this instance's cache entry immediately, then disposes the
/// sender. Failures are swallowed and logged.
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
internal sealed class PublisherJoinRequester : BackgroundService
{
    private readonly IEnumerable<PublisherRoleMarker> _markers;
    private readonly IOptions<MessagingOptions> _messagingOptions;
    private readonly IOptions<NegotiationOptions> _negotiationOptions;
    private readonly PublisherNegotiationSenderFactory _senderFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<PublisherJoinRequester> _logger;

    // Populated in StartAsync when the distributed-backend + markers guard passes; ExecuteAsync
    // and StopAsync rely on the same non-null check to know whether initial admission ran.
    private INegotiationTransport? _sender;
    private List<PublisherRoleMarker> _admittedMarkers = [];

    public PublisherJoinRequester(
        IEnumerable<PublisherRoleMarker> markers,
        IOptions<MessagingOptions> messagingOptions,
        IOptions<NegotiationOptions> negotiationOptions,
        PublisherNegotiationSenderFactory senderFactory,
        TimeProvider timeProvider,
        ILogger<PublisherJoinRequester> logger)
    {
        _markers = markers;
        _messagingOptions = messagingOptions;
        _negotiationOptions = negotiationOptions;
        _senderFactory = senderFactory;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        var messaging = _messagingOptions.Value;
        var markers = _markers.ToList();
        if (markers.Count == 0
            || (string.IsNullOrEmpty(messaging.AzureServiceBusConnectionString)
                && string.IsNullOrEmpty(messaging.RabbitUri)))
        {
            // Nothing to do. Skip base.StartAsync so ExecuteAsync never runs.
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
        catch
        {
            // Startup admission failed; the host will not come up, so drop the sender rather
            // than leaving it live for a heartbeat loop that will never run.
            await ((IAsyncDisposable)sender).DisposeAsync();
            throw;
        }

        _sender = sender;
        _admittedMarkers = markers;
        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Capture the sender into a local so the heartbeat tasks hold a stable reference for
        // this ExecuteAsync's lifetime. base.StopAsync usually awaits us to completion before
        // StopAsync nulls _sender and disposes, but under a host-shutdown-token cancellation it
        // returns via Task.WhenAny early, so _sender can be nulled while our tasks are still
        // running. The per-marker catch-all swallows the resulting ObjectDisposedException.
        var sender = _sender;
        if (sender is null || _admittedMarkers.Count == 0) return;

        var negotiation = _negotiationOptions.Value;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(negotiation.HeartbeatInterval, _timeProvider, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            var heartbeats = _admittedMarkers.Select(marker =>
                SendHeartbeatAsync(marker, sender, negotiation, stoppingToken));
            await Task.WhenAll(heartbeats);
        }
    }

    private async Task SendHeartbeatAsync(
        PublisherRoleMarker marker,
        INegotiationTransport sender,
        NegotiationOptions negotiation,
        CancellationToken stoppingToken)
    {
        try
        {
            await RequestJoinAsync(marker, sender, negotiation, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Loop returns on next iteration's cancellation check.
        }
        catch (Exception ex)
        {
            // Heartbeat is best-effort; the cache entry's TTL is the correctness backstop.
            // Swallowing here keeps the loop alive across transient blips (e.g., broker restart)
            // so the next tick has a chance to recover the entry.
            _logger.LogWarning(
                ex,
                "Publisher heartbeat for data-topic '{DataTopic}' failed. " +
                "The cache entry will expire at TTL if this persists.",
                marker.TopicName);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        // Cancels the stopping token and waits for ExecuteAsync to unwind.
        await base.StopAsync(cancellationToken);

        var sender = Interlocked.Exchange(ref _sender, null);
        if (sender is null) return;

        // Race the leaves in parallel so shutdown latency does not scale with marker count.
        var instanceId = _negotiationOptions.Value.InstanceId;
        var leaves = _admittedMarkers.Select(marker =>
            SendLeaveAsync(marker, sender, instanceId, cancellationToken));
        await Task.WhenAll(leaves);

        await ((IAsyncDisposable)sender).DisposeAsync();
    }

    private async Task SendLeaveAsync(
        PublisherRoleMarker marker,
        INegotiationTransport sender,
        string instanceId,
        CancellationToken cancellationToken)
    {
        try
        {
            await sender.SendPublisherLeaveAsync(
                new PublisherLeave { DataTopic = marker.TopicName, InstanceId = instanceId },
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Best-effort publisher leave for data-topic '{DataTopic}' failed on shutdown; " +
                "the cache entry will expire at TTL.",
                marker.TopicName);
        }
    }

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
            throw new NegotiationTimeoutException(marker.TopicName, negotiation.AdmissionTimeout);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not NegotiationTimeoutException)
        {
            // Broker unreachable or other transport-level failure. Wrap so the caller sees a
            // consistent typed exception matching what publish would throw for the same topic.
            throw new BrokerUnavailableException(marker.TopicName, ex);
        }

        if (!ack.Go)
        {
            throw new NegotiationRejectedException(
                marker.TopicName,
                [.. ack.Offenders.Select(o => o.InstanceId)]);
        }
    }
}
