using Microsoft.Extensions.DependencyInjection;

namespace Bitwarden.Server.Sdk.MessageBroker.Sender;

/// <inheritdoc />
internal sealed class OutboxSender(IServiceProvider services, IEnumerable<OutboxDestination> destinations) : IOutboxSender
{
    private readonly OutboxDestination[] _destinations = [.. destinations];

    public async Task<string> SendAsync<TPayload, TCeiling>(
        TCeiling message,
        string? messageId = null,
        DateTimeOffset? notBefore = null,
        CancellationToken cancellationToken = default)
        where TPayload : PayloadCeiling<TPayload, TCeiling>, IPayloadVariants<TPayload>, IPayloadWireName<TPayload>
        where TCeiling : Payload<TPayload>.ICeiling, Payload<TPayload>.IReachesFloor
    {
        ArgumentNullException.ThrowIfNull(message);
        messageId ??= Guid.CreateVersion7().ToString();
        PublisherExtensions.ThrowIfInvalidMessageId(messageId);

        var destination = _destinations.Where(each => each.Payload == typeof(TPayload)).Select(each => each.Destination).Distinct().ToList() switch
        {
            [var one] => one,
            [] => throw new InvalidOperationException(
                $"'{TPayload.WireName}' isn't registered to be sent through the outbox. Register it with " +
                "AddOutboxPublisher, AddOutboxCommandSender or AddOutboxLocalHandler."),
            var several => throw new InvalidOperationException(
                $"'{TPayload.WireName}' is registered to be sent to {string.Join(", ", several)}. Send it with the " +
                "publisher keyed for the destination meant."),
        };

        var publisher = services.GetRequiredKeyedService<Publisher<TPayload, TCeiling>>(MessageBrokerKeys.Publisher<TPayload>(destination));
        if (publisher is not IOutboxPublisher<TPayload> outbox)
        {
            throw new InvalidOperationException($"'{TPayload.WireName}' to '{destination}' isn't sent through the outbox.");
        }

        var variants = new List<Payload<TPayload>.IVariant> { message };
        message.DescendToFloor(variants);
        await outbox.SendLaterAsync(variants, messageId, notBefore, cancellationToken);
        return messageId;
    }
}
