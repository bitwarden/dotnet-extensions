
namespace Bitwarden.Server.Sdk.Service.Querying;

/// <summary>
/// What a <c>Filter&lt;T&gt;</c> parameter accepts, worked out from its type.
/// </summary>
/// <param name="Element">The type of a single value; for a range, of either bound.</param>
/// <param name="Many">Whether the filter takes a list rather than one value.</param>
/// <param name="Range">Whether the filter takes a range rather than values.</param>
internal readonly record struct FilterShape(Type Element, bool Many, bool Range = false)
{
    /// <summary>
    /// Describes a filter parameter, or null if the type is not a filter.
    /// </summary>
    /// <param name="parameterType">The declared parameter type.</param>
    public static FilterShape? Of(Type parameterType)
    {
        if (!parameterType.IsGenericType || parameterType.GetGenericTypeDefinition() != typeof(Filter<>))
        {
            return null;
        }

        var carried = parameterType.GetGenericArguments()[0];

        if (carried.IsArray)
        {
            return new FilterShape(carried.GetElementType()!, Many: true);
        }

        if (carried.IsGenericType && carried.GetGenericTypeDefinition() == typeof(Range<>))
        {
            return new FilterShape(carried.GetGenericArguments()[0], Many: false, Range: true);
        }

        if (carried.IsGenericType && Collections.Contains(carried.GetGenericTypeDefinition()))
        {
            return new FilterShape(carried.GetGenericArguments()[0], Many: true);
        }

        return new FilterShape(carried, Many: false);
    }

    private static readonly Type[] Collections =
    [
        typeof(List<>), typeof(IList<>), typeof(IReadOnlyList<>),
        typeof(ICollection<>), typeof(IReadOnlyCollection<>), typeof(IEnumerable<>),
    ];
}
