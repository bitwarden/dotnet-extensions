# Paging

A collection should be returned a page at a time, by offset (`page[number]` and `page[size]`), by
cursor (`page[size]` and `page[after]`), or both, with a documented default and maximum size. The
package binds and checks the paging parameters, takes the page from your query, counts only when it
must, and writes the `meta` and links. The standard: [Paging](https://contributing.bitwarden.com/architecture/server/apis/paging).

## What the package does

| The standard                                                                                        | The package                                                                                                                                                                     |
| --------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Offset paging uses `page[number]` and `page[size]`                                                  | `PageNumber pageNumber = default` and `PageSize pageSize = default` parameters, combined into a `Page`.                                                                         |
| `page[number]` `MUST` default to `1`                                                                | It does. A number below 1, or above 10737418 (the farthest page whose rows fit in an `int`), is refused `422`.                                                                  |
| A default `page[size]` `MUST` be documented and applied                                             | 50, or the `[DefaultValue]` on the `PageSize` parameter. The OpenAPI document publishes the default.                                                                            |
| A maximum `page[size]` `MUST` be documented                                                         | 200 (`Page.MaximumSize`). A larger size is reduced to 200, not refused; a size below 1 is refused `422`. The OpenAPI document publishes the minimum, maximum and the reduction. |
| Offset `meta` `MUST` carry `pageNumber` and `pageSize`, `SHOULD` carry `pageCount` and `totalCount` | `ReadManyResponse(paged, page)` writes all four. An empty collection is one empty page (`pageCount` 1).                                                                         |
| Cursor paging uses `page[size]` and `page[after]`                                                   | `PageSize pageSize = default` and `PageAfter pageAfter = default`, combined into a `CursorPage`.                                                                                |
| A cursor `MUST` be opaque                                                                           | `CursorQueryable` mints and reads it. A cursor that wasn't issued by the operation, or was issued under another sort, is refused `422`.                                         |
| Cursor `meta` `MUST` carry `pageSize`, `hasMore` and `cursor`; the last page `MUST` omit `cursor`   | `ReadManyResponse(cursorPaged, page)` writes them and leaves `cursor` out of the last page.                                                                                     |
| Paging parameters for a style the API doesn't implement `MUST` be refused `422`                     | Not enforced: `page[after]` on an offset-paged operation, `page[number]` on a cursor-paged one, and paging parameters on an operation that doesn't page are ignored.            |

Offset-paged responses also carry `first`, `prev`, `next` and `last` links. They repeat every query
parameter the caller sent (filters, sort, sparse fieldsets) with the page changed, so following one
stays in the same query. Cursor-paged responses carry no links: the next page is `page[after]` set
to `meta.cursor`.

## Offset paging

A paged list read with Entity Framework Core. `Page` takes the slice from an ordered query, and
`ToPagedAsync` works out the total:

```csharp
using System.ComponentModel;
using Bitwarden.Server.Sdk.Service;
using Bitwarden.Server.Sdk.Service.Resources;
using Bitwarden.Server.Sdk.Service.Querying;
using Microsoft.EntityFrameworkCore;
using static Bitwarden.Server.Sdk.Service.StandardResponses;

/// <summary>
/// List widgets
/// </summary>
/// <param name="id">Only these widgets.</param>
[StandardReadMany]
public static async Task<PagedResourceListDocument<Widget>> ListWidgets(
    Filter<List<Guid>>? id,
    Sort? sort,
    WidgetDatabase database,
    PageNumber pageNumber = default,
    [DefaultValue(25)] PageSize pageSize = default,
    CancellationToken cancellationToken = default)
{
    var page = new Page { Number = pageNumber, Size = pageSize };

    var widgets = database.Widgets.AsNoTracking();
    int? atMost = null;

    if (id is not null)
    {
        widgets = widgets.Where(widget => id.Value.Contains(widget.Id));

        // The primary key allows one row per identifier, so a page that reaches
        // this many rows holds the last one and needs no count.
        atMost = id.Value.Distinct().Count();
    }

    var rows = await widgets
        .SortedBy(sort ?? Sort.Default<Widget>(), widget => widget.Id)
        .Page(page)
        .ToListAsync(cancellationToken);

    // Counts the filtered query, without the page, and only when the rows can't
    // settle the total.
    var paged = await rows.ToPagedAsync(page, ToResource, widgets.CountAsync, cancellationToken, atMost);

    return ReadManyResponse(paged, page);
}
```

**When the count runs.** A page with fewer rows than it holds is the last one, so the total is the
rows before it plus the rows on it, and no count runs. Most lists fit on their first page, so most
reads cost one query. A full page counts (more may follow), and so does an empty page past the
first. `atMost` passes a bound that a key the schema enforces guarantees; a page that reaches it
needs no count either. Only pass a bound the database enforces, never one the code merely keeps.

Always order before paging, and end the order with a unique field (the identifier), or rows that
share a sort value can repeat or vanish between pages. See [Sorting](sorting.md).

The second page of five widgets, two at a time:

```http
GET /api/v1/widgets?page[size]=2&page[number]=2
```

```json
{
    "data": [
        {
            "id": "a0000000-0000-4000-8000-000000000003",
            "type": "widget",
            "attributes": {
                "name": "Widget 3",
                "notes": "2.AAAA|BBBB|CCCC",
                "createdAt": "2026-01-04T00:00:00Z"
            }
        },
        {
            "id": "a0000000-0000-4000-8000-000000000002",
            "type": "widget",
            "attributes": {
                "name": "Widget 2",
                "notes": "2.AAAA|BBBB|CCCC",
                "createdAt": "2026-01-03T00:00:00Z"
            }
        }
    ],
    "meta": { "pageCount": 3, "pageNumber": 2, "pageSize": 2, "totalCount": 5 },
    "links": {
        "first": "/api/v1/widgets?page[number]=1&page[size]=2",
        "prev": "/api/v1/widgets?page[number]=1&page[size]=2",
        "next": "/api/v1/widgets?page[number]=3&page[size]=2",
        "last": "/api/v1/widgets?page[number]=3&page[size]=2"
    }
}
```

A page number of zero is refused at the parameter:

```json
{
    "errors": [
        {
            "status": "422",
            "code": "invalid-value",
            "title": "A field has an invalid value.",
            "detail": "The 'page[number]' parameter must be a whole number of 1 or more, not '0'.",
            "source": { "parameter": "page[number]" }
        }
    ]
}
```

### In memory

A collection already in memory knows its size, so `ToPaged` takes the page and counts nothing:

```csharp
public static PagedResourceListDocument<Widget> ListWidgets(IReadOnlyList<Widget> all, Sort? sort, Page page)
{
    var sorted = all.AsQueryable()
        .SortedBy(sort ?? Sort.Default<Widget>(), widget => widget.Id)
        .ToList();

    return ReadManyResponse(sorted.ToPaged(page), page);
}
```

## Cursor paging

Cursor paging never counts and stays stable while rows are written, at the cost of page numbers and
totals. Use it for a large collection read in order, such as an event log:

```csharp
[Sortable(nameof(CreatedAt), Default = true, Descending = true)]
public sealed record WidgetEvent : Resource
{
    /// <summary>
    /// The widget the event happened to.
    /// </summary>
    public required string WidgetId { get; init; }

    /// <summary>
    /// What happened.
    /// </summary>
    [Sortable]
    public required string Action { get; init; }
}

/// <summary>
/// List widget events
/// </summary>
[StandardReadMany]
public static async Task<CursorPagedResourceListDocument<WidgetEvent>> ListWidgetEvents(
    Sort? sort,
    WidgetDatabase database,
    PageSize pageSize = default,
    PageAfter pageAfter = default,
    CancellationToken cancellationToken = default)
{
    var page = new CursorPage { Size = pageSize, After = pageAfter };
    var order = sort ?? Sort.Default<WidgetEvent>();

    // Filters go before Page; Page orders the query and reads one row more than the
    // page holds, to learn whether another page follows.
    var rows = await database.WidgetEvents.AsNoTracking()
        .Page(page, order, row => row.Id)
        .ToListAsync(cancellationToken);

    return ReadManyResponse(rows.ToCursorPage(page, order, row => row.Id, ToResource), page);
}
```

Pass the same sort and tie-break to `Page` and `ToCursorPage`. The tie-break must be unique, and
no sort field or tie-break may be nullable on the entity: providers disagree about where nulls sort,
so `Page` throws `InvalidOperationException` rather than mint a cursor that means different things
on different databases. A cursor records the sort, not the filters; a caller who changes filters
mid-walk should start again.

```http
GET /api/v1/widget-events?page[size]=2
```

```json
{
    "data": [
        {
            "id": "e0000000-0000-4000-8000-000000000003",
            "type": "widget-event",
            "attributes": {
                "widgetId": "a0000000-0000-4000-8000-000000000001",
                "action": "RENAMED",
                "createdAt": "2026-01-01T03:00:00Z"
            }
        },
        {
            "id": "e0000000-0000-4000-8000-000000000002",
            "type": "widget-event",
            "attributes": {
                "widgetId": "a0000000-0000-4000-8000-000000000001",
                "action": "RENAMED",
                "createdAt": "2026-01-01T02:00:00Z"
            }
        }
    ],
    "meta": {
        "pageSize": 2,
        "hasMore": true,
        "cursor": "eyJzIjoiLWNyZWF0ZWRBdCIsImsiOlsiMjAyNi0wMS0wMVQwMjowMDowMFx1MDAyQjAwOjAwIiwiZTAwMDAwMDAtMDAwMC00MDAwLTgwMDAtMDAwMDAwMDAwMDAyIl19"
    }
}
```

Passing that cursor back as `page[after]` returns the last event with
`"meta": { "pageSize": 2, "hasMore": false }`. The same cursor sent with `sort=action` is refused:

```json
{
    "errors": [
        {
            "status": "422",
            "code": "invalid-value",
            "title": "A field has an invalid value.",
            "detail": "'page[after]' is not a cursor this operation can continue from: it was issued for a different sort. A cursor continues the order it was issued in. Start again without it. ('eyJzIjoiLWNyZWF0ZWRBdCIsImsiOlsiMjAyNi0w…')",
            "source": { "parameter": "page[after]" }
        }
    ]
}
```

## What you decide

- **Which style.** Offset paging for a collection a caller browses and wants a total for; cursor
  paging for a large or fast-moving one read in order. An operation can't offer both from one
  signature: its return type is a `PagedResourceListDocument<T>` or a
  `CursorPagedResourceListDocument<T>`.
- **The default size.** Put `[DefaultValue(n)]` on the `PageSize` parameter. The maximum is the
  package's, 200, for every operation.
- **Not paging.** The standard allows it only where the result has a small, fixed upper bound.
  Return `ReadManyResponse(resources)`, a `ResourceListDocument<T>` with no paging `meta`.
- **Refusing paging parameters you don't support.** The package ignores them today. To meet the
  standard's `422`, check `request.Query` for `page[after]` (on an offset-paged operation) or
  `page[number]` (on a cursor-paged one) and refuse it, the same way
  [Filtering](filtering.md#what-you-decide) refuses an undeclared filter.
  `InvalidFilterException.Because(parameter, detail)` answers `422` with your detail at any query
  parameter, despite its name.

## See also

- [Sorting](sorting.md): the order a page is taken in.
- [Filtering](filtering.md) and [Sparse fieldsets](sparse-fieldsets.md): parameters the page links
  carry.
- [Advanced queries](advanced-queries.md): a search pages the same way.
- The standard: [Paging](https://contributing.bitwarden.com/architecture/server/apis/paging).
