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

    public async Task HandleAsync(ReceivedMessage message, CancellationToken stoppingToken)
    {
        var envelope = await ReceivedMessageDecoder.DecodeOrDeadLetterAsync<TPayload, TCeiling>(message, _serializer, QueueName, _metrics);
        if (envelope is null)
        {
            return;
        }

        await ConsumerInvocation.RunAsync<TPayload, TCeiling, TConsumer>(_scopeFactory, envelope, stoppingToken);
    }
}
