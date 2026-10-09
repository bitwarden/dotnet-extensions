using System.Buffers;
using Microsoft.Extensions.DependencyInjection;

namespace Bitwarden.Server.Sdk.MessageBroker;

/// <summary>Serializes and deserializes payload variant chains for the wire.</summary>
/// <remarks>
/// Register a custom implementation using
/// <see cref="ServiceCollectionServiceExtensions.AddKeyedSingleton{TService,TImplementation}(IServiceCollection,object)"/>
/// keyed to the topic name <em>before</em> calling
/// <see cref="Microsoft.Extensions.DependencyInjection.MessageBrokerServiceCollectionExtensions.AddPublisher{TPayload, TCeiling}"/>
/// or
/// <see cref="Microsoft.Extensions.DependencyInjection.MessageBrokerServiceCollectionExtensions.AddSubscriber{TPayload, TCeiling}"/>
/// to replace the default System.Text.Json serializer for that topic:
/// <code>
/// services.AddKeyedSingleton&lt;IMessageSerializer, MySerializer&gt;("my-topic");
/// services.AddPublisher&lt;MyPayload,MyVariantCeiling&gt;("my-topic");
/// </code>
/// A non-keyed <c>AddSingleton&lt;IMessageSerializer&gt;</c> registration is silently ignored.
/// </remarks>
public interface IMessageSerializer
{
    /// <summary>
    /// Serializes <paramref name="variants"/> into <paramref name="destination"/>. Each variant is
    /// tagged with its concrete type so a subscriber can decode a variant it recognizes and skip
    /// any it does not.
    /// </summary>
    void SerializeVariants<TPayload>(
        IEnumerable<Payload<TPayload>.IVariant> variants,
        IBufferWriter<byte> destination)
        where TPayload : Payload<TPayload>, IPayloadVariants<TPayload>;

    /// <summary>
    /// Deserializes the variants the subscriber can decode from <paramref name="source"/>.
    /// Unknown variant types are silently skipped so newer publishers do not break older
    /// subscribers.
    /// </summary>
    IReadOnlyList<Payload<TPayload>.IVariant> DeserializeVariants<TPayload>(
        ReadOnlySpan<byte> source)
        where TPayload : Payload<TPayload>, IPayloadVariants<TPayload>;
}
