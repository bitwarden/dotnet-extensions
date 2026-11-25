using Microsoft.Extensions.DependencyInjection;

namespace Bitwarden.Server.Sdk.MessageBroker.Sender;

/// <summary>
/// Hands a waiting message to a handler in this service, in a scope of its own, instead of sending
/// it to the broker. The handler is settled as a subscription's would be.
/// </summary>
internal sealed class OutboxLocalRoute<TPayload, TCeiling, THandler> : OutboxRoute
    where TPayload : PayloadCeiling<TPayload, TCeiling>, IPayloadVariants<TPayload>, IPayloadWireName<TPayload>
    where TCeiling : Payload<TPayload>.ICeiling
    where THandler : class, IMessageConsumer<TPayload, TCeiling>
{
    private readonly IServiceProvider _services;
    private readonly Lazy<IMessageSerializer> _serializer;

    public OutboxLocalRoute(IServiceProvider services)
    {
        _services = services;
        _serializer = new(() => services.GetRequiredKeyedService<IMessageSerializer>(MessageBrokerKeys.Publisher<TPayload>(MessageOutbox.Local)));
    }

    public override string Destination => MessageOutbox.Local;

    public override string MessageType => TPayload.WireName;

    public override async Task SendAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        OutboxEnvelope<TPayload, TCeiling> envelope;
        try
        {
            envelope = new(_serializer.Value.DeserializeVariants<TPayload>(message.Body), message, message.Attempts + 1);
        }
        catch (Exception ex)
        {
            throw new OutboxMessageSetAsideException($"The stored '{TPayload.WireName}' message can't be read as {typeof(TCeiling).Name}.", ex);
        }

        await using var scope = _services.CreateAsyncScope();
        try
        {
            await scope.ServiceProvider.GetRequiredService<THandler>().HandleAsync(envelope, cancellationToken);
        }
        catch (Exception) when (envelope.Outcome is OutboxEnvelope<TPayload, TCeiling>.Settlement.Completed)
        {
            // Settled before it threw. An envelope settles once, as a subscription's does.
            return;
        }
        catch (Exception ex) when (envelope.Outcome is OutboxEnvelope<TPayload, TCeiling>.Settlement.DeadLettered)
        {
            throw new OutboxMessageSetAsideException($"The '{TPayload.WireName}' handler dead-lettered the message.", ex);
        }

        switch (envelope.Outcome)
        {
            case OutboxEnvelope<TPayload, TCeiling>.Settlement.Requeued:
                throw new OutboxMessageRequeuedException($"The '{TPayload.WireName}' handler asked to try again later.");
            case OutboxEnvelope<TPayload, TCeiling>.Settlement.DeadLettered:
                throw new OutboxMessageSetAsideException($"The '{TPayload.WireName}' handler dead-lettered the message.");
        }
    }
}
