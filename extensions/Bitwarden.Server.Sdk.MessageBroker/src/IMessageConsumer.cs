namespace Bitwarden.Server.Sdk.MessageBroker;

/// <summary>
/// Processes messages of type <typeparamref name="T"/> delivered by a message broker.
/// </summary>
/// <remarks>
/// Register with
/// <see cref="Microsoft.Extensions.DependencyInjection.MessageBrokerServiceCollectionExtensions.AddMessageConsumer{T,TConsumer}"/>,
/// which wires up the <see cref="ISubscriber{T}"/> and hosts the consumer's processing loop.
/// Do not call <see cref="Envelope{T}.CompleteAsync"/> or <see cref="Envelope{T}.AbandonAsync"/>
/// inside <see cref="HandleAsync"/> — the framework settles the envelope automatically. To
/// permanently discard a message without redelivery, call
/// <see cref="Envelope{T}.DeadLetterAsync"/> and then return normally.
/// </remarks>
/// <typeparam name="T">The message type.</typeparam>
public interface IMessageConsumer<T>
{
    /// <summary>Processes a single delivered message.</summary>
    Task HandleAsync(Envelope<T> envelope, CancellationToken cancellationToken);
}
