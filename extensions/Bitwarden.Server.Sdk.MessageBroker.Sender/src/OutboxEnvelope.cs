namespace Bitwarden.Server.Sdk.MessageBroker.Sender;

/// <summary>
/// The envelope a handler in this service receives. Settling it only records the outcome; the
/// dispatcher acts on it once the handler returns.
/// </summary>
internal sealed class OutboxEnvelope<TPayload, TCeiling> : Envelope<TPayload, TCeiling>
    where TPayload : PayloadCeiling<TPayload, TCeiling>, IPayloadVariants<TPayload>
    where TCeiling : Payload<TPayload>.ICeiling
{
    internal enum Settlement { None, Completed, Requeued, DeadLettered }

    public OutboxEnvelope(IEnumerable<Payload<TPayload>.IVariant> variants, OutboxMessage message, int deliveryCount)
        : base(variants)
    {
        MessageId = message.MessageId;
        TraceId = message.TraceParent;
        DeliveryCount = deliveryCount;
    }

    public override string MessageId { get; }

    public override string? TraceId { get; }

    public override int DeliveryCount { get; }

    internal Settlement Outcome { get; private set; }

    protected override Task CompleteAsyncCore(CancellationToken cancellationToken)
    {
        Outcome = Settlement.Completed;
        return Task.CompletedTask;
    }

    protected override Task RequeueCoreAsync(CancellationToken cancellationToken)
    {
        Outcome = Settlement.Requeued;
        return Task.CompletedTask;
    }

    protected override Task DeadLetterAsyncCore(string? reason, CancellationToken cancellationToken)
    {
        Outcome = Settlement.DeadLettered;
        return Task.CompletedTask;
    }
}
