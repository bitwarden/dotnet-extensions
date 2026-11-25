using System.Diagnostics;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// The envelope for a decoded <see cref="ReceivedMessage"/>; it settles through the message's own
/// complete, requeue and dead-letter delegates.
/// </summary>
internal sealed class ReceivedEnvelope<TPayload, TCeiling> : Envelope<TPayload, TCeiling>
    where TPayload : PayloadCeiling<TPayload, TCeiling>, IPayloadVariants<TPayload>
    where TCeiling : Payload<TPayload>.ICeiling
{
    private readonly ReceivedMessage _message;

    public ReceivedEnvelope(IReadOnlyList<Payload<TPayload>.IVariant> variants, ReceivedMessage message, Activity? activity)
        : base(variants, activity)
    {
        _message = message;
    }

    public override string MessageId => _message.MessageId;
    public override string? TraceId => _message.TraceParent;
    public override int DeliveryCount => _message.DeliveryCount;

    protected override Task CompleteAsyncCore(CancellationToken cancellationToken) =>
        _message.CompleteAsync(cancellationToken);

    protected override Task RequeueCoreAsync(CancellationToken cancellationToken) =>
        _message.RequeueAsync(cancellationToken);

    protected override Task DeadLetterAsyncCore(string? reason, CancellationToken cancellationToken) =>
        _message.DeadLetterAsync(reason, cancellationToken);
}
