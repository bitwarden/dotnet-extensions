namespace Bitwarden.Server.Sdk.MessageBroker.Sender;

/// <summary>
/// Adds a message to the service's outbox, to wherever the service registered its type to go,
/// without naming the destination: <c>await outbox.SendAsync&lt;OrganizationDeletedPayload,
/// OrganizationDeleted&gt;(new(id))</c>, then <c>SaveChangesAsync</c>.
/// </summary>
/// <remarks>
/// <para>
/// Scoped, like the <c>DbContext</c> the message is written to: the message commits with the
/// caller's next <c>SaveChangesAsync</c> on it, and nothing is sent without that save.
/// </para>
/// <para>
/// The message type must be registered through the outbox exactly once, to one destination
/// (<c>AddOutboxPublisher</c>, <c>AddOutboxCommandSender</c> or <c>AddOutboxLocalHandler</c>). A
/// type sent to several destinations is sent with each one's keyed publisher instead.
/// </para>
/// </remarks>
public interface IOutboxSender
{
    /// <summary>
    /// Adds <paramref name="message"/> to the outbox, to be sent once the caller's change is saved,
    /// and no earlier than <paramref name="notBefore"/> if given.
    /// </summary>
    /// <typeparam name="TPayload">The message's payload family.</typeparam>
    /// <typeparam name="TCeiling">The family's ceiling: the variant sent, from which every older one is derived.</typeparam>
    /// <param name="message">The message.</param>
    /// <param name="messageId">
    /// Its identifier, which every send of it keeps; a version 7 GUID if not given. Choose one to
    /// take the message back later by it (<c>RetractMessageAsync</c>, <c>RetractMessagesAsync</c>).
    /// </param>
    /// <param name="notBefore">The earliest time it is sent; a time already past sends it once it commits.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The message's identifier.</returns>
    /// <exception cref="InvalidOperationException">
    /// The type isn't registered through the outbox, or is registered to more than one destination.
    /// </exception>
    /// <exception cref="ArgumentException"><paramref name="messageId"/> isn't a valid message identifier.</exception>
    Task<string> SendAsync<TPayload, TCeiling>(
        TCeiling message,
        string? messageId = null,
        DateTimeOffset? notBefore = null,
        CancellationToken cancellationToken = default)
        where TPayload : PayloadCeiling<TPayload, TCeiling>, IPayloadVariants<TPayload>, IPayloadWireName<TPayload>
        where TCeiling : Payload<TPayload>.ICeiling, Payload<TPayload>.IReachesFloor;
}
