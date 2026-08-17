using Bitwarden.Server.Sdk.MessageBroker;
using Microsoft.Extensions.Hosting;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Hosts an <see cref="IMessageConsumer{T}"/> as a <see cref="BackgroundService"/>, driving its
/// message-processing loop and settling each envelope automatically.
/// </summary>
internal sealed class ConsumerBackgroundService<T, TConsumer> : BackgroundService
    where TConsumer : class, IMessageConsumer<T>
{
    private readonly TConsumer _consumer;
    private readonly ISubscriber<T> _subscriber;

    /// <param name="consumer">The consumer to invoke for each message.</param>
    /// <param name="sp">
    /// Used to resolve <see cref="ISubscriber{T}"/> keyed by <c>typeof(<typeparamref name="TConsumer"/>)</c>,
    /// which <see cref="MessageBrokerServiceCollectionExtensions.AddMessageConsumer{T,TConsumer}"/>
    /// registers as a forwarding alias so this type is constructable by DI without a keyed-service
    /// attribute, enabling <c>TryAddEnumerable</c> deduplication.
    /// </param>
    public ConsumerBackgroundService(TConsumer consumer, IServiceProvider sp)
    {
        _consumer = consumer;
        _subscriber = sp.GetRequiredKeyedService<ISubscriber<T>>(typeof(TConsumer));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var envelope in _subscriber.SubscribeAsync(stoppingToken))
        {
            try
            {
                await _consumer.HandleAsync(envelope, stoppingToken);
                await envelope.CompleteAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                try
                {
                    await envelope.AbandonAsync(ex.Message, CancellationToken.None);
                }
                catch (Exception)
                {
                    // Settlement failed (broker disconnected, lock expired, etc.). The broker
                    // will redeliver the message when the lock times out. Swallow so a transient
                    // settlement error doesn't kill the consumer permanently.
                }
            }
        }
    }
}
