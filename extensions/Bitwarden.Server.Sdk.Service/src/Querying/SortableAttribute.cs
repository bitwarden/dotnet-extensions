using Bitwarden.Server.Sdk.Service.JsonApi;

namespace Bitwarden.Server.Sdk.Service.Querying;

/// <summary>
/// Marks a field a caller may sort a collection by.
/// </summary>
/// <remarks>
/// <para>
/// The whole sorting contract, declared beside the field it concerns. The published
/// documentation lists these fields and nothing else, and a request naming anything else is
/// refused, so the attribute is not a hint, it is the contract, and there is no second place
/// for it to drift out of step with.
/// </para>
/// <para>
/// Exactly one sortable field must set <see cref="Default"/>. A collection has an order whether
/// or not the caller chose it, and a paged collection whose order is unstated cannot be paged
/// through reliably: the same row can appear on two pages, or on none. Requiring the default to
/// be named makes that order part of the contract rather than an accident of the query.
/// </para>
/// <para>
/// On the resource itself rather than on a property, the attribute names an inherited field.
/// The audit timestamps every resource carries are declared on <see cref="Resource"/>, so a
/// service cannot attribute them where they are written, and ordering a collection by when its
/// resources were created is the most ordinary sort there is.
/// </para>
/// <example>
/// <code>
/// [Sortable(nameof(CreatedAt), Default = true)]
/// public sealed record User : Resource
/// {
///     [Sortable]
///     public string Email { get; init; }
/// }
/// </code>
/// </example>
/// </remarks>
/// <param name="field">
/// The inherited property this marks sortable. Only when the attribute is on the resource: a
/// property marks itself, and naming a second field there is an error.
/// </param>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Class, AllowMultiple = true)]
public sealed class SortableAttribute(string? field = null) : Attribute
{
    /// <summary>
    /// The inherited property this marks sortable, when declared on the resource.
    /// </summary>
    public string? Field { get; } = field;

    /// <summary>
    /// Whether this is the field a collection is ordered by when the caller does not ask.
    /// </summary>
    public bool Default { get; init; }

    /// <summary>
    /// Whether the default order runs largest first.
    /// </summary>
    /// <remarks>
    /// Only meaningful alongside <see cref="Default"/>: it says which way the <em>unasked-for</em>
    /// order runs. A caller who asks chooses the direction themselves, with a leading <c>-</c>.
    /// </remarks>
    public bool Descending { get; init; }

    /// <summary>
    /// The entity property this field is stored as, when it is not the property of the same name.
    /// </summary>
    /// <remarks>
    /// Written with <c>nameof</c> (<c>MappedTo = nameof(UserEntity.OrganizationId)</c>) so
    /// that renaming the entity's property stops this compiling rather than stops the sort working.
    /// A field whose name matches its property needs nothing; one that does not must say so here,
    /// and the first query sorted by the resource checks that every sortable field resolves.
    /// </remarks>
    public string? MappedTo { get; init; }
}
