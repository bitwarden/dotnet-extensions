using System.Collections;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using Bitwarden.Server.Sdk.Common.Errors;
using Microsoft.AspNetCore.Http;

namespace Bitwarden.Server.Sdk.Service.JsonApi;

/// <summary>
/// Validates a resource document before any of it reaches a service's code.
/// </summary>
/// <remarks>
/// <para>
/// Validation proceeds one level at a time (the document, then the resource, then its attributes)
/// and reports every error found at the level that failed. Going deeper would mean guessing:
/// there is nothing useful to say about an attribute when the object that should contain it is
/// missing.
/// </para>
/// <para>
/// The body is also cleaned as it is checked. Unrecognized members and read-only members are
/// dropped rather than rejected, as the standards require, so a caller that sends a field the
/// service has rolled back, or one it may read but not write, is not broken by it.
/// </para>
/// </remarks>
internal static class ResourceDocumentBinding
{
    /// <summary>
    /// Validates and cleans a body, or explains why it cannot be used.
    /// </summary>
    public static DocumentFailure? TryBind(
        string body,
        JsonTypeInfo resource,
        string expectedResourceType,
        IReadOnlySet<string> readOnlyDocumentMembers,
        out JsonObject? document)
    {
        document = null;

        if (string.IsNullOrWhiteSpace(body))
        {
            return Malformed(ErrorDetails.EmptyBody);
        }

        JsonNode? root;

        try
        {
            root = JsonNode.Parse(body);
        }
        catch (JsonException)
        {
            return Malformed(ErrorDetails.MalformedJson);
        }

        if (root is not JsonObject rootObject)
        {
            return Unprocessable([Error(ErrorCodes.InvalidShape, ErrorTitles.InvalidShape, ErrorDetails.NotAnObject, "/")]);
        }

        // The document's own read-only members are the caller's to read, not to send.
        foreach (var member in readOnlyDocumentMembers)
        {
            rootObject.Remove(member);
        }

        // Level one: the document.
        if (Missing(rootObject, ResourceEnvelope.Data))
        {
            return Unprocessable([Required(ResourceEnvelope.Data, "/data")]);
        }

        if (rootObject[ResourceEnvelope.Data] is not JsonObject data)
        {
            return Unprocessable([WrongType(ResourceEnvelope.Data, "an object", "/data")]);
        }

        // Level two: the resource.
        var resourceErrors = ValidateResource(data, expectedResourceType);

        if (resourceErrors.Count > 0)
        {
            return Unprocessable(resourceErrors);
        }

        // Level three: the attributes.
        var attributes = (JsonObject)data[ResourceEnvelope.Attributes]!;
        var attributeErrors = ValidateAttributes(attributes, resource);

        if (attributeErrors.Count > 0)
        {
            return Unprocessable(attributeErrors);
        }

        document = rootObject;
        return null;
    }

    /// <summary>
    /// Checks the members of the resource object itself.
    /// </summary>
    private static List<Error> ValidateResource(JsonObject data, string expectedResourceType)
    {
        var errors = new List<Error>();

        if (Missing(data, ResourceEnvelope.Type))
        {
            errors.Add(Required(ResourceEnvelope.Type, "/data/type"));
        }
        else if (data[ResourceEnvelope.Type] is not JsonValue type || type.GetValueKind() != JsonValueKind.String)
        {
            errors.Add(WrongType(ResourceEnvelope.Type, "a string", "/data/type"));
        }
        else if (!string.Equals(type.GetValue<string>(), expectedResourceType, StringComparison.Ordinal))
        {
            errors.Add(new Error
            {
                Status = StatusCodes.Status422UnprocessableEntity.ToString(),
                Code = ErrorCodes.InvalidResourceType,
                Title = ErrorTitles.InvalidResourceType,
                Detail = ErrorDetails.InvalidResourceType(expectedResourceType, type.GetValue<string>()),
                Source = new ErrorSource { Pointer = "/data/type" },
            });
        }

        if (Missing(data, ResourceEnvelope.Attributes))
        {
            errors.Add(Required(ResourceEnvelope.Attributes, "/data/attributes"));
        }
        else if (data[ResourceEnvelope.Attributes] is not JsonObject)
        {
            errors.Add(WrongType(ResourceEnvelope.Attributes, "an object", "/data/attributes"));
        }

        return errors;
    }

    /// <summary>
    /// Checks each attribute, dropping the ones a caller may not set.
    /// </summary>
    private static List<Error> ValidateAttributes(JsonObject attributes, JsonTypeInfo resource)
    {
        var errors = new List<Error>();
        var known = resource.Properties.ToDictionary(property => property.Name, StringComparer.Ordinal);

        foreach (var member in attributes.ToArray())
        {
            // Unrecognized, or read-only and therefore not the caller's to set. Dropped rather
            // than rejected, including when its value is the wrong type, because a field we are
            // going to ignore is not worth failing a request over.
            if (!known.TryGetValue(member.Key, out var property) || IsReadOnly(property))
            {
                attributes.Remove(member.Key);
                continue;
            }

            var verbatim = IsVerbatim(property);

            if (verbatim ? member.Value is null || member.Value.GetValueKind() == JsonValueKind.Null : IsAbsent(member.Value))
            {
                // An empty string and an explicit null both count as "not present", but for a
                // verbatim member, whose blank value is the caller's to have checked.
                attributes.Remove(member.Key);
                continue;
            }

            if (!verbatim && member.Value is JsonValue text && text.GetValueKind() == JsonValueKind.String)
            {
                attributes[member.Key] = text.GetValue<string>().Trim();
            }

            var (acceptable, expected) = Accepts(property.PropertyType, attributes[member.Key]);

            if (!acceptable)
            {
                errors.Add(WrongType(member.Key, expected, $"/data/attributes/{member.Key}"));
                continue;
            }

            // A string of the right shape may still not be a value the enumeration has. Caught
            // here rather than left to the deserializer, which throws, and an unrecognised
            // value is the caller's mistake, not the service's failure.
            //
            // Inside a list as well as on its own. A set of enumerated values is as ordinary as
            // a single one, and an unrecognised member of a list reaches the deserializer by
            // exactly the same route.
            foreach (var (permitted, at) in Unrecognisable(property.PropertyType, attributes[member.Key]))
            {
                errors.Add(new Error
                {
                    Status = StatusCodes.Status422UnprocessableEntity.ToString(),
                    Code = ErrorCodes.InvalidValue,
                    Title = ErrorTitles.InvalidValue,
                    Detail = ErrorDetails.NotAPermittedValue(member.Key, permitted),
                    Source = new ErrorSource { Pointer = $"/data/attributes/{member.Key}{at}" },
                });
            }
        }

        foreach (var property in resource.Properties)
        {
            if (!property.IsSetNullable
                && !IsReadOnly(property)
                && property.Name != ResourceEnvelope.Id
                && property.Name != ResourceEnvelope.Type
                && !attributes.ContainsKey(property.Name))
            {
                errors.Add(Required(property.Name, $"/data/attributes/{property.Name}"));
            }
        }

        return errors;
    }

    /// <summary>
    /// Every unrecognised enumerated value under a member, with a pointer suffix locating each.
    /// </summary>
    /// <remarks>
    /// A member is either an enumeration or a list of one. The suffix is empty for the first and
    /// an index for the second, so a caller sending twenty features is told which one is wrong
    /// rather than that something is.
    /// </remarks>
    private static IEnumerable<(IReadOnlyList<string> Permitted, string At)> Unrecognisable(
        Type type,
        JsonNode? value)
    {
        if (value is JsonArray items)
        {
            var element = ElementType(type);

            if (element is null)
            {
                yield break;
            }

            for (var i = 0; i < items.Count; i++)
            {
                if (Unrecognised(element, items[i]) is { } permitted)
                {
                    yield return (permitted, $"/{i}");
                }
            }

            yield break;
        }

        if (Unrecognised(type, value) is { } single)
        {
            yield return (single, string.Empty);
        }
    }

    /// <summary>
    /// What a sequence type holds, or null if it is not a sequence.
    /// </summary>
    private static Type? ElementType(Type type)
    {
        if (type.IsArray)
        {
            return type.GetElementType();
        }

        return type.IsGenericType && type.GetGenericArguments().Length == 1
            ? type.GetGenericArguments()[0]
            : null;
    }

    /// <summary>
    /// The permitted values, when a value is not one of them; otherwise null.
    /// </summary>
    private static IReadOnlyList<string>? Unrecognised(Type type, JsonNode? value)
    {
        var target = Nullable.GetUnderlyingType(type) ?? type;

        if (!target.IsEnum || value is not JsonValue text || text.GetValueKind() != JsonValueKind.String)
        {
            return null;
        }

        return EnumValues.Recognises(target, text.GetValue<string>())
            ? null
            : EnumValues.Of(target);
    }

    private static bool IsReadOnly(JsonPropertyInfo property) =>
        property.AttributeProvider?.GetCustomAttributes(typeof(ReadOnlyAttribute), inherit: true).Length > 0;

    private static bool IsVerbatim(JsonPropertyInfo property) =>
        property.AttributeProvider?.GetCustomAttributes(typeof(VerbatimAttribute), inherit: true).Length > 0;

    /// <summary>
    /// A member is absent if it is missing, null, or an empty or blank string.
    /// </summary>
    private static bool IsAbsent(JsonNode? value) =>
        value is null
        || value.GetValueKind() == JsonValueKind.Null
        || (value is JsonValue text
            && text.GetValueKind() == JsonValueKind.String
            && string.IsNullOrWhiteSpace(text.GetValue<string>()));

    private static bool Missing(JsonObject container, string member) =>
        !container.TryGetPropertyValue(member, out var value) || IsAbsent(value);

    /// <summary>
    /// Whether a JSON value can become the given type, and what it should have been.
    /// </summary>
    private static (bool Acceptable, string Expected) Accepts(Type type, JsonNode? value)
    {
        var target = Nullable.GetUnderlyingType(type) ?? type;
        var kind = value?.GetValueKind() ?? JsonValueKind.Null;

        // A free-form member is any JSON at all (an object, an array, a string, a number), which
        // the resource carries as it came and checks itself, if it checks it at all: an access
        // rule's conditions are an array. Asking it to be an object would refuse every other value
        // the member exists to hold.
        if (target == typeof(JsonElement) || typeof(JsonNode).IsAssignableFrom(target))
        {
            return (true, "any JSON value");
        }

        if (target == typeof(bool))
        {
            return (kind is JsonValueKind.True or JsonValueKind.False, "a boolean");
        }

        if (target.IsEnum || target == typeof(string) || target == typeof(Guid)
            || target == typeof(DateTimeOffset) || target == typeof(DateTime) || target == typeof(TimeSpan))
        {
            return (kind == JsonValueKind.String, "a string");
        }

        if (target.IsPrimitive || target == typeof(decimal))
        {
            return (kind == JsonValueKind.Number, "a number");
        }

        // Before the sequence check, because a dictionary is one and would otherwise be
        // asked for as an array when what it needs is an object.
        if (IsDictionary(target))
        {
            return (kind == JsonValueKind.Object, "an object");
        }

        if (target != typeof(string) && typeof(IEnumerable).IsAssignableFrom(target))
        {
            return (kind == JsonValueKind.Array, "an array");
        }

        return (kind == JsonValueKind.Object, "an object");
    }

    private static bool IsDictionary(Type type) =>
        typeof(System.Collections.IDictionary).IsAssignableFrom(type)
        || type.GetInterfaces().Append(type).Any(candidate =>
            candidate.IsGenericType
            && candidate.GetGenericTypeDefinition() is var definition
            && (definition == typeof(IDictionary<,>) || definition == typeof(IReadOnlyDictionary<,>)));

    private static DocumentFailure Malformed(string detail) =>
        new(StatusCodes.Status400BadRequest,
            [Error(ErrorCodes.MalformedJson, ErrorTitles.MalformedJson, detail, "/", StatusCodes.Status400BadRequest)]);

    private static DocumentFailure Unprocessable(IReadOnlyList<Error> errors) =>
        new(StatusCodes.Status422UnprocessableEntity, errors);

    private static Error Required(string field, string pointer) =>
        Error(ErrorCodes.FieldRequired, ErrorTitles.FieldRequired, ErrorDetails.FieldRequired(field), pointer);

    private static Error WrongType(string field, string expected, string pointer) =>
        Error(ErrorCodes.InvalidType, ErrorTitles.InvalidType, ErrorDetails.InvalidType(field, expected), pointer);

    private static Error Error(string code, string title, string detail, string pointer, int status = StatusCodes.Status422UnprocessableEntity) =>
        new()
        {
            Status = status.ToString(),
            Code = code,
            Title = title,
            Detail = detail,
            Source = new ErrorSource { Pointer = pointer },
        };
}
