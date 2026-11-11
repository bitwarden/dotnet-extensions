namespace Bitwarden.Server.Sdk.MessageBroker;

/// <summary>
/// Processes messages of family <typeparamref name="TFamily"/> at the consumer's declared
/// ceiling <typeparamref name="TCeiling"/>. The framework hands in an
/// <see cref="Envelope{TFamily, TCeiling}"/> whose <see cref="Envelope{TFamily, TCeiling}.Payload"/>
/// is the already-resolved latest variant — an exact match if the wire carried it, otherwise the
/// result of walking pure upcasts from an older variant.
/// </summary>
/// <remarks>
/// Register with
/// <see cref="Microsoft.Extensions.DependencyInjection.MessageBrokerServiceCollectionExtensions.AddMessageConsumer{TFamily, TCeiling, TConsumer}"/>,
/// which wires up the <see cref="ISubscriber{TFamily, TCeiling}"/> and hosts the consumer's processing
/// loop. Do not call <see cref="Envelope{TFamily, TCeiling}.CompleteAsync"/> or
/// <see cref="Envelope{TFamily, TCeiling}.RequeueAsync"/> inside <see cref="HandleAsync"/> — the
/// framework settles the envelope automatically. To permanently discard a message without
/// redelivery, call <see cref="Envelope{TFamily, TCeiling}.DeadLetterAsync"/> and then return
/// normally.
/// </remarks>
/// <typeparam name="TFamily">The family.</typeparam>
/// <typeparam name="TCeiling">The variant the consumer knows how to handle.</typeparam>
public interface IMessageConsumer<TFamily, TCeiling>
    where TFamily : FamilyCeiling<TFamily, TCeiling>, IFamilyVariants<TFamily>
    where TCeiling : Family<TFamily>.ICeiling
{
    /// <summary>Processes a single delivered envelope.</summary>
    /// <remarks>
    /// <see cref="System.Diagnostics.Activity.Current"/> is set to the receive span for the
    /// duration of this call, so nested activities, <c>HttpClient</c> spans, EF Core command
    /// spans, and scoped logs emitted here are parented to the <c>{topic} receive</c> span
    /// without any extra wiring.
    /// </remarks>
    Task HandleAsync(Envelope<TFamily, TCeiling> envelope, CancellationToken cancellationToken);
}
