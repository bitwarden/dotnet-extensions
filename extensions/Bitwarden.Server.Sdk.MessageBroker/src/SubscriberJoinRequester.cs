using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// DI marker registered once per <c>AddSubscriber</c> call. Enumerating these is the source of
/// truth for the topics this service subscribes to.
/// <para>
/// <see cref="ProceedOnAdmissionTimeout"/> lets a caller opt this registration into graceful
/// degradation: if no publisher listener replies within <see cref="NegotiationOptions.AdmissionTimeout"/>,
/// the requester logs an error and lets startup continue instead of failing the host. Suits
/// subscribers that can operate without a live publisher — for example, an eventual-consistency
/// consumer that will catch up when publishers come back. When multiple registrations exist for
/// the same topic, the strictest flag wins: the topic only softens if every registration opts in.
/// Soft-failing a hard-requiring subscriber is undefined behavior; hard-failing a soft-declared
/// one is merely annoying.
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
/// Hosted service that runs at host startup and, for each data-topic this service subscribes
/// to (as recorded by <see cref="SubscriberRoleMarker"/>), sends a <see cref="Capability"/>
/// requesting admission from the topic's publisher fleet and awaits the ack.
/// <para>
/// Any failure — no-go reply, ack timeout, transport error — throws. Host startup fails,
/// the process exits non-zero. A marker with
/// <see cref="SubscriberRoleMarker.ProceedOnAdmissionTimeout"/> set instead logs the timeout
/// and lets startup continue.
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
    private readonly SubscriberNegotiationSenderFactory _senderFactory;
    private readonly ILogger<SubscriberJoinRequester> _logger;

    public SubscriberJoinRequester(
        IEnumerable<SubscriberRoleMarker> markers,
        IOptions<MessagingOptions> messagingOptions,
        IOptions<NegotiationOptions> negotiationOptions,
        SubscriberNegotiationSenderFactory senderFactory,
        ILogger<SubscriberJoinRequester> logger)
    {
        _markers = markers;
        _messagingOptions = messagingOptions;
        _negotiationOptions = negotiationOptions;
        _senderFactory = senderFactory;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
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
            return;
        }

        var negotiation = _negotiationOptions.Value;
        var topics = markers.Select(m => m.TopicName).ToArray();

        // One transport handles sending across all topics — send-side is data-topic-agnostic
        // (the outgoing message's DataTopic carries the routing), and the per-InstanceId reply
        // loop can only exist once per process anyway.
        var sender = _senderFactory(topics);
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

    private async Task RequestCapabilityAsync(
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
            if (marker.ProceedOnAdmissionTimeout)
            {
                _logger.LogError(
                    "Subscriber capability request for data-topic '{DataTopic}' timed out after {AdmissionTimeout} " +
                    "without a reply. Proceeding anyway (ProceedOnAdmissionTimeout was set); this subscriber " +
                    "will start without confirmed publisher compatibility.",
                    marker.TopicName, negotiation.AdmissionTimeout);
                return;
            }
            throw new InvalidOperationException(
                $"Subscriber capability request for data-topic '{marker.TopicName}' timed out after " +
                $"{negotiation.AdmissionTimeout} without a reply. Aborting startup.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not InvalidOperationException)
        {
            // Broker unreachable or other transport-level failure. Wrap so the caller sees a
            // consistent typed exception matching what publish would throw for the same topic.
            // Broker-level outages are distinct from "publisher didn't reply" and are not
            // covered by ProceedOnAdmissionTimeout — the whole negotiation plane is down.
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
}
