using System.Reflection;
using Bitwarden.Server.Sdk.Service.JsonApi;
using Microsoft.AspNetCore.Http;

namespace Bitwarden.Server.Sdk.Service.Querying;

/// <summary>
/// The order a caller asked a collection to be returned in, bound from <c>sort</c>.
/// </summary>
/// <remarks>
/// <para>
/// One parameter, optional, on any read-many operation that lets a caller choose an order:
/// </para>
/// <code>
/// public async Task&lt;PagedResourceListDocument&lt;User&gt;&gt; ListUsers(
///     Sort? sort,
///     PageNumber pageNumber = default,
///     PageSize pageSize = default,
///     CancellationToken cancellationToken = default)
/// </code>
/// <para>
/// Which fields a caller may name is not declared here. It is read from the resource the
/// operation returns, where each field a caller may sort on carries
/// <see cref="SortableAttribute"/>, so the contract is declared once, beside the field, and
/// both the published documentation and the request-time validation are derived from it.
/// </para>
/// <para>
/// A caller writes a comma-delimited list in order of precedence, a leading <c>-</c> meaning
/// descending: <c>sort=-createdAt,email</c>. Anything the resource does not declare sortable is
/// refused rather than ignored, because a caller whose misspelled field is silently dropped is
/// handed an arbitrary order and told it is the one they asked for.
/// </para>
/// <para>
/// Null means the caller did not ask. It does not mean there is no order: every sortable
/// resource names a default, which <see cref="Default{T}"/> returns, so a query resolves the
/// two with <c>sort ?? Sort.Default&lt;User&gt;()</c> and always has one to apply.
/// </para>
/// </remarks>
public sealed class Sort
{
    /// <summary>
    /// The query parameter a sort is read from.
    /// </summary>
    public const string Parameter = "sort";

    private Sort(IReadOnlyList<SortBy> fields) => Fields = fields;

    /// <summary>
    /// The fields to order by, in order of precedence.
    /// </summary>
    public IReadOnlyList<SortBy> Fields { get; }

    /// <summary>
    /// The resource whose fields these are, when the sort was bound from a request or taken as a
    /// resource's default. What lets the query find where each field is stored.
    /// </summary>
    /// <remarks>
    /// Absent on a sort built by hand with <see cref="By"/>, whose fields are taken as the entity's
    /// own property names.
    /// </remarks>
    internal Type? Resource { get; private init; }

    /// <summary>
    /// A sort over a resource's fields.
    /// </summary>
    internal static Sort For(Type resource, IReadOnlyList<SortBy> fields) =>
        new(fields) { Resource = resource };

    /// <summary>
    /// Builds a sort from fields.
    /// </summary>
    /// <param name="fields">The fields to order by, in order of precedence.</param>
    /// <exception cref="ArgumentException">No fields were given.</exception>
    public static Sort By(params SortBy[] fields)
    {
        ArgumentNullException.ThrowIfNull(fields);

        if (fields.Length == 0)
        {
            throw new ArgumentException("A sort must order by at least one field.", nameof(fields));
        }

        return new Sort(fields);
    }

    /// <summary>
    /// The order a resource is returned in when the caller does not ask for one.
    /// </summary>
    /// <typeparam name="T">The resource.</typeparam>
    /// <remarks>
    /// Read from the field the resource marks <c>[Sortable(Default = true)]</c>. Declared on the
    /// resource rather than on each operation so that two operations returning the same resource
    /// cannot disagree about its natural order.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The resource declares no sortable fields.</exception>
    public static Sort Default<T>() where T : Resource => SortableFields.DefaultOf(typeof(T));

    /// <summary>
    /// Renders the sort the way a caller writes it: <c>-createdAt,email</c>.
    /// </summary>
    public override string ToString() => string.Join(',', Fields);

    /// <summary>
    /// Binds a handler's parameter.
    /// </summary>
    /// <param name="context">The request.</param>
    /// <param name="parameter">The handler's parameter.</param>
    /// <returns>The sort, or null when the caller chose none.</returns>
    /// <remarks>
    /// What may be sorted by is read from the resource the handler returns.
    /// </remarks>
    public static ValueTask<Sort?> BindAsync(HttpContext context, ParameterInfo parameter) =>
        ValueTask.FromResult(SortBinding.Bind(
            context.Request.Query,
            parameter.Member is MethodInfo method ? SortableFields.ResourceOf(method.ReturnType) : null));
}
