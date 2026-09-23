using Azure.Messaging.ServiceBus.Administration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using ZiggyCreatures.Caching.Fusion;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// DI marker registered once per <c>AddPublisher</c> call. Enumerating these is the source of
/// truth for the topics this service publishes.
/// </summary>
internal sealed record PublisherRoleMarker(string TopicName, IReadOnlySet<string> WireNames);

/// <summary>
/// Hosted service that spins up <see cref="NegotiationListener"/> instances for the negotiation
/// receive side at host startup. Wiring shape differs per backend:
/// <list type="bullet">
/// <item><description>ASB — one <see cref="AzureServiceBusNegotiationTransport"/> + one
/// <see cref="NegotiationListener"/> per <see cref="PublisherRoleMarker"/> (topic).</description></item>
/// <item><description>RabbitMQ — one <see cref="RabbitNegotiationTransport"/> (multi-topic, queue-scoped SAC)
/// + one <see cref="NegotiationListener"/> for the whole service.</description></item>
/// <item><description>In-process channel — negotiation skipped entirely (one assembly version, no skew).</description></item>
/// </list>
/// The coordinator owns listener and transport life cycles.
/// <para>
/// Implements <see cref="IHostedLifecycleService"/> so listener startup runs in the
/// <c>StartingAsync</c> phase, which the host guarantees completes before any hosted
/// <c>StartAsync</c>. This is what enforces publish-after-admission and subscribe-after-admission
/// regardless of the order the caller registered publishers, subscribers, or their own
/// <c>IHostedService</c>s.
/// </para>
/// </summary>
internal sealed class NegotiationListenerCoordinator : IHostedLifecycleService
{
    // Well-known cache key for the negotiation state. Callers must have registered an
    // IFusionCache under this key (AddBitwardenCaching's AnyKey registration satisfies this).
    internal const string NegotiationCacheKey = "bwsn::negotiation";

    private readonly IEnumerable<PublisherRoleMarker> _markers;
    private readonly IOptions<MessagingOptions> _messagingOptions;
    private readonly IOptions<NegotiationOptions> _negotiationOptions;
    private readonly IServiceProvider _services;
    private readonly NegotiationMetrics _metrics;
    private CancellationTokenSource? _listenerStopSignal;
    private List<(Task RunTask, INegotiationTransport Transport)>? _running;

    public NegotiationListenerCoordinator(
        IEnumerable<PublisherRoleMarker> markers,
        IOptions<MessagingOptions> messagingOptions,
        IOptions<NegotiationOptions> negotiationOptions,
        IServiceProvider services,
        NegotiationMetrics metrics)
    {
        _markers = markers;
        _messagingOptions = messagingOptions;
        _negotiationOptions = negotiationOptions;
        _services = services;
        _metrics = metrics;
    }

    internal int ListenerCount => _running?.Count ?? 0;

    // Listener startup runs in StartingAsync so it completes before any hosted service's
    // StartAsync — including any BackgroundService whose ExecuteAsync would begin iterating a
    // subscription or publish loop. The join requesters also run their admission in
    // StartingAsync, and within-phase ordering is by registration; the listener is registered
    // first inside AddPublisher, so it lands before the requesters that need it up.
    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        await ReconcileAsbRulesIfNeededAsync(cancellationToken);
        _metrics.RegisterPublisherTopics(_markers.Select(m => m.TopicName).Distinct());
        var pairs = BuildPairs();
        _listenerStopSignal = new CancellationTokenSource();
        _running = [.. pairs.Select(p => (
            RunTask: p.Listener.RunAsync(_listenerStopSignal.Token),
            p.Transport))];
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task ReconcileAsbRulesIfNeededAsync(CancellationToken cancellationToken)
    {
        var topics = _markers.Select(m => m.TopicName).Distinct().ToArray();
        var messaging = _messagingOptions.Value;
        if (topics.Length == 0 || string.IsNullOrEmpty(messaging.AzureServiceBusConnectionString))
            return;

        // The ASB emulator does not expose the management REST API that
        // ServiceBusAdministrationClient talks to; skip reconciliation when the caller has
        // opted into the emulator via the SDK's own connection-string flag.
        if (messaging.AzureServiceBusConnectionString.Contains("UseDevelopmentEmulator=true", StringComparison.OrdinalIgnoreCase))
            return;

        var admin = new ServiceBusAdministrationClient(messaging.AzureServiceBusConnectionString);
        await AzureServiceBusRuleReconciler.ReconcileAsync(
            admin,
            _negotiationOptions.Value.ControlTopicName,
            _negotiationOptions.Value.RequestSubscriptionName,
            topics,
            cancellationToken);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        // StopAsync can be called more than once (host graceful-shutdown paths, test teardown);
        // null both fields on first call so subsequent calls are no-ops.
        var stopSignal = Interlocked.Exchange(ref _listenerStopSignal, null);
        var running = Interlocked.Exchange(ref _running, null);
        if (stopSignal is null || running is null) return;

        // Signal every listener to exit, wait for their run loops to unwind, then dispose the
        // transports (closing underlying broker connections/channels) with nothing consuming.
        await stopSignal.CancelAsync();
        foreach (var (task, _) in running)
        {
            try { await task; }
            catch (OperationCanceledException) { /* expected */ }
        }
        foreach (var (_, transport) in running)
            await transport.DisposeAsync();
        stopSignal.Dispose();
    }

    private List<(NegotiationListener Listener, INegotiationTransport Transport)> BuildPairs()
    {
        var topics = _markers.Select(m => m.TopicName).Distinct().ToArray();
        var messaging = _messagingOptions.Value;

        // In-memory channel: negotiation skipped. Also short-circuit when there are no
        // published topics (subscriber-only service) — nothing on this side of the fleet to admit.
        if (topics.Length == 0
            || (string.IsNullOrEmpty(messaging.AzureServiceBusConnectionString)
                && string.IsNullOrEmpty(messaging.RabbitUri)))
        {
            return [];
        }

        // State is constructed here, not injected. The only legitimate consumer is the
        // SAC-holding listener, and there is no scenario in which any other class should read
        // or write it.
        var inner = new FusionCacheNegotiationState(
            _services.GetRequiredKeyedService<IFusionCache>(NegotiationCacheKey),
            _negotiationOptions);

        if (!string.IsNullOrEmpty(messaging.AzureServiceBusConnectionString))
        {
            // ASB works per topic. Each listener holds SAC for its single data-topic
            // so the state handed to it is scoped to the topic
            return [.. topics.Select(topic =>
            {
                var transport = AzureServiceBusNegotiationTransport.ForListener(_messagingOptions, _negotiationOptions, topic);
                var scoped = new TopicScopedNegotiationState(inner, topic);
                return (new NegotiationListener(transport, scoped, _metrics), transport);
            })];
        }

        // Rabbit: one transport and one listener across all topics.
        var rabbitConnection = _services.GetRequiredService<RabbitConnection>();
        var rabbitTransport = RabbitNegotiationTransport.ForListener(rabbitConnection, _negotiationOptions, topics);
        return [(new NegotiationListener(rabbitTransport, inner, _metrics), rabbitTransport)];
    }
}
