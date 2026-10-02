using System.Buffers;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

internal sealed class SystemTextJsonMessageSerializer : IMessageSerializer
{
    private readonly JsonSerializerOptions _jsonOptions;

    public SystemTextJsonMessageSerializer(IOptionsMonitor<MessageBrokerSerializerOptions> optionsMonitor, string name)
    {
        _jsonOptions = optionsMonitor.Get(name).JsonSerializerOptions;
        if (_jsonOptions.TypeInfoResolver is null)
        {
            throw new InvalidOperationException(
                $"No TypeInfoResolver is configured for message broker queue '{name}'. " +
                $"When reflection is disabled, configure a JsonSerializerContext via " +
                $"services.Configure<{nameof(MessageBrokerSerializerOptions)}>(name, o => o.JsonSerializerOptions.TypeInfoResolverChain.Add(MyContext.Default)).");
        }
    }

    public void Serialize<T>(T message, IBufferWriter<byte> destination)
    {
        var typeInfo = (JsonTypeInfo<T>)_jsonOptions.GetTypeInfo(typeof(T));
        using var writer = new Utf8JsonWriter(destination);
        JsonSerializer.Serialize(writer, message, typeInfo);
    }

    public T? Deserialize<T>(ReadOnlySpan<byte> source)
    {
        var typeInfo = (JsonTypeInfo<T>)_jsonOptions.GetTypeInfo(typeof(T));
        return JsonSerializer.Deserialize(source, typeInfo);
    }
}
