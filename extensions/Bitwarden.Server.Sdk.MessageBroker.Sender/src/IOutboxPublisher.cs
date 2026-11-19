namespace Bitwarden.Server.Sdk.MessageBroker.Sender;

/// <summary>
/// A publisher that writes to an outbox, so it can hold a message back until a given time.
/// </summary>
internal interface IOutboxPublisher<TPayload>
    where TPayload : Payload<TPayload>
{
    Task SendLaterAsync(
        IReadOnlyList<Payload<TPayload>.IVariant> variants,
        string messageId,
        DateTimeOffset? notBefore,
        CancellationToken cancellationToken);
}
