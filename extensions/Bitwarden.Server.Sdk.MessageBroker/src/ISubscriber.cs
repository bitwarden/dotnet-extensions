namespace Bitwarden.Server.Sdk.MessageBroker;

/// <summary>Receives messages of type <typeparamref name="T"/> on a named topic.</summary>
/// <typeparam name="T">The message type.</typeparam>
public interface ISubscriber<T>
{
    /// <summary>Returns an async stream of incoming messages.</summary>
    IAsyncEnumerable<Envelope<T>> SubscribeAsync(CancellationToken cancellationToken);
}
