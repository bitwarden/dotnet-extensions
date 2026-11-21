using System.Reflection;

namespace Bitwarden.Server.Sdk.Resources.Querying;

/// <summary>
/// The most an operation's page may hold, when it is not <see cref="Page.MaximumSize"/>.
/// </summary>
/// <remarks>
/// <para>
/// On the operation's <see cref="PageSize"/> parameter, beside any <c>[DefaultValue]</c>:
/// </para>
/// <code>
/// [MaximumPageSize(1000)] PageSize pageSize = default
/// </code>
/// <para>
/// For a collection a caller reads whole, such as the lists a sync reads, where every extra page
/// is another request. Binding carries the maximum on the <see cref="PageSize"/>, the page built
/// from it with <see cref="Page(PageNumber, PageSize)"/> or <see cref="CursorPage(PageSize)"/>
/// treats a larger size as the maximum, the farthest page a caller may ask for is the last that the
/// maximum can reach without overflowing, and the OpenAPI document publishes it.
/// </para>
/// </remarks>
/// <param name="size">The most a page may hold; at least one.</param>
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class MaximumPageSizeAttribute(int size) : Attribute
{
    /// <summary>
    /// The most a page may hold.
    /// </summary>
    public int Size { get; } = size >= 1
        ? size
        : throw new ArgumentOutOfRangeException(nameof(size), size, "A page must be allowed to hold at least one resource.");

    /// <summary>
    /// The maximum <paramref name="parameter"/> declares, or <see cref="Page.MaximumSize"/>.
    /// </summary>
    internal static int Of(ParameterInfo? parameter) =>
        parameter?.GetCustomAttribute<MaximumPageSizeAttribute>()?.Size ?? Page.MaximumSize;

    /// <summary>
    /// The maximum the <see cref="PageSize"/> parameter beside <paramref name="parameter"/> declares, or
    /// <see cref="Page.MaximumSize"/>: what a <see cref="PageNumber"/> is bounded by.
    /// </summary>
    internal static int OfSibling(ParameterInfo? parameter) =>
        Of((parameter?.Member as MethodBase)?.GetParameters()
            .FirstOrDefault(each => (Nullable.GetUnderlyingType(each.ParameterType) ?? each.ParameterType) == typeof(PageSize)));
}
