using System.ComponentModel;
using System.Globalization;

namespace Bitwarden.Server.Sdk.Service.Querying;

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
    /// <param name="declaredSize">The size the operation declared with <see cref="DefaultValueAttribute"/>, if any.</param>
    /// <returns>The parameter.</returns>
    /// <exception cref="InvalidQueryException">A number that is not a whole number, or a page number past the farthest page.</exception>
    public static object Bind(Type type, Microsoft.AspNetCore.Http.IQueryCollection query, int? declaredSize)
    {
        var name = type == typeof(PageNumber) ? PageNumber.Parameter
            : type == typeof(PageSize) ? PageSize.Parameter
            : PageAfter.Parameter;

        var supplied = query[name];

        if (supplied.Count == 0 || string.IsNullOrEmpty(supplied[^1]))
        {
            return type == typeof(PageSize) && declaredSize is { } size
                ? new PageSize(size)
                : Activator.CreateInstance(type)!;
        }

        // The last value, as the platform's other single-valued parameters take it.
        var value = supplied[^1]!;

        return type == typeof(PageAfter)
            ? new PageAfter(value)
            : type == typeof(PageNumber) ? new PageNumber(Number(value)) : new PageSize(Whole(name, value));
    }

    /// <remarks>
    /// Refused here as well as where a <see cref="Page"/> is built, so a number no page could be
    /// built from never reaches the handler.
    /// </remarks>
    private static int Number(string value) =>
        Whole(PageNumber.Parameter, value) is var number && number > Page.MaximumNumber
            ? throw Page.PastTheLast(value, tooFar: true)
            : number;

    private static int Whole(string name, string value) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : throw new InvalidQueryException(name, value);
}
