using System.Reflection;
using Microsoft.AspNetCore.Http;

namespace Bitwarden.Server.Sdk.Service.Querying;

/// <summary>
/// The <c>page[size]</c> query parameter.
/// </summary>
/// <remarks>
/// <para>
/// <c>default</c> is <see cref="Page.DefaultSize"/>. An operation whose collection is read a different
/// number at a time by default says so on the parameter, and the documentation publishes
/// that instead:
/// </para>
/// <code>
/// [DefaultValue(10)] PageSize pageSize = default
/// </code>
/// <para>
/// Shared by both paging styles; the rules on what a size may be are <see cref="Page"/>'s and
/// <see cref="CursorPage"/>'s, applied when one is built from this.
/// </para>
/// </remarks>
public readonly record struct PageSize
{
    /// <summary>
    /// The query parameter it is read from.
    /// </summary>
    public const string Parameter = "page[size]";

    private readonly int? _value;

    /// <summary>
    /// A page size, as the caller sent it.
    /// </summary>
    /// <param name="value">The size; checked when a page is built from it.</param>
    public PageSize(int value) => _value = value;

    /// <summary>
    /// The size, or <see cref="Page.DefaultSize"/> when neither the caller nor the operation said.
    /// </summary>
    public int Value => _value ?? Page.DefaultSize;

    /// <summary>
    /// The size, for building a <see cref="Page"/> or <see cref="CursorPage"/>.
    /// </summary>
    public static implicit operator int(PageSize size) => size.Value;

    /// <inheritdoc />
    public override string ToString() => Value.ToString();

    /// <summary>
    /// Binds a handler's parameter.
    /// </summary>
    /// <param name="context">The request.</param>
    /// <param name="parameter">The handler's parameter.</param>
    /// <returns>The parameter.</returns>
    /// <remarks>
    /// An operation's own default size is the one its parameter declares with <c>[DefaultValue]</c>.
    /// </remarks>
    public static ValueTask<PageSize> BindAsync(HttpContext context, ParameterInfo parameter) =>
        ValueTask.FromResult((PageSize)PageBinding.Bind(
            typeof(PageSize),
            context.Request.Query,
            parameter?.GetCustomAttributes<System.ComponentModel.DefaultValueAttribute>()
                .Select(attribute => attribute.Value).OfType<int>().Cast<int?>().FirstOrDefault()));
}
