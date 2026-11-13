namespace Bitwarden.Server.Sdk.MessageBroker;

/// <summary>Receives payloads of family <typeparamref name="TPayload"/> on a named topic, resolved to <typeparamref name="TCeiling"/>.</summary>
/// <typeparam name="TPayload">The payload family.</typeparam>
/// <typeparam name="TCeiling">The variant the consumer knows how to handle.</typeparam>
public interface ISubscriber<TPayload, TCeiling>
    where TPayload : PayloadCeiling<TPayload, TCeiling>, IPayloadVariants<TPayload>
    where TCeiling : Payload<TPayload>.ICeiling
{
    /// <summary>Returns an async stream of incoming envelopes.</summary>
    IAsyncEnumerable<Envelope<TPayload, TCeiling>> SubscribeAsync(CancellationToken cancellationToken);
}
