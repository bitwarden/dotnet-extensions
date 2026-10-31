using System.Text.Json;
using System.Text.Json.Nodes;
using Bitwarden.Server.Sdk.Service.Querying;

namespace Bitwarden.Server.Sdk.Service.JsonApi;

/// <summary>
/// Envelopes the resources a bulk create carries, each shaped as the single-resource create
/// expects it.
/// </summary>
/// <typeparam name="T">The resource type.</typeparam>
internal sealed class BulkCreateJsonConverter<T>
    : CollectionJsonConverter<BulkCreate<T>, T> where T : Resource
{
    /// <remarks>
    /// Reads each item's <c>lid</c> off it before the item becomes a resource, so the resource
    /// never sees a member it does not declare, and keeps them on the request in item order.
    /// A <c>lid</c> must be a non-empty string and no two items may share one: a duplicated
    /// local identifier correlates nothing, and the standards forbid naming the same resource
    /// twice.
    /// </remarks>
    public override BulkCreate<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (JsonNode.Parse(ref reader) is not JsonObject document)
        {
            throw new JsonException("A resource collection document must be a JSON object.");
        }

        var lids = new List<string?>();

        if (document[ResourceEnvelope.Data] is JsonArray data)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);

            for (var i = 0; i < data.Count; i++)
            {
                if (data[i] is not JsonObject element)
                {
                    lids.Add(null);
                    continue;
                }

                lids.Add(TakeLid(element, $"/{ResourceEnvelope.Data}/{i}", seen));
            }
        }

        var request = Read(document, options);
        request.Lids = lids;

        return request;
    }

    private static string? TakeLid(JsonObject element, string pointer, HashSet<string> seen)
    {
        if (!element.TryGetPropertyValue(ResourceEnvelope.Lid, out var lid))
        {
            return null;
        }

        element.Remove(ResourceEnvelope.Lid);

        if (lid is not JsonValue value
            || value.GetValueKind() != JsonValueKind.String
            || value.GetValue<string>() is not { Length: > 0 } local)
        {
            throw new ResourceContractException(
                "A \"lid\" is a non-empty string the caller chooses.", $"{pointer}/{ResourceEnvelope.Lid}");
        }

        if (!seen.Add(local))
        {
            throw new ResourceContractException(
                $"Two items carry the \"lid\" \"{local}\". Each item's local identifier must be its own.",
                $"{pointer}/{ResourceEnvelope.Lid}");
        }

        return local;
    }
}
