using System.Buffers;
using Microsoft.Extensions.DependencyInjection;

namespace Bitwarden.Server.Sdk.MessageBroker;

/// <summary>Serializes and deserializes message broker messages.</summary>
/// <remarks>
/// Register a custom implementation using
/// <see cref="ServiceCollectionServiceExtensions.AddKeyedSingleton{TService,TImplementation}(IServiceCollection,object)"/>
/// keyed to the topic name <em>before</em> calling
/// <see cref="MessageBrokerServiceCollectionExtensions.AddPublisher{T}"/> or
/// <see cref="MessageBrokerServiceCollectionExtensions.AddSubscriber{T}"/> to replace the default
/// System.Text.Json serializer for that topic:
/// <code>
/// services.AddKeyedSingleton&lt;IMessageSerializer, MySerializer&gt;("my-topic");
/// services.AddPublisher&lt;MyMessage&gt;("my-topic");
/// </code>
/// A non-keyed <c>AddSingleton&lt;IMessageSerializer&gt;</c> registration is silently ignored.
/// </remarks>
public interface IMessageSerializer
{
    /// <summary>Serializes <paramref name="message"/> into <paramref name="destination"/>.</summary>
    void Serialize<T>(T message, IBufferWriter<byte> destination);

    /// <summary>Deserializes a message from <paramref name="source"/>.</summary>
    T? Deserialize<T>(ReadOnlySpan<byte> source);
}
