using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Bitwarden.Server.Sdk.Service.JsonApi;

/// <summary>
/// Envelopes the single resource under a <see cref="ResourceDocument{T}"/>'s <c>data</c>.
/// </summary>
/// <typeparam name="T">The resource type.</typeparam>
internal sealed class ResourceDocumentJsonConverter<T> : JsonConverter<ResourceDocument<T>> where T : Resource
{
    public override ResourceDocument<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (JsonNode.Parse(ref reader) is not JsonObject document)
        {
            throw new JsonException("A resource document must be a JSON object.");
        }

        if (document[ResourceEnvelope.Data] is JsonObject data)
        {
            ResourceEnvelope.RequireType(data, ResourceTypes.For(typeof(T)), $"/{ResourceEnvelope.Data}");
            document[ResourceEnvelope.Data] = ResourceEnvelope.Flatten(data);
        }

        return document.Deserialize<ResourceDocument<T>>(FlattenedOptions.For(options))
            ?? throw new JsonException("A resource document must not be null.");
    }

    public override void Write(Utf8JsonWriter writer, ResourceDocument<T> value, JsonSerializerOptions options)
    {
        var document = Serialize(value, options);

        if (document[ResourceEnvelope.Data] is JsonObject data)
        {
            document[ResourceEnvelope.Data] = ResourceEnvelope.Sparse(ResourceEnvelope.Envelope(data), value.Fields);
        }

        document.WriteTo(writer, options);
    }

    private static JsonObject Serialize(ResourceDocument<T> value, JsonSerializerOptions options)
        => JsonSerializer.SerializeToNode(value, typeof(ResourceDocument<T>), FlattenedOptions.For(options)) as JsonObject
           ?? throw new JsonException("A resource document must serialise to a JSON object.");
}
