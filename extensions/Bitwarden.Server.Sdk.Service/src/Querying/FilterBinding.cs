using System.ComponentModel;
using System.Globalization;
using Microsoft.AspNetCore.Http;

namespace Bitwarden.Server.Sdk.Service.Querying;

/// <summary>
/// Reads a <c>Filter&lt;T&gt;</c> from the query parameter named after it, for its
/// <c>BindAsync</c>.
/// </summary>
internal static class FilterBinding
{
    /// <summary>
    /// The filter the query asks for, or null when it asks for none.
    /// </summary>
    /// <param name="modelType">The parameter's type, a <c>Filter&lt;T&gt;</c>.</param>
    /// <param name="name">The parameter's name: <c>id</c> reads <c>filter[id]</c>.</param>
    /// <param name="query">The request's query string.</param>
    /// <returns>The filter, or null when the parameter is absent.</returns>
    /// <exception cref="InvalidFilterException">The value cannot be read as the filter.</exception>
    public static object? Bind(Type modelType, string name, IQueryCollection query)
    {
        var parameter = $"filter[{name}]";
        var shape = FilterShape.Of(modelType)
            ?? throw new InvalidOperationException(
                $"'{name}' is bound as a filter but is not a Filter<T>.");

        var supplied = query[parameter];

        if (supplied.Count == 0)
        {
            return null;
        }

        if (shape.Range)
        {
            return Range(shape, Single(supplied, parameter), parameter);
        }

        if (!shape.Many)
        {
            // Never split. The standards are explicit that a single-value parameter's value is
            // taken whole, so a comma in it is part of the value and needs no escaping.
            return One(shape, Single(supplied, parameter).Trim(), parameter);
        }

        // Both conventions, in any combination: repeated parameters and comma-delimited lists.
        // The standards require a service to accept either, because a caller assembling a
        // filter from a set should not have to know which one we prefer. A comma that is part of
        // a value arrives escaped, and is kept.
        var raw = new List<string>();

        foreach (var value in supplied)
        {
            if (!FilterSyntax.TrySplit(value ?? string.Empty, out var parts))
            {
                throw InvalidFilterException.Because(
                    parameter,
                    $"'{parameter}' ends in a backslash that escapes nothing: '{value}'. A backslash in a value is written \\\\.");
            }

            raw.AddRange(parts.Where(part => part.Length > 0));
        }

        return Many(shape, modelType.GetGenericArguments()[0], [.. raw]);
    }

    /// <remarks>
    /// A filter that takes one value, sent twice, has been asked two questions at once; answering
    /// either would be guessing which was meant.
    /// </remarks>
    private static string Single(Microsoft.Extensions.Primitives.StringValues supplied, string parameter) =>
        supplied.Count == 1
            ? supplied[0] ?? string.Empty
            : throw new InvalidFilterException(parameter, string.Join(',', supplied!), "accepts one value");

    /// <remarks>
    /// Refused whenever it cannot be read exactly. A range is a question about a span, and
    /// answering a different span than the one asked, or none, is worse than saying why not.
    /// </remarks>
    private static object Range(FilterShape shape, string raw, string parameter)
    {
        var expected = $"expects a range such as [begin,end), with {Describe(shape.Element)} bounds or * for no limit";

        if (!FilterSyntax.TryRange(raw, out var bounds))
        {
            throw new InvalidFilterException(parameter, raw, expected);
        }

        object? lower = null;
        object? upper = null;

        if ((bounds.Lower is { } from && !TryConvert(shape.Element, from, out lower))
            || (bounds.Upper is { } to && !TryConvert(shape.Element, to, out upper)))
        {
            throw new InvalidFilterException(parameter, raw, expected);
        }

        try
        {
            return Activator.CreateInstance(
                typeof(Filter<>).MakeGenericType(typeof(Range<>).MakeGenericType(shape.Element)),
                Activator.CreateInstance(
                    typeof(Range<>).MakeGenericType(shape.Element),
                    lower,
                    bounds.LowerInclusive,
                    upper,
                    bounds.UpperInclusive))!;
        }
        catch (System.Reflection.TargetInvocationException invalid) when (invalid.InnerException is ArgumentException reason)
        {
            throw InvalidFilterException.Because(
                parameter,
                $"'{parameter}' is '{raw}', which describes no values: {reason.Message}");
        }
    }

    /// <remarks>
    /// <para>
    /// Values that will not parse are dropped, not refused. A list filter is a set to intersect
    /// with, and the standards have unresolved identifiers absent from the result rather than
    /// reported, which is what lets a caller establish which of a set of references are valid
    /// in one request. A value of the wrong shape is unresolvable in the same way.
    /// </para>
    /// <para>
    /// Built as the collection the handler declared: an array for an array, and a list for a list
    /// or any of the interfaces a list implements.
    /// </para>
    /// </remarks>
    private static object Many(FilterShape shape, Type declared, string[] raw)
    {
        var list = (System.Collections.IList)Activator.CreateInstance(
            typeof(List<>).MakeGenericType(shape.Element))!;

        foreach (var value in raw)
        {
            if (TryConvert(shape.Element, value, out var converted))
            {
                list.Add(converted);
            }
        }

        object values = list;

        if (declared.IsArray)
        {
            var array = Array.CreateInstance(shape.Element, list.Count);
            list.CopyTo(array, 0);
            values = array;
        }

        return Activator.CreateInstance(typeof(Filter<>).MakeGenericType(declared), values)!;
    }

    /// <remarks>
    /// Refused rather than dropped, unlike a list. A caller filtering on one value has asked a
    /// question about that value; answering as though they had not asked would be a different
    /// question.
    /// </remarks>
    private static object One(FilterShape shape, string raw, string parameter)
    {
        if (!TryConvert(shape.Element, raw, out var converted))
        {
            // Taken whole, so a comma is part of the value, but a value that will not convert
            // and has one in it is far more likely two values sent to a filter that takes one,
            // and saying so is the answer the caller can act on.
            throw raw.Contains(',', StringComparison.Ordinal)
                ? new InvalidFilterException(parameter, raw, "accepts one value")
                : new InvalidFilterException(parameter, raw, $"expects {WithArticle(Describe(shape.Element))}");
        }

        return Activator.CreateInstance(typeof(Filter<>).MakeGenericType(shape.Element), converted)!;
    }

    private static bool TryConvert(Type target, string value, out object? converted)
    {
        converted = null;

        // An enumeration travels as its published name, which is not its CLR name.
        if (target.IsEnum)
        {
            return EnumValues.TryParse(target, value, out converted);
        }

        try
        {
            converted = TypeDescriptor.GetConverter(target)
                .ConvertFromString(null, CultureInfo.InvariantCulture, value);

            return converted is not null;
        }
        catch (Exception exception) when (exception is FormatException or NotSupportedException or ArgumentException)
        {
            return false;
        }
    }

    private static string Describe(Type type) => type == typeof(Guid) ? "identifier" : type.Name.ToLowerInvariant();

    // By the described word's first letter, so it reads "an identifier", "an int32", "a string".
    private static string WithArticle(string described) =>
        ("aeiou".Contains(described[0], StringComparison.Ordinal) ? "an " : "a ") + described;
}
