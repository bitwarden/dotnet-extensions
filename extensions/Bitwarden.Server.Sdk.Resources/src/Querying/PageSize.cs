using System.Reflection;
using Microsoft.AspNetCore.Http;

namespace Bitwarden.Server.Sdk.Resources.Querying;

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
/// Likewise, an operation whose pages may hold more than <see cref="Page.MaximumSize"/> declares its
/// own maximum with <see cref="MaximumPageSizeAttribute"/>, which binding carries in <see cref="Maximum"/>.
/// </para>
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
    private readonly int? _maximum;

    /// <summary>
    /// A page size, as the caller sent it.
    /// </summary>
    /// <param name="value">The size; checked when a page is built from it.</param>
    public PageSize(int value) => _value = value;

    /// <summary>
    /// A page size, as the caller sent it, for an operation whose pages may hold up to <paramref name="maximum"/>.
    /// </summary>
    /// <param name="value">The size; checked when a page is built from it.</param>
    /// <param name="maximum">The most the operation's page may hold, as its <see cref="MaximumPageSizeAttribute"/> says.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maximum"/> is less than one.</exception>
    public PageSize(int value, int maximum)
        : this((int?)value, maximum)
    {
    }

    /// <summary>
    /// A size the caller may not have sent, so that binding nothing to an operation that declares
    /// nothing is <c>default</c>.
    /// </summary>
    internal PageSize(int? value, int maximum)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximum, 1);
        _value = value;
        _maximum = maximum == Page.MaximumSize ? null : maximum;
    }

    /// <summary>
    /// The size, or <see cref="Page.DefaultSize"/> when neither the caller nor the operation said.
    /// </summary>
    public int Value => _value ?? Page.DefaultSize;

    /// <summary>
    /// The most the operation's page may hold: its <see cref="MaximumPageSizeAttribute"/>, or <see cref="Page.MaximumSize"/>.
    /// </summary>
    /// <remarks>
    /// Applied by <see cref="Page(PageNumber, PageSize)"/> and <see cref="CursorPage(PageSize)"/>; a page built
    /// from the size alone, as an <see cref="int"/>, holds at most <see cref="Page.MaximumSize"/>.
    /// </remarks>
    public int Maximum => _maximum ?? Page.MaximumSize;

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
    /// An operation's own default size is the one its parameter declares with <c>[DefaultValue]</c>, and its own
    /// maximum the one it declares with <see cref="MaximumPageSizeAttribute"/>.
    /// </remarks>
    public static ValueTask<PageSize> BindAsync(HttpContext context, ParameterInfo parameter) =>
        ValueTask.FromResult((PageSize)PageBinding.Bind(typeof(PageSize), context.Request.Query, parameter));
}
