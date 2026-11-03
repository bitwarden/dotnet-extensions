# Sorting

A caller chooses the order of a collection with one `sort` parameter: a comma-delimited list of
fields, each descending when it starts with `-`. The package reads the sortable fields from the
resource, binds and validates `sort` against them, and applies the order to a query. The standard:
[Sorting](https://contributing.bitwarden.com/architecture/server/apis/sorting).

## What the package does

| The standard                                                 | The package                                                                                                                                                                |
| ------------------------------------------------------------ | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| One `sort` parameter, a comma-delimited list of fields       | A `Sort? sort` handler parameter. Null means the caller didn't ask; `Sort.Fields` is the list of `SortBy` (field and `SortDirection`) in order of precedence.              |
| A leading `-` `MUST` mean descending                         | It does.                                                                                                                                                                   |
| A `sort` naming an unsupported field `MUST` be refused `422` | Only fields the resource marks `[Sortable]` are accepted; anything else is refused `422`, listing the sortable fields. So is a field named twice, or a `sort` naming none. |

A few things go beyond the standard:

- **The contract is declared once, on the resource.** `[Sortable]` on a property marks it;
  `[Sortable(nameof(CreatedAt))]` on the record marks an inherited field such as `CreatedAt`. The
  binder knows the resource from the handler's return type, and the OpenAPI document lists the
  sortable fields and the default under `sort`.
- **Every sortable resource has a default.** Exactly one sortable field sets `Default = true`, and
  `Descending = true` makes the default run largest first. `Sort.Default<Widget>()` returns it.
- **Names match without regard to case** (`sort=NAME` is `name`) and are recorded under the
  published name.
- **Fields map to storage.** `SortedBy` finds each field on the entity as the property of the same
  name; `MappedTo` names a different one.

A mistake in the declarations (no default, two defaults, a field declared twice, a `MappedTo` the
entity doesn't have) throws `InvalidOperationException` the first time the declarations are read or
resolved against an entity: when the OpenAPI document is built, a `sort` is bound, or the first
query is sorted. It doesn't stop the service starting, so a test that lists or sorts each resource
catches it.

## Example

The resource declares its sortable fields, its default order, and where an inherited field is
stored:

```csharp
using System.ComponentModel.DataAnnotations;
using Bitwarden.Server.Sdk.Common;
using Bitwarden.Server.Sdk.Service.Resources;
using Bitwarden.Server.Sdk.Service.Querying;

[Sortable(nameof(CreatedAt), Default = true, Descending = true)]
[Sortable(nameof(UpdatedAt), MappedTo = nameof(WidgetEntity.ModifiedAt))]
public sealed record Widget : Resource
{
    /// <summary>
    /// What the widget is called.
    /// </summary>
    [Sortable]
    [StringLength(100)]
    public required string Name { get; init; }

    /// <summary>
    /// The person's notes, encrypted on their device.
    /// </summary>
    [Encrypted(MaxLength = 10_000)]
    public string? Notes { get; init; }
}
```

The operation takes `Sort? sort`, falls back to the default, and breaks ties on the identifier,
here with Entity Framework Core:

```csharp
using Bitwarden.Server.Sdk.Service;
using Bitwarden.Server.Sdk.Service.Resources;
using Bitwarden.Server.Sdk.Service.Querying;
using Microsoft.EntityFrameworkCore;
using static Bitwarden.Server.Sdk.Service.StandardResponses;

/// <summary>
/// List widgets
/// </summary>
[StandardReadMany]
public static async Task<PagedResourceListDocument<Widget>> ListWidgets(
    Sort? sort,
    WidgetDatabase database,
    PageNumber pageNumber = default,
    PageSize pageSize = default,
    CancellationToken cancellationToken = default)
{
    var page = new Page { Number = pageNumber, Size = pageSize };
    var widgets = database.Widgets.AsNoTracking();

    // The caller's order, or the resource's default, then the identifier so rows that
    // share a value keep one order from page to page.
    var rows = await widgets
        .SortedBy(sort ?? Sort.Default<Widget>(), widget => widget.Id)
        .Page(page)
        .ToListAsync(cancellationToken);

    return ReadManyResponse(
        await rows.ToPagedAsync(page, ToResource, widgets.CountAsync, cancellationToken),
        page);
}
```

`SortedBy` builds `OrderBy` and `ThenBy` calls as an expression tree, so the order runs in the
database. Always pass the tie-break when the result is paged: without a unique last field, the
database may order rows that share a value differently on each request.

`GET /api/v1/widgets?sort=-updatedAt,name` orders by `ModifiedAt` descending, then by name, then
by identifier. A field the resource doesn't mark sortable is refused:

```http
GET /api/v1/widgets?sort=colour
```

```json
{
    "errors": [
        {
            "status": "422",
            "code": "invalid-value",
            "title": "A field has an invalid value.",
            "detail": "'colour' cannot be used to sort: 'colour' is not a field this collection sorts by. Sortable fields are 'createdAt', 'name', 'updatedAt'.",
            "source": { "parameter": "sort" }
        }
    ]
}
```

### In memory

`SortedBy` works on any `IQueryable`, so `AsQueryable()` sorts a list in memory. It resolves the
fields on the type it sorts, through `MappedTo`, so sort the stored rows when a field is mapped:

```csharp
public static List<WidgetEntity> Sorted(IReadOnlyList<WidgetEntity> rows, Sort? sort) =>
    rows.AsQueryable()
        .SortedBy(sort ?? Sort.Default<Widget>(), row => row.Id)
        .ToList();
```

A list of resources sorts the same way when every sortable field is a property of the resource
under its own name.

### A sort built in code

`Sort.By` builds a sort without a request. It isn't tied to a resource, so its fields are the
entity's own property names:

```csharp
public static IQueryable<WidgetEntity> NewestFirst(WidgetDatabase database) =>
    // Built by hand, a sort names the entity's own properties.
    database.Widgets.SortedBy(Sort.By(SortBy.Descending(nameof(WidgetEntity.CreatedAt))), widget => widget.Id);
```

## What you decide

- **Which fields sort.** Each one needs an index, so mark only what callers need. Don't mark an
  `[Encrypted]` member: its ciphertext is Protected Data and has no meaningful order.
- **The default order,** and which way it runs.
- **An order that isn't a column.** `SortedBy` is a convenience. When a field is computed or lives
  on a joined row, read `sort.Fields` and order the query yourself; the binder has already refused
  anything the resource doesn't declare.
- **A `sort` on an operation that doesn't sort.** An operation without a `Sort` parameter ignores
  `sort`; the standard asks for `422` when it names a field the operation doesn't support. Refuse
  it yourself if the operation must not be sorted, as [Filtering](filtering.md#what-you-decide)
  does for an undeclared filter.

## See also

- [Paging](paging.md): sorting is what makes a page stable.
- [Filtering](filtering.md) and [Advanced queries](advanced-queries.md): a search takes `sort` too.
- The standard: [Sorting](https://contributing.bitwarden.com/architecture/server/apis/sorting).
