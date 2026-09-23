using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// DI marker registered once per <c>AddSubscriber</c> call. Enumerating these is the source of
/// truth for the topics this service subscribes to.
/// <para>
/// <see cref="ProceedOnAdmissionTimeout"/> opts this registration into graceful degradation: on
/// ack timeout, log an error and let startup continue instead of failing the host. Suits
/// subscribers that can operate without a live publisher — an eventual-consistency consumer
/// that will catch up when publishers come back, for example. When multiple registrations exist
/// for the same topic, the strictest flag wins — a topic softens only if every registration
/// opts in, since softening a hard-requiring subscriber violates its invariant.
/// </para>
/// </summary>
internal sealed record SubscriberRoleMarker(
    string TopicName,
    IReadOnlySet<string> WireNames,
    bool ProceedOnAdmissionTimeout = false);

/// <summary>
/// Builds the outbound <see cref="INegotiationTransport"/> the subscriber requester uses to
/// send its <see cref="Capability"/>. Registered in DI so tests can substitute an in-memory
/// transport and drive <see cref="SubscriberJoinRequester"/> end-to-end without a real broker.
/// </summary>
internal delegate INegotiationTransport SubscriberNegotiationSenderFactory(string[] topics);

/// <summary>
/// Hosted service that runs the subscriber side of version negotiation:
/// <list type="bullet">
/// <item><description>At startup, sends a <see cref="Capability"/> per
/// <see cref="SubscriberRoleMarker"/> and awaits the ack. No-go and transport errors throw and
/// fail host startup; ack timeout throws unless
/// <see cref="SubscriberRoleMarker.ProceedOnAdmissionTimeout"/> is set, in which case it logs
/// and lets startup continue.</description></item>
/// <item><description>While running, republishes each capability every
/// <see cref="NegotiationOptions.HeartbeatInterval"/> so the fleet cache entry stays alive.
/// Heartbeat failures log a warning; the cache entry's TTL is the correctness backstop.</description></item>
/// <item><description>On graceful shutdown, fires a <see cref="SubscriberLeave"/> per admitted
/// marker so the cache entry drops immediately, then disposes the sender.</description></item>
/// </list>
/// <para>
/// Implements <see cref="IHostedLifecycleService"/> so admission runs in the
/// <c>StartingAsync</c> phase, which the host completes for every service before invoking any
/// <c>StartAsync</c>. That guarantee holds regardless of caller registration order: any
/// downstream <see cref="BackgroundService"/> — including
/// <see cref="ConsumerBackgroundService{TPayload, TCeiling, TConsumer}"/>, ASP.NET's Kestrel
/// host, or a user-registered <c>IHostedService</c> that iterates a subscription — begins its
/// <c>ExecuteAsync</c> only after subscription admission is settled.
/// </para>
/// <para>
/// The channel backend skips negotiation entirely (one assembly version, no skew), and a
/// service with no <see cref="SubscriberRoleMarker"/>s (publisher-only) has nothing to do here.
/// </para>
/// </summary>
internal sealed class SubscriberJoinRequester : BackgroundService, IHostedLifecycleService
{
    private readonly IEnumerable<SubscriberRoleMarker> _markers;
    private readonly IOptions<MessagingOptions> _messagingOptions;
    private readonly IOptions<NegotiationOptions> _negotiationOptions;
    private readonly SubscriberNegotiationSenderFactory _senderFactory;
    private readonly TimeProvider _timeProvider;
    private readonly NegotiationMetrics _metrics;
    private readonly ILogger<SubscriberJoinRequester> _logger;

    // Populated in StartingAsync when the distributed-backend + markers guard passes;
    // ExecuteAsync and StopAsync rely on the same non-null check to know whether initial
    // admission ran.
    private INegotiationTransport? _sender;
    private List<SubscriberRoleMarker> _admittedMarkers = [];

    public SubscriberJoinRequester(
        IEnumerable<SubscriberRoleMarker> markers,
        IOptions<MessagingOptions> messagingOptions,
        IOptions<NegotiationOptions> negotiationOptions,
        SubscriberNegotiationSenderFactory senderFactory,
        TimeProvider timeProvider,
        NegotiationMetrics metrics,
        ILogger<SubscriberJoinRequester> logger)
    {
        _markers = markers;
        _messagingOptions = messagingOptions;
        _negotiationOptions = negotiationOptions;
        _senderFactory = senderFactory;
        _timeProvider = timeProvider;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        var messaging = _messagingOptions.Value;
        // A single AddSubscriber<TPayload>(topic, subscriptionName) call registers one marker;
        // multiple subscription-groups on the same topic share one Capability entry (identical
        // wire-names, same InstanceId), so dedupe by topic before sending. When registrations
        // for the same topic disagree on ProceedOnAdmissionTimeout, the strictest wins — the
        // topic only softens if every registration opts in. Soft-failing a hard-requiring
        // subscriber is undefined behavior; hard-failing a soft-declared one is merely annoying.
        var markers = _markers
            .GroupBy(m => m.TopicName)
            .Select(g => g.First() with { ProceedOnAdmissionTimeout = g.All(m => m.ProceedOnAdmissionTimeout) })
            .ToList();
        if (markers.Count == 0
            || (string.IsNullOrEmpty(messaging.AzureServiceBusConnectionString)
                && string.IsNullOrEmpty(messaging.RabbitUri)))
        {
            // Nothing to do; leave _sender null so ExecuteAsync short-circuits.
            return;
        }

        var negotiation = _negotiationOptions.Value;
        var topics = markers.Select(m => m.TopicName).ToArray();

        // Register the topics this instance subscribes to with the metrics gauge before
        // sending admission requests. Even a marker whose admission ultimately fails represents
        // a deployment intent worth surfacing until the process exits.
        _metrics.RegisterSubscriberTopics(topics);

        // One transport handles sending across all topics — send-side is data-topic-agnostic
        // (the outgoing message's DataTopic carries the routing), and the per-InstanceId reply
        // loop can only exist once per process anyway.
        var sender = _senderFactory(topics);
        try
        {
            foreach (var marker in markers)
                await RequestCapabilityAsync(marker, sender, negotiation, cancellationToken);
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
        SubscriberRoleMarker marker,
        INegotiationTransport sender,
        NegotiationOptions negotiation,
        CancellationToken stoppingToken)
    {
        try
        {
            await SendCapabilityAsync(marker, sender, negotiation, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Loop returns on next iteration's cancellation check.
        }
        catch (Exception ex)
        {
            // Heartbeat is best-effort; the cache entry's TTL is the correctness backstop.
            // Swallowing here keeps the loop alive across transient blips (e.g., broker restart)
            // so the next tick has a chance to recover the entry. The ProceedOnAdmissionTimeout
            // flag is only about the startup gate; here every failure mode is soft regardless.
            _logger.LogWarning(
                ex,
                "Subscriber heartbeat for data-topic '{DataTopic}' failed. " +
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
        SubscriberRoleMarker marker,
        INegotiationTransport sender,
        string instanceId,
        CancellationToken cancellationToken)
    {
        try
        {
            await sender.SendSubscriberLeaveAsync(
                new SubscriberLeave { DataTopic = marker.TopicName, InstanceId = instanceId },
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Best-effort subscriber leave for data-topic '{DataTopic}' failed on shutdown; " +
                "the cache entry will expire at TTL.",
                marker.TopicName);
        }
    }

    private async Task RequestCapabilityAsync(
        SubscriberRoleMarker marker,
        INegotiationTransport sender,
        NegotiationOptions negotiation,
        CancellationToken cancellationToken)
    {
        try
        {
            await SendCapabilityAsync(marker, sender, negotiation, cancellationToken);
        }
        catch (NegotiationTimeoutException) when (marker.ProceedOnAdmissionTimeout)
        {
            _logger.LogError(
                "Subscriber capability request for data-topic '{DataTopic}' timed out after {AdmissionTimeout} " +
                "without a reply. Proceeding anyway (ProceedOnAdmissionTimeout was set); this subscriber " +
                "will start without confirmed publisher compatibility.",
                marker.TopicName, negotiation.AdmissionTimeout);
        }
    }

    private static async Task SendCapabilityAsync(
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
            // host-shutdown token. RequestCapabilityAsync softens this into a logged error when
            // the marker opts in; otherwise it propagates and fails startup.
            throw new NegotiationTimeoutException(marker.TopicName, negotiation.AdmissionTimeout);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not NegotiationTimeoutException)
        {
            // Broker unreachable or other transport-level failure. Wrap so the caller sees a
            // consistent typed exception matching what publish would throw for the same topic.
            // Broker-level outages are distinct from "publisher didn't reply" and are not
            // covered by ProceedOnAdmissionTimeout — the whole negotiation plane is down.
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
