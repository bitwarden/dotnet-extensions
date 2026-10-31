using System.Reflection;
using Microsoft.AspNetCore.Http;

namespace Bitwarden.Server.Sdk.Service.Querying;

/// <summary>
/// The <c>page[number]</c> query parameter.
/// </summary>
/// <remarks>
/// <para>
/// An operation that pages declares it by type, and the type carries the name the standards give
/// it, so no operation spells out a bracketed name:
/// </para>
/// <code>
/// public async Task&lt;PagedResourceListDocument&lt;User&gt;&gt; ListUsers(
///     PageNumber pageNumber = default,
///     PageSize pageSize = default,
///     CancellationToken cancellationToken = default)
/// {
///     var page = new Page { Number = pageNumber, Size = pageSize };
/// </code>
/// <para>
/// <c>default</c> is the first page. C# allows nothing but a constant, <c>new()</c> or
/// <c>default</c> as a parameter's default, so the value a caller gets when they send nothing is
/// the type's own, and the documentation publishes it. The rules on what a page may be are
/// <see cref="Page"/>'s, applied when one is built from this.
/// </para>
/// </remarks>
public readonly record struct PageNumber
{
    /// <summary>
    /// The query parameter it is read from.
    /// </summary>
    public const string Parameter = "page[number]";

    /// <summary>
    /// The page a caller gets when they do not say.
    /// </summary>
    public const int Default = 1;

    private readonly int? _value;

    /// <summary>
    /// A page number, as the caller sent it.
    /// </summary>
    /// <param name="value">The number; checked when a <see cref="Page"/> is built from it.</param>
    public PageNumber(int value) => _value = value;

    /// <summary>
    /// The number, or <see cref="Default"/> when the caller sent none.
    /// </summary>
    public int Value => _value ?? Default;

    /// <summary>
    /// The number, for building a <see cref="Page"/>.
    /// </summary>
    public static implicit operator int(PageNumber number) => number.Value;

    /// <inheritdoc />
    public override string ToString() => Value.ToString();

    /// <summary>
    /// Binds a handler's parameter.
    /// </summary>
    /// <param name="context">The request.</param>
    /// <param name="parameter">The handler's parameter.</param>
    /// <returns>The parameter.</returns>
    public static ValueTask<PageNumber> BindAsync(HttpContext context, ParameterInfo parameter) =>
        ValueTask.FromResult((PageNumber)PageBinding.Bind(typeof(PageNumber), context.Request.Query, declaredSize: null));
}
