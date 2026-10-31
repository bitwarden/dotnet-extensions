using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bitwarden.Server.Sdk.Service.Querying;

/// <summary>
/// Which entity property each sortable or filterable resource field is stored as.
/// </summary>
/// <remarks>
/// <para>
/// A resource's field names are its contract and an entity's are its storage, and nothing makes
/// them the same. Where they are, the field says nothing; where they are not, it says
/// <c>MappedTo</c>. What this adds is the check: the first time a resource's fields are resolved
/// against an entity, every sortable (or filterable) field is resolved at once, and any that
/// does not reach a property fails there, naming each one. So a forgotten mapping fails the first
/// test that sorts or searches the resource, rather than the one request that happens to use it.
/// </para>
/// </remarks>
internal static class StoredFields
{
    private static readonly ConcurrentDictionary<Type, IReadOnlyDictionary<string, string>> Filterable = new();
    private static readonly ConcurrentDictionary<(Type Resource, Type Entity, string Kind), IReadOnlyDictionary<string, PropertyInfo>> Resolved = new();

    /// <summary>
    /// The sortable fields of a resource, resolved to the entity's properties.
    /// </summary>
    public static IReadOnlyDictionary<string, PropertyInfo> SortableOn(Type resource, Type entity) =>
        Resolved.GetOrAdd((resource, entity, "sortable"), key => Resolve(
            key.Resource,
            key.Entity,
            SortableFields.Of(key.Resource).ToDictionary(field => field.Name, field => field.Stored, StringComparer.OrdinalIgnoreCase),
            "sortable",
            "Sortable"));

    /// <summary>
    /// The filterable fields of a resource, resolved to the entity's properties.
    /// </summary>
    public static IReadOnlyDictionary<string, PropertyInfo> FilterableOn(Type resource, Type entity) =>
        Resolved.GetOrAdd((resource, entity, "filterable"), key => Resolve(
            key.Resource,
            key.Entity,
            FilterableOf(key.Resource),
            "filterable",
            "Filterable"));

    /// <summary>
    /// The published names of a resource's filterable fields, and what each is stored as.
    /// </summary>
    public static IReadOnlyDictionary<string, string> FilterableOf(Type resource) => Filterable.GetOrAdd(resource, ReadFilterable);

    /// <summary>
    /// A property of the entity by name, as the convention finds it: the same name, any case.
    /// </summary>
    public static PropertyInfo? Named(Type entity, string name) =>
        entity.GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

    private static Dictionary<string, PropertyInfo> Resolve(
        Type resource,
        Type entity,
        IReadOnlyDictionary<string, string> fields,
        string kind,
        string attribute)
    {
        var resolved = new Dictionary<string, PropertyInfo>(StringComparer.OrdinalIgnoreCase);
        var missing = new List<string>();

        foreach (var (published, stored) in fields)
        {
            if (Named(entity, stored) is { } property)
            {
                resolved[published] = property;
            }
            else
            {
                missing.Add($"'{published}' (looked for '{entity.Name}.{stored}')");
            }
        }

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"'{resource.Name}' declares {kind} fields that '{entity.Name}' has no property for: "
                + $"{string.Join(", ", missing)}. Where a field is stored under another name, say so on "
                + $"the resource: [{attribute}(MappedTo = nameof({entity.Name}.ThePropertyName))].");
        }

        return resolved;
    }

    private static IReadOnlyDictionary<string, string> ReadFilterable(Type resource)
    {
        var properties = resource
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .ToDictionary(property => property.Name, StringComparer.Ordinal);
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var property in properties.Values)
        {
            foreach (var declared in property.GetCustomAttributes<FilterableAttribute>())
            {
                if (declared.Field is not null)
                {
                    throw new InvalidOperationException(
                        $"'{resource.Name}.{property.Name}' is marked [Filterable(\"{declared.Field}\")]. "
                        + "A property marks itself filterable; only the resource may name a field.");
                }

                Add(resource, fields, property, declared);
            }
        }

        foreach (var declared in resource.GetCustomAttributes<FilterableAttribute>())
        {
            if (declared.Field is null || !properties.TryGetValue(declared.Field, out var property))
            {
                throw new InvalidOperationException(
                    $"'{resource.Name}' is marked [Filterable] without naming one of its properties. On a "
                    + "resource the attribute names an inherited property, as in [Filterable(nameof(CreatedAt))].");
            }

            Add(resource, fields, property, declared);
        }

        return fields;
    }

    private static void Add(Type resource, Dictionary<string, string> fields, PropertyInfo property, FilterableAttribute declared)
    {
        if (!fields.TryAdd(Published(property), declared.MappedTo ?? property.Name))
        {
            throw new InvalidOperationException(
                $"'{resource.Name}' declares '{Published(property)}' filterable more than once.");
        }
    }

    /// <summary>
    /// The name a caller sends: an explicit JSON name, or the property's in camelCase.
    /// </summary>
    public static string Published(PropertyInfo property) =>
        property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name
        ?? JsonNamingPolicy.CamelCase.ConvertName(property.Name);
}
