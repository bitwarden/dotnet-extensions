using Bitwarden.Server.Sdk.Common;

namespace Bitwarden.Server.Sdk.Service.Querying;

/// <summary>
/// Reads which fields a resource publishes.
/// </summary>
/// <remarks>
/// The list a query expression is checked against. A <c>var</c> naming something outside it is
/// refused with a <c>422</c> rather than ignored, because an ignored term makes a filter match
/// more than the caller asked for, and on a bulk delete that is the difference between
/// removing four rows and removing the table.
/// </remarks>
internal static class ResourceFields
{
    /// <summary>
    /// Refuses an expression naming a field the resource does not mark filterable.
    /// </summary>
    /// <param name="query">The expression.</param>
    /// <param name="resource">The resource it filters.</param>
    /// <exception cref="InvalidRequestException">It names a field the resource does not mark filterable.</exception>
    /// <remarks>
    /// Only the fields the resource marks <see cref="FilterableAttribute"/>: which fields an
    /// expression may name is part of the contract, and a resource that marks none cannot be searched
    /// or changed in bulk at all.
    /// </remarks>
    public static void Require(Query query, Type resource)
    {
        var filterable = StoredFields.FilterableOf(resource);

        foreach (var named in Named(query.Root))
        {
            if (!filterable.ContainsKey(named))
            {
                throw new InvalidRequestException(
                    filterable.Count == 0
                        ? "This resource declares no fields an expression may name, so it cannot be filtered this way."
                        : $"'{named}' is not a field this resource may be filtered on. The fields available are "
                          + $"{string.Join(", ", filterable.Keys.Order().Select(field => $"'{field}'"))}.",
                    "/filter");
            }
        }

        query.Resource = resource;
    }

    private static IEnumerable<string> Named(QueryTerm term)
    {
        if (term.Operator == QueryOperator.Var && term.Field is { Length: > 0 } field)
        {
            yield return field;
        }

        foreach (var named in term.Terms.SelectMany(Named))
        {
            yield return named;
        }
    }
}
