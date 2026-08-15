using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Bitwarden.Server.Sdk.MessageBroker;

/// <summary>
/// Base class for hosted message consumers. Owns the <see cref="BackgroundService.ExecuteAsync"/>
/// loop and settles each envelope automatically — <see cref="Envelope{T}.CompleteAsync"/> on
/// success, <see cref="Envelope{T}.AbandonAsync"/> when <see cref="HandleAsync"/> throws.
/// </summary>
/// <remarks>
/// Register with
/// <see cref="MessageBrokerServiceCollectionExtensions.AddMessageConsumer{T,TConsumer}"/>, which
/// wires up the <see cref="ISubscriber{T}"/> and registers the consumer as an
/// <see cref="IHostedService"/> in one call.
/// </remarks>
/// <typeparam name="T">The message type.</typeparam>
public abstract class MessageConsumer<T> : BackgroundService
{
    private readonly ISubscriber<T> _subscriber;

    /// <param name="subscriber">
    /// The subscriber to consume from. When using
    /// <see cref="MessageBrokerServiceCollectionExtensions.AddMessageConsumer{T,TConsumer}"/>,
    /// this is resolved from the correct keyed service automatically.
    /// </param>
    protected MessageConsumer(ISubscriber<T> subscriber)
    {
        _subscriber = subscriber;
    }

    /// <summary>Processes a single delivered message.</summary>
    /// <remarks>
    /// Do not call <see cref="Envelope{T}.CompleteAsync"/> or
    /// <see cref="Envelope{T}.AbandonAsync"/> inside this method — the base class settles the
    /// envelope after this method returns or throws.
    /// </remarks>
    protected abstract Task HandleAsync(Envelope<T> envelope, CancellationToken cancellationToken);

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var envelope in _subscriber.SubscribeAsync(stoppingToken))
        {
            try
            {
                await HandleAsync(envelope, stoppingToken);
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
