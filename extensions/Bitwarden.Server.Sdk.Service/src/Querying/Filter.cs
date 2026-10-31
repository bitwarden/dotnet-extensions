using System.Reflection;
using Microsoft.AspNetCore.Http;

namespace Bitwarden.Server.Sdk.Service.Querying;

/// <summary>
/// A filter a caller applied, bound from <c>filter[…]</c>.
/// </summary>
/// <typeparam name="T">
/// What the filter carries. A collection type (<c>List&lt;Guid&gt;</c>, say) accepts repeated
/// parameters and comma-delimited lists in any combination; anything else accepts one value.
/// </typeparam>
/// <remarks>
/// <para>
/// One parameter per filter an operation supports, named for the filter:
/// </para>
/// <code>
/// public async Task&lt;PagedResourceListDocument&lt;Member&gt;&gt; ListMembers(
///     Filter&lt;List&lt;Guid&gt;&gt;? id,
///     PageNumber pageNumber = default,
///     PageSize pageSize = default,
///     CancellationToken cancellationToken = default)
/// </code>
/// <para>
/// The parameter's name is the filter's name, its type is what the filter accepts, and its
/// nullability says whether it is optional, so an operation declares its filters the same way
/// it declares anything else, documents them with ordinary <c>&lt;param&gt;</c> comments, and
/// the platform publishes them from the signature.
/// </para>
/// <para>
/// A filter no parameter declares is refused rather than ignored. A caller that misspells one
/// and is quietly given the unfiltered collection has been handed more than it asked for and no
/// way to notice.
/// </para>
/// </remarks>
/// <param name="value">What the caller supplied.</param>
public sealed class Filter<T>(T value)
{
    /// <summary>
    /// What the caller supplied.
    /// </summary>
    public T Value { get; } = value;

    /// <summary>
    /// Reads the value, so a filter can be used where its type is expected.
    /// </summary>
    /// <param name="filter">The filter.</param>
    public static implicit operator T(Filter<T> filter) => filter.Value;

    /// <inheritdoc />
    public override string ToString() => Value?.ToString() ?? string.Empty;

    /// <summary>
    /// Binds a handler's parameter.
    /// </summary>
    /// <param name="context">The request.</param>
    /// <param name="parameter">The handler's parameter.</param>
    /// <returns>The parameter.</returns>
    /// <remarks>
    /// The parameter's name is the filter's: <c>id</c> reads <c>filter[id]</c>.
    /// </remarks>
    public static ValueTask<Filter<T>?> BindAsync(HttpContext context, ParameterInfo parameter) =>
        ValueTask.FromResult((Filter<T>?)FilterBinding.Bind(typeof(Filter<T>), parameter.Name ?? string.Empty, context.Request.Query));
}
