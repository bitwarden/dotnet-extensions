using System.ComponentModel;
using System.Globalization;
using System.Reflection;

namespace Bitwarden.Server.Sdk.Resources.Querying;

/// <summary>
/// Reads <see cref="PageNumber"/>, <see cref="PageSize"/> and <see cref="PageAfter"/>, for their
/// <c>BindAsync</c>.
/// </summary>
internal static class PageBinding
{
    /// <summary>
    /// The page parameter the query asks for, or its default.
    /// </summary>
    /// <param name="type">Which of the three.</param>
    /// <param name="query">The request's query string.</param>
    /// <param name="parameter">
    /// The handler's parameter, if any: a <see cref="PageSize"/>'s <see cref="DefaultValueAttribute"/> and
    /// <see cref="MaximumPageSizeAttribute"/> are read from it, and a <see cref="PageNumber"/>'s farthest page from
    /// the <see cref="MaximumPageSizeAttribute"/> of the <see cref="PageSize"/> beside it.
    /// </param>
    /// <returns>The parameter.</returns>
    /// <exception cref="InvalidQueryException">A number that is not a whole number, or a page number past the farthest page.</exception>
    public static object Bind(Type type, Microsoft.AspNetCore.Http.IQueryCollection query, ParameterInfo? parameter)
    {
        var declaredSize = type == typeof(PageSize)
            ? parameter?.GetCustomAttributes<DefaultValueAttribute>().Select(attribute => attribute.Value).OfType<int>().Cast<int?>().FirstOrDefault()
            : null;
        var maximum = type == typeof(PageSize)
            ? MaximumPageSizeAttribute.Of(parameter)
            : MaximumPageSizeAttribute.OfSibling(parameter);

        var name = type == typeof(PageNumber) ? PageNumber.Parameter
            : type == typeof(PageSize) ? PageSize.Parameter
            : PageAfter.Parameter;

        var supplied = query[name];

        if (supplied.Count == 0 || string.IsNullOrEmpty(supplied[^1]))
        {
            return type == typeof(PageSize)
                ? new PageSize(declaredSize, maximum)
                : Activator.CreateInstance(type)!;
        }

        // The last value, as the platform's other single-valued parameters take it.
        var value = supplied[^1]!;

        if (type == typeof(PageAfter))
        {
            return new PageAfter(value);
        }

        if (type == typeof(PageNumber))
        {
            return new PageNumber(Number(value, maximum));
        }

        return new PageSize(Whole(name, value), maximum);
    }

    /// <remarks>
    /// Refused here as well as where a <see cref="Page"/> is built, so a number no page could be
    /// built from never reaches the handler.
    /// </remarks>
    private static int Number(string value, int maximum) =>
        Whole(PageNumber.Parameter, value) is var number && number > Page.FarthestNumber(maximum)
            ? throw Page.PastTheLast(value, maximum, tooFar: true)
            : number;

    private static int Whole(string name, string value) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : throw new InvalidQueryException(name, value);
}
