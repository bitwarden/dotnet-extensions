using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Builds the outbound <see cref="INegotiationSender"/> the publisher requester uses to
/// send its <see cref="PublisherJoin"/>. Registered in DI so tests can substitute an
/// in-memory transport and drive <see cref="PublisherJoinRequester"/> end-to-end without a
/// real broker.
/// </summary>
internal delegate INegotiationSender PublisherNegotiationSenderFactory(string[] topics);

/// <summary>
/// Hosted service that runs the publisher side of version negotiation:
/// <list type="bullet">
/// <item><description>At startup, sends a <see cref="PublisherJoin"/> per
/// <see cref="PublisherRoleMarker"/> and awaits the ack. Any failure — no-go, timeout, transport
/// error — throws and fails host startup.</description></item>
/// <item><description>While running, republishes each join every
/// <see cref="NegotiationOptions.HeartbeatInterval"/> so the fleet cache entry stays alive.
/// Heartbeat failures log a warning; the cache entry's TTL is the correctness backstop.</description></item>
/// <item><description>On graceful shutdown, fires a <see cref="PublisherLeave"/> per admitted
/// marker so the cache entry drops immediately, then disposes the sender.</description></item>
/// </list>
/// <para>
/// Implements <see cref="IHostedLifecycleService"/> so admission runs in the
/// <c>StartingAsync</c> phase, which the host completes for every service before invoking any
/// <c>StartAsync</c>. That guarantee holds regardless of caller registration order: any
/// downstream <see cref="BackgroundService"/> (e.g., a consumer loop or a user-registered
/// hosted service) begins its <c>ExecuteAsync</c> only after publish admission is settled.
/// Admission decisions are made by whichever instance holds the broker-native
/// single-active-consumer role for the topic; this class never touches
/// <see cref="INegotiationState"/>.
/// </para>
/// <para>
/// The channel backend skips negotiation entirely (one assembly version, no skew), and a
/// service with no <see cref="PublisherRoleMarker"/>s (subscriber-only) has nothing to do
/// here.
/// </para>
/// </summary>
internal sealed class PublisherJoinRequester : BackgroundService, IHostedLifecycleService
{
    private readonly IEnumerable<PublisherRoleMarker> _markers;
    private readonly IOptions<MessagingOptions> _messagingOptions;
    private readonly IOptions<NegotiationOptions> _negotiationOptions;
    private readonly PublisherNegotiationSenderFactory _senderFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<PublisherJoinRequester> _logger;

    // Populated in StartingAsync when the distributed-backend + markers guard passes;
    // ExecuteAsync and StopAsync rely on the same non-null check to know whether initial
    // admission ran.
    private INegotiationSender? _sender;
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

    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        var messaging = _messagingOptions.Value;
        var markers = _markers.ToList();
        if (markers.Count == 0
            || (string.IsNullOrEmpty(messaging.AzureServiceBusConnectionString)
                && string.IsNullOrEmpty(messaging.RabbitUri)))
        {
            // Nothing to do; leave _sender null so ExecuteAsync short-circuits.
            return;
        }

        var negotiation = _negotiationOptions.Value;
        var topics = markers.Select(m => m.TopicName).Distinct().ToArray();

        // One sender handles all topics — the outgoing message's DataTopic carries the routing,
        // and the reply loop is per-transport.
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
            await sender.DisposeAsync();
            throw;
        }

        _sender = sender;
        _admittedMarkers = markers;
    }

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

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
        INegotiationSender sender,
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

        await sender.DisposeAsync();
    }

    private async Task SendLeaveAsync(
        PublisherRoleMarker marker,
        INegotiationSender sender,
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
        INegotiationSender sender,
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
