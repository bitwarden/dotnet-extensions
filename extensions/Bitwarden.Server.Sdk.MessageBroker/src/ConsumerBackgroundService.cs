using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Hosts an <see cref="IMessageConsumer{TPayload, TCeiling}"/> as a <see cref="BackgroundService"/>,
/// driving its message-processing loop and settling each envelope automatically. Resolves
/// <typeparamref name="TConsumer"/> from a fresh DI scope per message so handlers can inject
/// scoped dependencies (e.g. a <c>DbContext</c>) the same way a controller action would.
/// </summary>
internal sealed class ConsumerBackgroundService<TPayload, TCeiling, TConsumer> : BackgroundService
    where TPayload : PayloadCeiling<TPayload, TCeiling>, IPayloadVariants<TPayload>
    where TCeiling : Payload<TPayload>.ICeiling
    where TConsumer : class, IMessageConsumer<TPayload, TCeiling>
{
    private readonly TopologyGate _topology;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ISubscriber<TPayload, TCeiling> _subscriber;
    private readonly ILogger _logger;
    private readonly string _subscriptionKey;
    private readonly IOptionsMonitor<ConsumerRetryOptions> _retry;
    private readonly SubscriptionReadiness _readiness;

    /// <param name="scopeFactory">Used to create a per-message DI scope.</param>
    /// <param name="subscriber">The subscriber to consume messages from.</param>
    /// <param name="logger">Logs a failed subscription before it is opened again.</param>
    /// <param name="subscriptionKey">The <c>{topic}/{subscription}</c> being consumed, for the log.</param>
    /// <param name="readiness">Where the subscription reports whether it is receiving.</param>
    /// <param name="retry">Whether and how to retry a message the handler threw on, named by <paramref name="subscriptionKey"/>.</param>
    /// <param name="topology">Opens once the service has provisioned its topology, when it does.</param>
    public ConsumerBackgroundService(
        IServiceScopeFactory scopeFactory, ISubscriber<TPayload, TCeiling> subscriber, ILogger logger, string subscriptionKey,
        IOptionsMonitor<ConsumerRetryOptions> retry, SubscriptionReadiness readiness, TopologyGate topology)
    {
        _topology = topology;
        _readiness = readiness;
        // Said here, before the service runs, so the readiness check never reports healthy in the
        // moment before it starts.
        readiness.Expect(subscriptionKey);
        _retry = retry;
        _scopeFactory = scopeFactory;
        _subscriber = subscriber;
        _logger = logger;
        _subscriptionKey = subscriptionKey;
    }

    // A subscription that fails (a broker outage, or a broker down at startup) is opened again
    // rather than faulting the service, which with .NET's defaults would stop the host.
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        ResubscribeLoop.RunAsync(ReceiveAsync, _logger, _subscriptionKey, _readiness, _topology, TimeProvider.System, stoppingToken);

    private async Task ReceiveAsync(Action onSubscribed, Action onMessage, CancellationToken stoppingToken)
    {
        IAsyncEnumerable<Envelope<TPayload, TCeiling>> envelopes;
        if (_subscriber is ISignalsSubscribed<TPayload, TCeiling> signalling)
        {
            envelopes = signalling.SubscribeAsync(onSubscribed, stoppingToken);
        }
        else
        {
            // A subscriber that can't say when it subscribed counts as receiving once it starts.
            onSubscribed();
            envelopes = _subscriber.SubscribeAsync(stoppingToken);
        }
        await foreach (var envelope in envelopes)
        {
            onMessage();
            await ConsumerInvocation.RunAsync<TPayload, TCeiling, TConsumer>(_scopeFactory, envelope, _retry.Get(_subscriptionKey), stoppingToken);
        }
    }
}
