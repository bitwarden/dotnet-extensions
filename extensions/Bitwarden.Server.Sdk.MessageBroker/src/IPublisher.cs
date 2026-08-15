namespace Bitwarden.Server.Sdk.MessageBroker;

/// <summary>Publishes messages of type <typeparamref name="T"/> to a named channel.</summary>
/// <typeparam name="T">The message type.</typeparam>
public interface IPublisher<T>
{
    /// <summary>Publishes a message.</summary>
    Task PublishAsync(T message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes a batch of messages. The default implementation publishes each message
    /// sequentially; backends that support atomic batch delivery may override this.
    /// </summary>
    async Task PublishBatchAsync(IEnumerable<T> messages, CancellationToken cancellationToken = default)
    {
        foreach (var message in messages)
            await PublishAsync(message, cancellationToken);
    }
}
