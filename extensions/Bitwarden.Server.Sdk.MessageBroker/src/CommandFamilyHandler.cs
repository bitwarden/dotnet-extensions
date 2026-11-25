using System.Diagnostics;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// The <see cref="ICommandFamilyHandler"/> for command family <typeparamref name="TPayload"/>, run by
/// consumer <typeparamref name="TConsumer"/>.
/// </summary>
internal sealed class CommandFamilyHandler<TPayload, TCeiling, TConsumer> : ICommandFamilyHandler
    where TPayload : PayloadCeiling<TPayload, TCeiling>, IPayloadVariants<TPayload>, IPayloadWireName<TPayload>
    where TCeiling : Payload<TPayload>.ICeiling
    where TConsumer : class, IMessageConsumer<TPayload, TCeiling>
{
    private readonly IMessageSerializer _serializer;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly MessageBrokerMetrics _metrics;

    public CommandFamilyHandler(string queueName, IMessageSerializer serializer, IServiceScopeFactory scopeFactory, MessageBrokerMetrics metrics)
    {
        QueueName = queueName;
        _serializer = serializer;
        _scopeFactory = scopeFactory;
        _metrics = metrics;
    }

    public string QueueName { get; }

    public string FamilyWireName => TPayload.WireName;

    public async Task HandleAsync(CommandQueueMessage message, CancellationToken stoppingToken)
    {
        IReadOnlyList<Payload<TPayload>.IVariant> variants;
        try
        {
            variants = _serializer.DeserializeVariants<TPayload>(message.Body.Span);
        }
        catch (Exception)
        {
            await DeadLetterAsync(message, "The command's body could not be deserialized.");
            return;
        }

        if (variants.Count == 0)
        {
            await DeadLetterAsync(message, "No version of the command on the wire is known to this receiver.");
            return;
        }

        var activity = MessageBrokerActivitySource.StartConsumerActivity(QueueName, TPayload.WireName, message.TraceParent);
        CommandEnvelope envelope;
        try
        {
            envelope = new CommandEnvelope(variants, message, activity);
        }
        catch (InvalidOperationException)
        {
            activity?.Dispose();
            await DeadLetterAsync(message, $"The command's versions cannot be resolved to {typeof(TCeiling).Name}.");
            return;
        }

        _metrics.RecordConsume(QueueName, TPayload.WireName, envelope.ConsumedVariantWireName);

        await ConsumerInvocation.RunAsync<TPayload, TCeiling, TConsumer>(_scopeFactory, envelope, stoppingToken);
    }

    private static async Task DeadLetterAsync(CommandQueueMessage message, string reason)
    {
        try
        {
            await message.DeadLetterAsync(reason, CancellationToken.None);
        }
        catch (Exception)
        {
            // An unsettled message is redelivered when its lock expires.
        }
    }

    private sealed class CommandEnvelope : Envelope<TPayload, TCeiling>
    {
        private readonly CommandQueueMessage _message;

        public CommandEnvelope(IReadOnlyList<Payload<TPayload>.IVariant> variants, CommandQueueMessage message, Activity? activity)
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
}
