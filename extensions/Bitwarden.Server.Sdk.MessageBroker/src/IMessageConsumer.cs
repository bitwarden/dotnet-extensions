namespace Bitwarden.Server.Sdk.MessageBroker;

/// <summary>
/// Processes payloads of family <typeparamref name="TPayload"/> at the consumer's declared
/// ceiling <typeparamref name="TCeiling"/>. The framework hands in an
/// <see cref="Envelope{TPayload, TCeiling}"/> whose <see cref="Envelope{TPayload, TCeiling}.Payload"/>
/// is the already-resolved latest variant — an exact match if the wire carried it, otherwise the
/// result of walking pure upcasts from an older variant.
/// </summary>
/// <remarks>
/// Register with
/// <see cref="Microsoft.Extensions.DependencyInjection.MessageBrokerServiceCollectionExtensions.AddMessageConsumer{TPayload, TCeiling, TConsumer}"/>,
/// which wires up the <see cref="ISubscriber{TPayload, TCeiling}"/> and hosts the consumer's processing
/// loop. Do not call <see cref="Envelope{TPayload, TCeiling}.CompleteAsync"/> or
/// <see cref="Envelope{TPayload, TCeiling}.RequeueAsync"/> inside <see cref="HandleAsync"/> — the
/// framework settles the envelope automatically. To permanently discard a message without
/// redelivery, call <see cref="Envelope{TPayload, TCeiling}.DeadLetterAsync"/> and then return
/// normally.
/// </remarks>
/// <typeparam name="TPayload">The payload family.</typeparam>
/// <typeparam name="TCeiling">The variant the consumer knows how to handle.</typeparam>
public interface IMessageConsumer<TPayload, TCeiling>
    where TPayload : PayloadCeiling<TPayload, TCeiling>, IPayloadVariants<TPayload>
    where TCeiling : Payload<TPayload>.ICeiling
{
    /// <summary>Processes a single delivered envelope.</summary>
    Task HandleAsync(Envelope<TPayload, TCeiling> envelope, CancellationToken cancellationToken);
}
