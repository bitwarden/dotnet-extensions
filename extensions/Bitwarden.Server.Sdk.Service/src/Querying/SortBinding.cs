namespace Bitwarden.Server.Sdk.Service.Querying;

/// <summary>
/// Reads <see cref="Sort"/> from the <c>sort</c> query parameter, for its <c>BindAsync</c>.
/// </summary>
internal static class SortBinding
{
    /// <summary>
    /// The order the query asks for, or null when it asks for none.
    /// </summary>
    /// <param name="query">The request's query string.</param>
    /// <param name="resource">The resource the operation returns, whose declarations say what sorts.</param>
    /// <returns>The sort, or null when the caller did not choose one.</returns>
    /// <exception cref="InvalidSortException">The sort names a field the collection does not sort by.</exception>
    public static Sort? Bind(Microsoft.AspNetCore.Http.IQueryCollection query, Type? resource)
    {
        var supplied = query[Sort.Parameter];

        if (supplied.Count == 0)
        {
            // Absent. Null says the caller did not choose, which is not the same as there being
            // no order; the query resolves it against the resource's declared default.
            return null;
        }

        var allowed = resource is null ? [] : SortableFields.Of(resource);

        return Parse(string.Join(',', supplied!), allowed, resource);
    }

    private static Sort Parse(string supplied, IReadOnlyList<SortableField> allowed, Type? resource)
    {
        var names = allowed.Select(field => field.Name).ToArray();

        var requested = supplied
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(value => value.Trim())
            .Where(value => value.Length > 0)
            .ToArray();

        if (requested.Length == 0)
        {
            throw new InvalidSortException(supplied, "names no field", names);
        }

        var fields = new List<SortBy>(requested.Length);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var value in requested)
        {
            var descending = value.StartsWith('-');
            var name = descending ? value[1..] : value;

            // Matched without regard to case so that a caller who writes createdat is understood,
            // but recorded under the published name so that everything downstream sees one
            // spelling of the field.
            var field = allowed.FirstOrDefault(
                candidate => string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase));

            if (field is null)
            {
                throw new InvalidSortException(value, $"'{name}' is not a field this collection sorts by", names);
            }

            if (!seen.Add(field.Name))
            {
                throw new InvalidSortException(value, $"'{field.Name}' is named more than once", names);
            }

            fields.Add(new SortBy(field.Name, descending ? SortDirection.Descending : SortDirection.Ascending));
        }

        // With its resource, so the query can find where each field is stored.
        return resource is null ? Sort.By([.. fields]) : Sort.For(resource, [.. fields]);
    }
}
