using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// DI marker registered once per <c>AddPublisher</c> call. Enumerating these is the source of
/// truth for "topics this service publishes" — the listener coordinator, the Rabbit transport's
/// per-topic queue bindings, and the ASB per-topic rule reconciliation all read from this set.
/// </summary>
internal sealed record PublisherRoleMarker(string TopicName);

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
/// </summary>
internal sealed class NegotiationListenerCoordinator : IHostedService
{
    private readonly IEnumerable<PublisherRoleMarker> _markers;
    private readonly IOptions<MessagingOptions> _messagingOptions;
    private readonly IOptions<NegotiationOptions> _negotiationOptions;
    private readonly IServiceProvider _services;
    private CancellationTokenSource? _listenerStopSignal;
    private List<(Task RunTask, IAsyncDisposable Transport)>? _running;

    public NegotiationListenerCoordinator(
        IEnumerable<PublisherRoleMarker> markers,
        IOptions<MessagingOptions> messagingOptions,
        IOptions<NegotiationOptions> negotiationOptions,
        IServiceProvider services)
    {
        _markers = markers;
        _messagingOptions = messagingOptions;
        _negotiationOptions = negotiationOptions;
        _services = services;
    }

    internal int ListenerCount => _running?.Count ?? 0;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var pairs = BuildPairs();
        _listenerStopSignal = new CancellationTokenSource();
        _running = [.. pairs.Select(p => (
            RunTask: p.Listener.RunAsync(_listenerStopSignal.Token),
            p.Transport))];
        return Task.CompletedTask;
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

    private List<(NegotiationListener Listener, IAsyncDisposable Transport)> BuildPairs()
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

        // Lazy state resolution: only distributed backends need it, so the in-memory channel
        // branch above avoids pulling in FusionCache when the caller hasn't configured one.
        var state = _services.GetRequiredService<INegotiationState>();

        if (!string.IsNullOrEmpty(messaging.AzureServiceBusConnectionString))
        {
            // ASB works per topic
            return [.. topics.Select(topic =>
            {
                var transport = new AzureServiceBusNegotiationTransport(_messagingOptions, _negotiationOptions, topic);
                return (new NegotiationListener(transport, state), (IAsyncDisposable)transport);
            })];
        }

        // Rabbit one transport across all topics and one listener to drive it.
        var rabbitConnection = _services.GetRequiredService<RabbitConnection>();
        var rabbitTransport = new RabbitNegotiationTransport(rabbitConnection, _negotiationOptions, topics);
        return [(new NegotiationListener(rabbitTransport, state), rabbitTransport)];
    }
}
