namespace Bitwarden.Server.Sdk.MessageBroker;

/// <summary>Receives messages of family <typeparamref name="TFamily"/> on a named topic, resolved to <typeparamref name="TCeiling"/>.</summary>
/// <typeparam name="TFamily">The family.</typeparam>
/// <typeparam name="TCeiling">The variant the consumer knows how to handle.</typeparam>
public interface ISubscriber<TFamily, TCeiling>
    where TFamily : FamilyCeiling<TFamily, TCeiling>, IFamilyVariants<TFamily>
    where TCeiling : Family<TFamily>.ICeiling
{
    /// <summary>Returns an async stream of incoming envelopes.</summary>
    IAsyncEnumerable<Envelope<TFamily, TCeiling>> SubscribeAsync(CancellationToken cancellationToken);
}
