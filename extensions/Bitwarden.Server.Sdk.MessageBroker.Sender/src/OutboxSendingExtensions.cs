using Microsoft.EntityFrameworkCore;

namespace Bitwarden.Server.Sdk.MessageBroker.Sender;

/// <summary>
/// Sending later, and taking back a message that hasn't been sent.
/// </summary>
public static class OutboxSendingExtensions
{
    /// <summary>
    /// Adds the message to the outbox, like <c>SendAsync</c>, to be sent no earlier than
    /// <paramref name="notBefore"/>, under a new message identifier (a version 7 GUID). Keep the
    /// identifier to take it back with <see cref="RetractMessageAsync"/>.
    /// </summary>
    /// <returns>The message's identifier.</returns>
    public static Task<string> SendLaterAsync<TPayload, TCeiling, TCurrent>(
        this Publisher<TPayload, TCeiling>.PublishBuilder<TCurrent> builder,
        DateTimeOffset notBefore,
        CancellationToken cancellationToken = default)
        where TPayload : PayloadCeiling<TPayload, TCeiling>, IPayloadVariants<TPayload>
        where TCeiling : Payload<TPayload>.ICeiling
        where TCurrent : Payload<TPayload>.IReachesFloor
        => builder.SendLaterAsync(Guid.CreateVersion7().ToString(), notBefore, cancellationToken);

    /// <summary>
    /// Adds the message to the outbox, like <c>SendAsync</c>, to be sent under
    /// <paramref name="messageId"/> no earlier than <paramref name="notBefore"/>. A time already
    /// past sends it as soon as it is committed.
    /// </summary>
    /// <returns><paramref name="messageId"/>.</returns>
    /// <exception cref="ArgumentException"><paramref name="messageId"/> isn't a valid message identifier.</exception>
    /// <exception cref="InvalidOperationException">
    /// The publisher wasn't registered through the outbox, so it can't hold a message back.
    /// </exception>
    public static async Task<string> SendLaterAsync<TPayload, TCeiling, TCurrent>(
        this Publisher<TPayload, TCeiling>.PublishBuilder<TCurrent> builder,
        string messageId,
        DateTimeOffset notBefore,
        CancellationToken cancellationToken = default)
        where TPayload : PayloadCeiling<TPayload, TCeiling>, IPayloadVariants<TPayload>
        where TCeiling : Payload<TPayload>.ICeiling
        where TCurrent : Payload<TPayload>.IReachesFloor
    {
        PublisherExtensions.ThrowIfInvalidMessageId(messageId);
        if (builder.Publisher is not IOutboxPublisher<TPayload> outbox)
        {
            throw new InvalidOperationException(
                "Only a publisher registered through the outbox (AddOutboxPublisher, AddOutboxCommandSender) can send later.");
        }

        builder.Current.DescendToFloor(builder.Accumulated);
        await outbox.SendLaterAsync(builder.Accumulated, messageId, notBefore, cancellationToken);
        return messageId;
    }

    /// <summary>
    /// Takes back the message <paramref name="messageId"/> if it hasn't been sent. One added in
    /// this unit of work and not yet saved is dropped from it; one already saved is deleted at once,
    /// inside the caller's transaction if one is open.
    /// </summary>
    /// <remarks>
    /// A send that is already under way can't be recalled, so a retracted message can still
    /// arrive. Its receiver handles it as it handles any duplicate: harmlessly.
    /// </remarks>
    /// <returns>
    /// Whether the message was in the outbox and is now gone from it.
    /// </returns>
    public static async Task<bool> RetractMessageAsync<TContext>(
        this TContext database,
        string messageId,
        CancellationToken cancellationToken = default)
        where TContext : DbContext, IMessageOutboxContext
    {
        var outbox = database.Set<OutboxMessage>();
        var unsaved = outbox.Local.FirstOrDefault(row => row.MessageId == messageId);
        if (unsaved is not null && database.Entry(unsaved).State == EntityState.Added)
        {
            database.Entry(unsaved).State = EntityState.Detached;
            return true;
        }
        return await outbox.Where(row => row.MessageId == messageId).ExecuteDeleteAsync(cancellationToken) > 0;
    }

    /// <summary>
    /// Takes back every message of type <paramref name="messageType"/> not yet sent whose identifier
    /// starts with <paramref name="idPrefix"/>, but <paramref name="except"/>. One added in this unit
    /// of work and not yet saved is dropped from it; those already saved are deleted at once, inside
    /// the caller's transaction if one is open.
    /// </summary>
    /// <remarks>
    /// <para>
    /// For a sender that chooses its identifiers as <c>"{subject}.{unique}"</c>, such as
    /// <c>$"{deviceId:N}.{Guid.CreateVersion7()}"</c>, so the subject's messages can be found by it.
    /// End the prefix with the separator, so one subject's prefix is never another's start. The
    /// identifier, and so the prefix, is never a person's data.
    /// </para>
    /// <para>
    /// As with <see cref="RetractMessageAsync"/>, a retracted message can still arrive.
    /// </para>
    /// </remarks>
    /// <param name="database">The database holding the outbox.</param>
    /// <param name="messageType">The messages' type (their payload's wire name).</param>
    /// <param name="idPrefix">The start their identifiers share.</param>
    /// <param name="except">An identifier to leave, the latest send's; none if null.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>How many messages were taken back.</returns>
    public static async Task<int> RetractMessagesAsync<TContext>(
        this TContext database,
        string messageType,
        string idPrefix,
        string? except = null,
        CancellationToken cancellationToken = default)
        where TContext : DbContext, IMessageOutboxContext
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentException.ThrowIfNullOrWhiteSpace(messageType);
        ArgumentException.ThrowIfNullOrEmpty(idPrefix);

        var outbox = database.Set<OutboxMessage>();
        var retracted = 0;
        foreach (var unsaved in outbox.Local
            .Where(row => row.MessageType == messageType && row.MessageId.StartsWith(idPrefix, StringComparison.Ordinal) && row.MessageId != except)
            .Where(row => database.Entry(row).State == EntityState.Added)
            .ToList())
        {
            database.Entry(unsaved).State = EntityState.Detached;
            retracted++;
        }

        var saved = outbox.Where(row => row.MessageType == messageType && row.MessageId.StartsWith(idPrefix));
        if (except is not null)
        {
            saved = saved.Where(row => row.MessageId != except);
        }

        return retracted + await saved.ExecuteDeleteAsync(cancellationToken);
    }
}
