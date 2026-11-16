using Microsoft.EntityFrameworkCore;

namespace Bitwarden.Server.Sdk.MessageBroker.Sender;

/// <summary>
/// The publisher a service injects: <c>SendAsync</c> serializes the message and adds it to the
/// outbox in <typeparamref name="TContext"/>. Nothing is sent, and nothing is saved, until the
/// caller's <c>SaveChangesAsync</c> commits it with the change it reports or asks for; the
/// dispatcher sends it after that.
/// </summary>
internal sealed class OutboxPublisher<TPayload, TCeiling, TContext> : Publisher<TPayload, TCeiling>, IOutboxPublisher<TPayload>
    where TPayload : PayloadCeiling<TPayload, TCeiling>, IPayloadVariants<TPayload>, IPayloadWireName<TPayload>
    where TCeiling : Payload<TPayload>.ICeiling
    where TContext : DbContext, IMessageOutboxContext
{
    private readonly string _destination;
    private readonly TContext _database;
    private readonly IMessageSerializer _serializer;
    private readonly TimeProvider _time;
    private readonly int _maxMessageSize;

    public OutboxPublisher(string destination, TContext database, IMessageSerializer serializer, TimeProvider time, int maxMessageSize)
    {
        _maxMessageSize = maxMessageSize;
        _destination = destination;
        _database = database;
        _serializer = serializer;
        _time = time;
    }

    protected internal override Task SendAsync(
        IReadOnlyList<Payload<TPayload>.IVariant> variants,
        string messageId,
        CancellationToken cancellationToken) =>
        SendLaterAsync(variants, messageId, notBefore: null, cancellationToken);

    public Task SendLaterAsync(
        IReadOnlyList<Payload<TPayload>.IVariant> variants,
        string messageId,
        DateTimeOffset? notBefore,
        CancellationToken cancellationToken)
    {
        _database.Set<OutboxMessage>().Add(
            OutboxRows.Create<TPayload>(_serializer, _destination, variants, messageId, notBefore, _time, _maxMessageSize));
        return Task.CompletedTask;
    }
}
