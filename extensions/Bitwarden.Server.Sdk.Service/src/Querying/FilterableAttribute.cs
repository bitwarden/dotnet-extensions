namespace Bitwarden.Server.Sdk.Service.Querying;

/// <summary>
/// Marks a field an expression may name: an advanced search, a bulk update or a bulk delete.
/// </summary>
/// <remarks>
/// <para>
/// Only where a caller writes an expression over fields. An ordinary list operation takes discrete
/// <c>filter[x]</c> parameters, each translated to the query by hand, and needs nothing here. An
/// expression can name any field in any combination, so which fields it may name is part of the
/// contract, declared beside the field, and a request naming another is refused. A resource with
/// no filterable field cannot be searched or changed in bulk.
/// </para>
/// <para>
/// As with <see cref="SortableAttribute"/>, a field is stored as the entity property of the same
/// name unless <see cref="MappedTo"/> says otherwise, and the first expression applied to the
/// resource checks that every filterable field resolves.
/// </para>
/// <para>
/// On the resource itself rather than on a property, the attribute names an inherited field,
/// such as <c>CreatedAt</c>.
/// </para>
/// </remarks>
/// <param name="field">
/// The inherited property this marks filterable. Only when the attribute is on the resource.
/// </param>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Class, AllowMultiple = true)]
public sealed class FilterableAttribute(string? field = null) : Attribute
{
    /// <summary>
    /// The inherited property this marks filterable, when on the resource.
    /// </summary>
    public string? Field { get; } = field;

    /// <summary>
    /// The entity property this field is stored as, when it is not the property of the same name.
    /// Written with <c>nameof</c>, so renaming the property stops this compiling.
    /// </summary>
    public string? MappedTo { get; init; }
}
