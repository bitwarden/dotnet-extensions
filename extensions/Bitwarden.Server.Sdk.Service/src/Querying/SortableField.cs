using System.Reflection;

namespace Bitwarden.Server.Sdk.Service.Querying;

/// <summary>
/// A field a caller may sort by, and the property behind it.
/// </summary>
/// <param name="Name">The field's published name, as it appears in the resource's schema.</param>
/// <param name="Property">The property it was declared on.</param>
/// <param name="Default">Whether this is the order used when the caller does not ask.</param>
/// <param name="Direction">Which way the default order runs.</param>
/// <param name="Stored">The entity property it is stored as: <c>MappedTo</c>, or the property's own name.</param>
internal sealed record SortableField(
    string Name,
    PropertyInfo Property,
    bool Default,
    SortDirection Direction,
    string Stored);
