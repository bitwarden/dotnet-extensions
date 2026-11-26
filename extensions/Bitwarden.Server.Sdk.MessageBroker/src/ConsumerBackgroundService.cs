using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

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
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ISubscriber<TPayload, TCeiling> _subscriber;
    private readonly ILogger _logger;
    private readonly string _subscriptionKey;

    /// <param name="scopeFactory">The factory for dependency-injection scopes.</param>
    /// <param name="subscriber">The subscriber to consume messages from.</param>
    /// <param name="logger">The logger for the consumer.</param>
    /// <param name="subscriptionKey">The <c>{topic}/{subscription}</c> being consumed.</param>
    public ConsumerBackgroundService(
        IServiceScopeFactory scopeFactory, ISubscriber<TPayload, TCeiling> subscriber, ILogger logger, string subscriptionKey)
    {
        _scopeFactory = scopeFactory;
        _subscriber = subscriber;
        _logger = logger;
        _subscriptionKey = subscriptionKey;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        ResubscribeLoop.RunAsync(ReceiveAsync, _logger, _subscriptionKey, TimeProvider.System, stoppingToken);

    private async Task ReceiveAsync(Action onMessage, CancellationToken stoppingToken)
    {
        await foreach (var envelope in _subscriber.SubscribeAsync(stoppingToken))
        {
            onMessage();
            await ConsumerInvocation.RunAsync<TPayload, TCeiling, TConsumer>(_scopeFactory, envelope, stoppingToken);
        }
    }
}
