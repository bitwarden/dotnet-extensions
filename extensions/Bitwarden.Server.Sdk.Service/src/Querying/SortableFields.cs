using System.Collections.Concurrent;
using System.Reflection;
using Bitwarden.Server.Sdk.Service.JsonApi;

namespace Bitwarden.Server.Sdk.Service.Querying;

/// <summary>
/// Reads a resource's sorting contract from its <see cref="SortableAttribute"/> declarations.
/// </summary>
/// <remarks>
/// One reading, shared by the two things that must agree: the document transformer that
/// publishes which fields a caller may name, and the model binder that refuses the ones it may
/// not. A validator that rejects what the documentation offers is worse than no validator, so
/// neither is allowed its own copy of the answer.
/// </remarks>
internal static class SortableFields
{
    private static readonly ConcurrentDictionary<Type, IReadOnlyList<SortableField>> Cache = new();

    /// <summary>
    /// The fields a caller may sort <paramref name="resource"/> by, alphabetically.
    /// </summary>
    /// <remarks>
    /// Empty when the resource declares none, which means the operation does not sort.
    /// </remarks>
    public static IReadOnlyList<SortableField> Of(Type resource) => Cache.GetOrAdd(resource, Read);

    /// <summary>
    /// The order a resource is returned in when the caller does not ask.
    /// </summary>
    /// <exception cref="InvalidOperationException">The resource declares no sortable fields.</exception>
    public static Sort DefaultOf(Type resource)
    {
        var declared = Of(resource).SingleOrDefault(field => field.Default)
            ?? throw new InvalidOperationException(
                $"'{resource.Name}' declares no sortable fields, so it has no default sort. "
                + "Mark one of its fields [Sortable(Default = true)].");

        return Sort.For(resource, [new SortBy(declared.Name, declared.Direction)]);
    }

    /// <summary>
    /// The resource an action returns, unwrapping the task and the document around it.
    /// </summary>
    /// <param name="returnType">The action's declared return type.</param>
    /// <returns>The resource, or null if the return type does not carry one.</returns>
    public static Type? ResourceOf(Type returnType)
    {
        var type = returnType;

        if (type.IsGenericType
            && (type.GetGenericTypeDefinition() == typeof(Task<>)
                || type.GetGenericTypeDefinition() == typeof(ValueTask<>)))
        {
            type = type.GetGenericArguments()[0];
        }

        if (!type.IsGenericType)
        {
            return null;
        }

        var definition = type.GetGenericTypeDefinition();

        return definition == typeof(PagedResourceListDocument<>)
               || definition == typeof(CursorPagedResourceListDocument<>)
               || definition == typeof(ResourceListDocument<>)
               || definition == typeof(ResourceDocument<>)
            ? type.GetGenericArguments()[0]
            : null;
    }

    private static IReadOnlyList<SortableField> Read(Type resource)
    {
        var properties = resource
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .ToDictionary(property => property.Name, StringComparer.Ordinal);

        var fields = new List<SortableField>();

        foreach (var property in properties.Values)
        {
            foreach (var declared in property.GetCustomAttributes<SortableAttribute>())
            {
                if (declared.Field is not null)
                {
                    throw new InvalidOperationException(
                        $"'{resource.Name}.{property.Name}' is marked [Sortable(\"{declared.Field}\")]. "
                        + "A property marks itself sortable; only the resource may name a field.");
                }

                fields.Add(Field(property, declared));
            }
        }

        // On the resource, naming a field it inherits. Read after the properties' own so that a
        // resource cannot quietly restate one of its own fields under a different declaration.
        foreach (var declared in resource.GetCustomAttributes<SortableAttribute>())
        {
            if (declared.Field is null)
            {
                throw new InvalidOperationException(
                    $"'{resource.Name}' is marked [Sortable] without naming a field. On a resource "
                    + "the attribute names an inherited property, as in [Sortable(nameof(CreatedAt))].");
            }

            if (!properties.TryGetValue(declared.Field, out var property))
            {
                throw new InvalidOperationException(
                    $"'{resource.Name}' is marked [Sortable(\"{declared.Field}\")], but has no such property.");
            }

            fields.Add(Field(property, declared));
        }

        if (fields.Count == 0)
        {
            return [];
        }

        var duplicated = fields
            .GroupBy(field => field.Name, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicated is not null)
        {
            throw new InvalidOperationException(
                $"'{resource.Name}' declares '{duplicated.Key}' sortable more than once.");
        }

        var defaults = fields.Count(field => field.Default);

        if (defaults != 1)
        {
            throw new InvalidOperationException(
                $"'{resource.Name}' marks {defaults} of its sortable fields [Sortable(Default = true)]; "
                + "exactly one must be. A collection is returned in some order whether the caller "
                + "chose it or not, and a page is only meaningful against an order the caller knows.");
        }

        return [.. fields.OrderBy(field => field.Name, StringComparer.Ordinal)];
    }

    private static SortableField Field(PropertyInfo property, SortableAttribute declared) =>
        new(
            // The published name, which is what a caller sends and what the schema shows: an
            // explicit JSON name where the resource gives one, else the property's in camelCase.
            StoredFields.Published(property),
            property,
            declared.Default,
            declared.Descending ? SortDirection.Descending : SortDirection.Ascending,
            declared.MappedTo ?? property.Name);
}
