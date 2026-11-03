# Advanced queries

When `filter[...]` parameters can't say enough ("or", or an expression too long for a URL), an API
exposes a search: `POST /api/v1/{resources}:search` with a JSON Logic expression in a top-level
`filter` field, answered like the read-many. The package provides the operation, parses and
validates the expression against the fields the resource marks `[Filterable]`, and compiles it into
a query predicate. The standard:
[Advanced queries](https://contributing.bitwarden.com/architecture/server/apis/advanced-queries).

## What the package does

| The standard                                                         | The package                                                                                                                                                                      |
| -------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| The verb `MUST` be `POST`, the path `/api/v1/{resources}:search`     | `[StandardSearch(Collection)]` is a `POST` to `{collection}:search`. The service refuses to start when the `Map` line's verb or route disagrees with it.                         |
| The expression is JSON Logic in a top-level `filter` field           | A `Search<T>` body, whose required `Filter` is a `Query`. A body without `filter` is refused `422`.                                                                              |
| Only `var`, `==`, `!=`, `>`, `>=`, `<`, `<=`, `in`, `and`, `or`, `!` | Any other operator is refused `422` at `/filter`, naming the permitted ones.                                                                                                     |
| A field the API doesn't support `MUST` be refused `422`              | Only fields the resource marks `[Filterable]` may be named; anything else is refused `422` at `/filter`, listing the filterable fields. This is checked before the handler runs. |
| The response `MUST` be identical in shape to the read-many           | Return the same `PagedResourceListDocument<T>`. `sort`, `page[...]` and `fields[...]` bind on a search as on a list.                                                             |
| Success `MUST` be `200`                                              | It is.                                                                                                                                                                           |

How the expression is read:

- **Comparisons are between one field and one literal**, in either order: `{"<": [5, {"var":
"size"}]}` is the widgets whose size is above 5. Two fields, or two literals, are refused.
- **`in`** is membership when the literal is an array (`{"in": [{"var": "size"}, [15, 20]]}`) and a
  substring match when it is a string and the field is text (`{"in": ["Round", {"var": "name"}]}`).
- **`>`, `>=`, `<`, `<=`** need a field with an order: numbers and dates. Text, booleans and
  enumerations compare only with `==`, `!=` and `in`.
- **`and` and `or`** take two or more conditions, and `!` exactly one. A literal or a bare field
  where a condition belongs is refused.
- **Literals** are converted to the field's type: dates as ISO 8601 strings, enumerations by their
  published names. `null` is allowed only against a field that can be null.
- **An expression holds at most 500 terms** (`Query.MaxTerms`), counting every operator, field and
  literal.

Every refusal is a `422` with a pointer to `/filter`.

`Matching` compiles the expression to an expression tree, so on Entity Framework Core it runs in the
database. A field marked `[Filterable]` is found on the entity as the property of the same name, or
the one `MappedTo` names. The first expression matched against an entity resolves every filterable
field at once, and throws `InvalidOperationException` naming any without a property.

The same expressions select resources for bulk updates, deletes and actions.

## Example

The resource marks what an expression may name, including the inherited `CreatedAt`, and a field
stored under another name:

```csharp
using System.ComponentModel.DataAnnotations;
using Bitwarden.Server.Sdk.Common;
using Bitwarden.Server.Sdk.Service.Resources;
using Bitwarden.Server.Sdk.Service.Querying;

[Sortable(nameof(CreatedAt), Default = true, Descending = true)]
[Filterable(nameof(CreatedAt))]
public sealed record Widget : Resource
{
    /// <summary>
    /// What the widget is called.
    /// </summary>
    [Sortable]
    [Filterable]
    [StringLength(100)]
    public required string Name { get; init; }

    /// <summary>
    /// How many gadgets the widget holds.
    /// </summary>
    [Filterable(MappedTo = nameof(WidgetEntity.Capacity))]
    public int Size { get; init; }

    /// <summary>
    /// The person's notes, encrypted on their device. Never filterable.
    /// </summary>
    [Encrypted(MaxLength = 10_000)]
    public string? Notes { get; init; }
}
```

The search shares its read with the list, so the two answer alike. Its `Map` line is on the route
builder, not the group, because the path is the collection's plus a suffix:

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

        // On the route builder, not the group: the path is the collection's plus a suffix.
        endpoints.MapPost($"{Collection}:search", SearchWidgets).WithStandards();
    }

    /// <summary>
    /// List widgets
    /// </summary>
    [StandardReadMany]
    public static async Task<PagedResourceListDocument<Widget>> ListWidgets(
        Sort? sort,
        WidgetDatabase database,
        PageNumber pageNumber = default,
        PageSize pageSize = default,
        CancellationToken cancellationToken = default) =>
        await Read(database.Widgets.AsNoTracking(), sort, pageNumber, pageSize, cancellationToken);

    /// <summary>
    /// Search widgets
    /// </summary>
    [StandardSearch(Collection)]
    public static async Task<PagedResourceListDocument<Widget>> SearchWidgets(
        Search<Widget> request,
        Sort? sort,
        WidgetDatabase database,
        PageNumber pageNumber = default,
        PageSize pageSize = default,
        CancellationToken cancellationToken = default) =>
        // The expression has been checked against the [Filterable] fields already.
        await Read(database.Widgets.AsNoTracking().Matching(request.Filter), sort, pageNumber, pageSize, cancellationToken);

    // One read for both, so a search answers exactly as the list does.
    private static async Task<PagedResourceListDocument<Widget>> Read(
        IQueryable<WidgetEntity> widgets,
        Sort? sort,
        PageNumber pageNumber,
        PageSize pageSize,
        CancellationToken cancellationToken)
    {
        var page = new Page { Number = pageNumber, Size = pageSize };

        var rows = await widgets
            .SortedBy(sort ?? Sort.Default<Widget>(), widget => widget.Id)
            .Page(page)
            .ToListAsync(cancellationToken);

        return ReadManyResponse(
            await rows.ToPagedAsync(page, ToResource, widgets.CountAsync, cancellationToken),
            page);
    }

    private static Widget ToResource(WidgetEntity entity) =>
        new()
        {
            Id = entity.Id.ToString(),
            Name = entity.Name,
            Size = entity.Capacity,
            Notes = entity.Notes,
            CreatedAt = entity.CreatedAt,
        };
}
```

The round widgets that hold at least ten gadgets, sorted by name, one at a time:

```http
POST /api/v1/widgets:search?sort=name&page[size]=1
Content-Type: application/json
```

```json
{
    "filter": {
        "and": [
            { "in": ["Round", { "var": "name" }] },
            { ">=": [{ "var": "size" }, 10] }
        ]
    }
}
```

```json
{
    "data": [
        {
            "id": "a0000000-0000-4000-8000-000000000002",
            "type": "widget",
            "attributes": {
                "name": "Round widget 2",
                "size": 10,
                "notes": "2.AAAA|BBBB|CCCC",
                "createdAt": "2026-01-03T00:00:00Z"
            }
        }
    ],
    "meta": { "pageCount": 2, "pageNumber": 1, "pageSize": 1, "totalCount": 2 },
    "links": {
        "first": "/api/v1/widgets:search?sort=name&page[number]=1&page[size]=1",
        "next": "/api/v1/widgets:search?sort=name&page[number]=2&page[size]=1",
        "last": "/api/v1/widgets:search?sort=name&page[number]=2&page[size]=1"
    }
}
```

The links carry the query string only. The expression is in the body, so a caller follows `next`
with a `POST` of the same body.

An expression naming a field the resource doesn't mark `[Filterable]` is refused before the handler
runs:

```json
{ "filter": { "==": [{ "var": "notes" }, "x"] } }
```

```json
{
    "errors": [
        {
            "status": "422",
            "code": "invalid-value",
            "title": "A field has an invalid value.",
            "detail": "'notes' is not a field this resource may be filtered on. The fields available are 'createdAt', 'name', 'size'.",
            "source": { "pointer": "/filter" }
        }
    ]
}
```

And so is an operator outside the permitted set, such as `{"+": [{"var": "size"}, 1]}`:
`"'+' is not an operator a query may use. The permitted operators are 'var', '==', '!=', '>', '>=', '<', '<=', 'in', 'and', 'or', '!'."`

### In memory

`Matching` works on any `IQueryable`, so the same expression runs over rows in memory. It resolves
fields through `MappedTo`, so match the stored rows, then map them:

```csharp
public static PagedResourceListDocument<Widget> Search(
    IReadOnlyList<WidgetEntity> rows, Search<Widget> request, Page page)
{
    // The same expression, compiled and run over the rows in memory.
    var found = rows.AsQueryable()
        .Matching(request.Filter)
        .SortedBy(Sort.Default<Widget>(), row => row.Id)
        .Select(row => new Widget { Id = row.Id.ToString(), Name = row.Name, Size = row.Capacity })
        .ToList();

    return ReadManyResponse(found.ToPaged(page), page);
}
```

### An expression built in code

A `Query` built from `QueryTerm`s, rather than read from a request, names the entity's own
properties:

```csharp
using System.Text.Json;

public static IQueryable<WidgetEntity> Large(WidgetDatabase database)
{
    // Built in code, an expression names the entity's own properties.
    var large = new Query
    {
        Root = new QueryTerm
        {
            Operator = QueryOperator.GreaterThanOrEqual,
            Terms =
            [
                new QueryTerm { Operator = QueryOperator.Var, Field = nameof(WidgetEntity.Capacity) },
                new QueryTerm { Value = JsonSerializer.SerializeToElement(10) },
            ],
        },
    };

    return database.Widgets.Matching(large);
}
```

An expression a caller sent is matched only after the platform has checked it against the
resource. `Query.Parse` reads one from JSON, but `Matching` throws `InvalidOperationException` for
it unless it arrived as a `Search<T>` or bulk body on its standard operation. The service refuses to
start when a handler takes a `Search<T>` (or a bulk body with a filter) on any other kind of
operation.

## What you decide

- **Whether to offer a search.** A resource with no `[Filterable]` field can't be searched. Mark
  only fields that are indexed and that callers may see, and never an `[Encrypted]` member: its
  value is Protected Data, encrypted on the client, and its ciphertext means nothing to the server.
- **An expression that is too costly.** The package bounds its size, not its cost. A rule such as
  "a substring match needs another condition" is yours; throw `InvalidRequestException` with the
  pointer `/filter` to refuse it with `422`.
- **Fields that aren't columns.** `Matching` is a convenience. A field that is computed or lives on
  a joined row needs you to walk `request.Filter.Root` and build the predicate yourself; the
  platform has already refused anything the resource doesn't declare.
- **Paging.** A search pages like a list; see [Paging](paging.md). A caller following a page link
  sends the same body again.

## See also

- [Filtering](filtering.md): simple `filter[...]` parameters.
- [Sorting](sorting.md), [Paging](paging.md) and [Sparse fieldsets](sparse-fieldsets.md): the query
  parameters a search shares with the list.
- The standards:
  [Advanced queries](https://contributing.bitwarden.com/architecture/server/apis/advanced-queries)
  and
  [Bulk operations: targeting](https://contributing.bitwarden.com/architecture/server/apis/bulk-operations/targeting).
