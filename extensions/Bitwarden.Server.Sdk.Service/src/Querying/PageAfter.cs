using System.Reflection;
using Microsoft.AspNetCore.Http;

namespace Bitwarden.Server.Sdk.Service.Querying;

/// <summary>
/// The <c>page[after]</c> query parameter: the cursor the previous page handed out.
/// </summary>
/// <remarks>
/// <c>default</c> is no cursor, which is the first page.
/// </remarks>
public readonly record struct PageAfter
{
    /// <summary>
    /// The query parameter it is read from.
    /// </summary>
    public const string Parameter = InvalidCursorException.Parameter;

    /// <summary>
    /// A cursor, as the caller sent it.
    /// </summary>
    /// <param name="value">The cursor; checked when the query reads it.</param>
    public PageAfter(string? value) => Value = value;

    /// <summary>
    /// The cursor, or null for the first page.
    /// </summary>
    public string? Value { get; }

    /// <summary>
    /// The cursor, for building a <see cref="CursorPage"/>.
    /// </summary>
    public static implicit operator string?(PageAfter after) => after.Value;

    /// <inheritdoc />
    public override string ToString() => Value ?? string.Empty;

    /// <summary>
    /// Binds a handler's parameter.
    /// </summary>
    /// <param name="context">The request.</param>
    /// <param name="parameter">The handler's parameter.</param>
    /// <returns>The parameter.</returns>
    public static ValueTask<PageAfter> BindAsync(HttpContext context, ParameterInfo parameter) =>
        ValueTask.FromResult((PageAfter)PageBinding.Bind(typeof(PageAfter), context.Request.Query, declaredSize: null));
}
