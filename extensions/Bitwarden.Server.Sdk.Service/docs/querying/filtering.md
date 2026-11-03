# Filtering

A read-many operation that lets a caller narrow the collection takes one `filter[{name}]` query
parameter per criterion, with comma-delimited lists for several values and a bracketed form for
ranges. The package binds and validates each filter as a `Filter<T>` handler parameter; you apply
it to your query. The standard: [Filtering](https://contributing.bitwarden.com/architecture/server/apis/filtering).

## What the package does

| The standard                                                   | The package                                                                                                                                                                                               |
| -------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Criteria `MUST` arrive as `filter[{name}]` parameters          | A `Filter<T>? name` parameter reads `filter[name]`. The parameter's name is the filter's, `T` is what it accepts, and nullability makes it optional. The OpenAPI document publishes it as `filter[name]`. |
| Several values `MUST` be a comma-delimited list                | When `T` is a collection (`List<T>`, `T[]`, `IReadOnlyList<T>` and the other list interfaces), repeated parameters and comma-delimited lists are both accepted, in any combination.                       |
| Several values `MUST` mean "or"                                | Yours to apply, usually with `Contains`. The package hands you the values.                                                                                                                                |
| A comma in a value `MUST` be escaped as `\,`, a backslash `\\` | On a list or a range, `\,` is a comma and `\\` a backslash. A value ending in a lone `\` is refused `422`. A single-value filter is never split, so its value is taken whole.                             |
| Ranges `MUST` use `[`, `]`, `(`, `)` and `MAY` use `*`         | `Filter<Range<T>>` reads `[begin,end)` and its variants, for any `T` that is a comparable value type (dates, numbers). `Within` applies it to a query and `Range<T>.Contains` tests a value.              |
| A filter naming an unsupported field `MUST` be refused `422`   | Not enforced. A `filter[...]` parameter that no handler parameter declares is ignored. See [What you decide](#what-you-decide).                                                                           |
| `MAY` support `*` wildcards, documenting which attributes do   | Not supported. A `*` reaches a `Filter<string>` as a literal character.                                                                                                                                   |
| `MAY` use dot-notation (`address.city`)                        | Not supported: a filter's name is a C# parameter name.                                                                                                                                                    |

Values are converted with the type's converter in the invariant culture, and enumerations by their
published upper snake case names. What happens to a value that doesn't convert depends on the
filter:

- **A single-value filter** refuses it `422` (`'filter[id]' expects an identifier, not 'nope'.`),
  and refuses the parameter sent twice (`accepts one value`).
- **A list filter** drops it and keeps the rest. A list is a set to intersect with, so an
  identifier that can't exist simply matches nothing.
- **A range** refuses anything it can't read exactly: a malformed range, a bound that doesn't
  convert, a lower bound above the upper, or equal bounds with one excluded.

`[Filterable]` is not part of `filter[...]` parameters. It marks the fields a caller may name in a
JSON Logic expression, on a search or a bulk operation; see
[Advanced queries](advanced-queries.md).

## Example

A list of widgets filtered by identifier, by name and by when each was created, read with Entity
Framework Core:

```csharp
using Bitwarden.Server.Sdk.Service;
using Bitwarden.Server.Sdk.Service.Resources;
using Bitwarden.Server.Sdk.Service.Querying;
using Microsoft.EntityFrameworkCore;
using static Bitwarden.Server.Sdk.Service.StandardResponses;

public static class WidgetsEndpoints
{
    public const string Collection = "api/v1/widgets";

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var widgets = endpoints.MapGroup(Collection).WithStandards();
        widgets.MapGet("", ListWidgets);
    }

    /// <summary>
    /// List widgets
    /// </summary>
    /// <param name="id">Only these widgets.</param>
    /// <param name="name">Only widgets with this name.</param>
    /// <param name="createdAt">Only widgets created within this range.</param>
    [StandardReadMany]
    public static async Task<PagedResourceListDocument<Widget>> ListWidgets(
        Filter<List<Guid>>? id,
        Filter<string>? name,
        Filter<Range<DateTimeOffset>>? createdAt,
        Sort? sort,
        WidgetDatabase database,
        PageNumber pageNumber = default,
        PageSize pageSize = default,
        CancellationToken cancellationToken = default)
    {
        var page = new Page { Number = pageNumber, Size = pageSize };
        var widgets = database.Widgets.AsNoTracking();

        // A list filter matches a row that has any of its values.
        if (id is not null)
        {
            widgets = widgets.Where(widget => id.Value.Contains(widget.Id));
        }

        if (name is not null)
        {
            widgets = widgets.Where(widget => widget.Name == name.Value);
        }

        // Leaves the query as it is when the caller sent no range.
        widgets = widgets.Within(widget => widget.CreatedAt, createdAt?.Value);

        var rows = await widgets
            .SortedBy(sort ?? Sort.Default<Widget>(), widget => widget.Id)
            .Page(page)
            .ToListAsync(cancellationToken);

        return ReadManyResponse(
            await rows.ToPagedAsync(page, ToResource, widgets.CountAsync, cancellationToken),
            page);
    }

    private static Widget ToResource(WidgetEntity entity) =>
        new() { Id = entity.Id.ToString(), Name = entity.Name, Notes = entity.Notes, CreatedAt = entity.CreatedAt };
}
```

`Within` builds the comparison as an expression tree with the bound as a parameter, so it runs in
the database.

A caller asks for the widgets created in the second half of January:

```http
GET /api/v1/widgets?filter[createdAt]=[2026-01-15T00:00:00Z,2026-02-01T00:00:00Z)
```

```json
{
    "data": [
        {
            "id": "a0000000-0000-4000-8000-000000000003",
            "type": "widget",
            "attributes": {
                "name": "Smith, Jr",
                "notes": "2.AAAA|BBBB|CCCC",
                "createdAt": "2026-01-31T00:00:00Z"
            }
        },
        {
            "id": "a0000000-0000-4000-8000-000000000002",
            "type": "widget",
            "attributes": {
                "name": "Round widget",
                "notes": "2.AAAA|BBBB|CCCC",
                "createdAt": "2026-01-21T00:00:00Z"
            }
        }
    ],
    "meta": {
        "pageCount": 1,
        "pageNumber": 1,
        "pageSize": 50,
        "totalCount": 2
    },
    "links": {
        "first": "/api/v1/widgets?filter%5BcreatedAt%5D=%5B2026-01-15T00%3A00%3A00Z%2C2026-02-01T00%3A00%3A00Z%29&page[number]=1&page[size]=50",
        "last": "/api/v1/widgets?filter%5BcreatedAt%5D=%5B2026-01-15T00%3A00%3A00Z%2C2026-02-01T00%3A00%3A00Z%29&page[number]=1&page[size]=50"
    }
}
```

The page links carry the caller's filters, so following them stays in the same query. A range that
can't be read is refused at the parameter:

```http
GET /api/v1/widgets?filter[createdAt]=[2026-01-01
```

```json
{
    "errors": [
        {
            "status": "422",
            "code": "invalid-value",
            "title": "A field has an invalid value.",
            "detail": "'filter[createdAt]' expects a range such as [begin,end), with datetimeoffset bounds or * for no limit, not '[2026-01-01'.",
            "source": { "parameter": "filter[createdAt]" }
        }
    ]
}
```

### In memory

The filter types don't need a database. `Range<T>.Contains` tests one value, and the rest of the
query works on any `IQueryable`, including `AsQueryable()` over a list:

```csharp
public static PagedResourceListDocument<Widget> ListWidgets(
    IReadOnlyList<Widget> all,
    Filter<List<Guid>>? id,
    Filter<Range<DateTimeOffset>>? createdAt,
    Page page)
{
    IEnumerable<Widget> widgets = all;

    if (id is not null)
    {
        var ids = id.Value.Select(each => each.ToString()).ToHashSet();
        widgets = widgets.Where(widget => ids.Contains(widget.Id!));
    }

    if (createdAt is not null)
    {
        widgets = widgets.Where(widget =>
            widget.CreatedAt is { } created && createdAt.Value.Contains(created));
    }

    var sorted = widgets.AsQueryable().SortedBy(Sort.Default<Widget>(), widget => widget.Id).ToList();

    return ReadManyResponse(sorted.ToPaged(page), page);
}
```

## What you decide

- **Which filters an operation offers.** Each one has to be indexed and authorized, so declare only
  what a caller needs. Name each after the resource attribute it filters.
- **A required filter.** Declare the parameter non-nullable (`Filter<Guid> organizationId`) and a
  request without it is refused `422`.
- **How much of a range is allowed.** A rule such as "a year at most" is yours: read
  `Range<T>.Lower` and `Upper`, and throw `InvalidFilterException` to refuse with `422` at the
  parameter.
- **Refusing a filter you don't support.** The standard requires a `422` for a `filter[...]`
  parameter naming a field the operation doesn't filter on, and the package ignores one today. Until
  it does, check the query string yourself:

    ```csharp
    public static class DeclaredFilters
    {
        /// <summary>
        /// Refuses any <c>filter[...]</c> parameter the operation does not declare.
        /// </summary>
        public static void Require(HttpRequest request, params string[] declared)
        {
            foreach (var key in request.Query.Keys)
            {
                if (key.StartsWith("filter[", StringComparison.Ordinal)
                    && !declared.Any(name => key == $"filter[{name}]"))
                {
                    throw InvalidFilterException.Because(
                        key,
                        $"'{key}' is not a filter this collection supports. "
                        + $"The filters are {string.Join(", ", declared.Select(name => $"'filter[{name}]'"))}.");
                }
            }
        }
    }
    ```

    Take `HttpRequest request` as a handler parameter and call
    `DeclaredFilters.Require(request, nameof(name))` first. `GET /api/v1/widgets?filter[colour]=red`
    is then refused `422` with `"source": { "parameter": "filter[colour]" }`.

- **Wildcards.** If an attribute accepts `*`, document which, and read the value yourself from a
  `Filter<string>`. The standard requires `\*` for a literal asterisk and `\\` for a backslash
  wherever wildcards apply. A prefix match, refusing a `*` anywhere but the end:

    ```csharp
    public static IQueryable<WidgetEntity> Matching(IQueryable<WidgetEntity> widgets, string value)
    {
        var text = new System.Text.StringBuilder();
        var prefix = false;

        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '\\' && i + 1 < value.Length)
            {
                text.Append(value[++i]);
            }
            else if (value[i] == '*' && i == value.Length - 1)
            {
                prefix = true;
            }
            else if (value[i] is '*' or '\\')
            {
                throw new InvalidFilterException(
                    "filter[name]", value, "takes a name, or the start of one followed by *");
            }
            else
            {
                text.Append(value[i]);
            }
        }

        var name = text.ToString();

        return prefix
            ? widgets.Where(widget => widget.Name.StartsWith(name))
            : widgets.Where(widget => widget.Name == name);
    }
    ```

- **Never filter on an encrypted member.** An `[Encrypted]` value is Protected Data, encrypted on
  the client. Its ciphertext means nothing to the server, so offer no filter on it.

## See also

- [Paging](paging.md), [Sorting](sorting.md) and [Sparse fieldsets](sparse-fieldsets.md): the other
  query parameters.
- [Advanced queries](advanced-queries.md): `:search` and JSON Logic, for "or" and long expressions.
- The standards: [Filtering](https://contributing.bitwarden.com/architecture/server/apis/filtering)
  and
  [Unrecognized fields, query parameters, and headers](https://contributing.bitwarden.com/architecture/server/apis/unrecognized-fields-query-parameters-and-headers).
