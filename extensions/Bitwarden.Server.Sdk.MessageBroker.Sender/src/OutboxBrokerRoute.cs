using Microsoft.Extensions.DependencyInjection;

namespace Bitwarden.Server.Sdk.MessageBroker.Sender;

/// <summary>
/// Sends through the publisher registered under <see cref="MessageBrokerKeys.Direct"/>. The stored
/// body goes as written: decoding it would drop any version this build doesn't know.
/// </summary>
internal sealed class OutboxBrokerRoute<TPayload, TCeiling> : OutboxRoute
    where TPayload : PayloadCeiling<TPayload, TCeiling>, IPayloadVariants<TPayload>, IPayloadWireName<TPayload>
    where TCeiling : Payload<TPayload>.ICeiling
{
    private readonly Lazy<Publisher<TPayload, TCeiling>> _publisher;

    public OutboxBrokerRoute(string destination, IServiceProvider services)
    {
        Destination = destination;
        var key = MessageBrokerKeys.Direct(MessageBrokerKeys.Publisher<TPayload>(destination));
        _publisher = new(() => services.GetRequiredKeyedService<Publisher<TPayload, TCeiling>>(key));
    }

    public override string Destination { get; }

    public override string MessageType => TPayload.WireName;

    public override Task SendAsync(OutboxMessage message, CancellationToken cancellationToken) =>
        _publisher.Value.SendRawAsync(message.Body, message.MessageId, cancellationToken);
}
