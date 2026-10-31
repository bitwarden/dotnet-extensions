using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Bitwarden.Server.Sdk.Service.JsonApi;

/// <summary>
/// Envelopes each resource in a collection document's <c>data</c>.
/// </summary>
/// <typeparam name="TDocument">The document type.</typeparam>
/// <typeparam name="T">The resource type.</typeparam>
/// <remarks>
/// Shared by the paged and unpaged documents, which differ only in the members beside
/// <c>data</c>, and those the ordinary serializer handles.
/// </remarks>
internal abstract class CollectionJsonConverter<TDocument, T> : JsonConverter<TDocument>
    where TDocument : class
    where T : Resource
{
    public override TDocument Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (JsonNode.Parse(ref reader) is not JsonObject document)
        {
            throw new JsonException("A resource collection document must be a JSON object.");
        }

        return Read(document, options);
    }

    /// <summary>
    /// Reads a document already parsed, flattening each resource in its <c>data</c>.
    /// </summary>
    /// <param name="document">The document, which this changes.</param>
    /// <param name="options">The caller's serializer options.</param>
    protected TDocument Read(JsonObject document, JsonSerializerOptions options)
    {
        if (document[ResourceEnvelope.Data] is JsonArray data)
        {
            var expected = ResourceTypes.For(typeof(T));

            for (var i = 0; i < data.Count; i++)
            {
                if (data[i] is JsonObject element)
                {
                    ResourceEnvelope.RequireType(element, expected, $"/{ResourceEnvelope.Data}/{i}");
                }
            }

            document[ResourceEnvelope.Data] = Map(data, ResourceEnvelope.Flatten);
        }

        return document.Deserialize<TDocument>(FlattenedOptions.For(options))
            ?? throw new JsonException("A resource collection document must not be null.");
    }

    public override void Write(Utf8JsonWriter writer, TDocument value, JsonSerializerOptions options)
    {
        var document = JsonSerializer.SerializeToNode(value, typeof(TDocument), FlattenedOptions.For(options))
            as JsonObject
            ?? throw new JsonException("A resource collection document must serialise to a JSON object.");

        if (document[ResourceEnvelope.Data] is JsonArray data)
        {
            var fields = (value as ISparseDocument)?.Fields;
            var enveloped = Map(data, resource => ResourceEnvelope.Sparse(ResourceEnvelope.Envelope(resource), fields));
            Decorate(enveloped, value);
            document[ResourceEnvelope.Data] = enveloped;
        }

        document.WriteTo(writer, options);
    }

    /// <summary>
    /// Adds whatever a document carries beside each resource, once the resources are enveloped.
    /// </summary>
    /// <param name="resources">The enveloped resources, in order.</param>
    /// <param name="value">The document being written.</param>
    protected virtual void Decorate(JsonArray resources, TDocument value)
    {
    }

    private static JsonArray Map(JsonArray source, Func<JsonObject, JsonObject> transform)
    {
        var mapped = new JsonArray();

        foreach (var element in source)
        {
            mapped.Add(element is JsonObject resource ? transform(resource) : element?.DeepClone());
        }

        return mapped;
    }
}
