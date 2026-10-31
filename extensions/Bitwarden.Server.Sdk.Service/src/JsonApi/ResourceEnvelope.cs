using System.Text.Json;
using System.Text.Json.Nodes;

namespace Bitwarden.Server.Sdk.Service.JsonApi;

/// <summary>
/// Moves a resource's members into and out of the JSON:API <c>attributes</c> envelope.
/// </summary>
internal static class ResourceEnvelope
{
    internal const string Attributes = "attributes";
    internal const string Data = "data";
    internal const string Id = "id";
    internal const string Lid = "lid";
    internal const string Type = "type";

    /// <summary>
    /// Turns <c>{id, type, attributes:{…}}</c> into the flat shape the record declares.
    /// </summary>
    /// <remarks>
    /// A member outside <c>attributes</c> wins over one inside it. The specification does not permit
    /// both, and preferring the outer keeps <c>id</c> and <c>type</c> authoritative.
    /// </remarks>
    public static JsonObject Flatten(JsonObject enveloped)
    {
        var flattened = new JsonObject();

        if (enveloped.TryGetPropertyValue(Attributes, out var attributes) && attributes is JsonObject attributeObject)
        {
            foreach (var attribute in attributeObject)
            {
                flattened[attribute.Key] = attribute.Value?.DeepClone();
            }
        }

        foreach (var member in enveloped)
        {
            if (member.Key != Attributes)
            {
                flattened[member.Key] = member.Value?.DeepClone();
            }
        }

        return flattened;
    }

    /// <summary>
    /// Refuses a resource whose <c>type</c> is missing or is not the one expected.
    /// </summary>
    /// <remarks>
    /// The standards make <c>type</c> a required member, and the schema pins it to a single value.
    /// Checked here so the contract is enforced rather than merely published: without this, an
    /// absent <c>type</c> is quietly filled in from the record's own name and a wrong one is taken
    /// at face value, so a caller could post a <c>widget</c> and be told it created one.
    /// </remarks>
    /// <param name="resource">The resource object as it arrived.</param>
    /// <param name="expected">The only type this resource may declare.</param>
    /// <param name="pointer">A JSON Pointer to the resource within the document.</param>
    /// <exception cref="ResourceContractException">The type is absent, not a string, or not <paramref name="expected"/>.</exception>
    public static void RequireType(JsonObject resource, string expected, string pointer)
    {
        if (!resource.TryGetPropertyValue(Type, out var declared)
            || declared is not JsonValue value
            || value.GetValueKind() != JsonValueKind.String)
        {
            throw new ResourceContractException(
                $"A resource must declare \"{Type}\": \"{expected}\".", $"{pointer}/{Type}");
        }

        var actual = value.GetValue<string>();

        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            throw new ResourceContractException(
                $"A resource of type \"{expected}\" was expected, but \"{actual}\" was declared.",
                $"{pointer}/{Type}");
        }
    }

    /// <summary>
    /// Turns the flat record shape into <c>{id, type, attributes:{…}}</c>.
    /// </summary>
    /// <remarks>
    /// <c>id</c> and <c>type</c> lead, as in every example in the standards. <c>attributes</c> is
    /// always written, as an empty object when no member is set (every one null, so left out): the
    /// published schema requires it, and a generated client refuses a resource without it.
    /// </remarks>
    public static JsonObject Envelope(JsonObject flattened)
    {
        var enveloped = new JsonObject();

        foreach (var identity in (string[])[Id, Type])
        {
            if (flattened.TryGetPropertyValue(identity, out var value) && value is not null)
            {
                enveloped[identity] = value.DeepClone();
            }
        }

        var attributes = new JsonObject();

        foreach (var member in flattened)
        {
            if (member.Key is Id or Type)
            {
                continue;
            }

            attributes[member.Key] = member.Value?.DeepClone();
        }

        enveloped[Attributes] = attributes;

        return enveloped;
    }

    /// <summary>
    /// Leaves out of an enveloped resource every attribute a sparse fieldset did not ask for.
    /// </summary>
    /// <param name="enveloped">The resource, as <see cref="Envelope"/> made it; changed in place.</param>
    /// <param name="fields">The attributes asked for, or null for every one.</param>
    /// <returns>The same resource.</returns>
    public static JsonObject Sparse(JsonObject enveloped, IReadOnlySet<string>? fields)
    {
        if (fields is null || enveloped[Attributes] is not JsonObject attributes)
        {
            return enveloped;
        }

        // An emptied attributes object stays: the schema requires it.
        foreach (var name in attributes.Select(attribute => attribute.Key).Where(name => !fields.Contains(name)).ToList())
        {
            attributes.Remove(name);
        }

        return enveloped;
    }
}
