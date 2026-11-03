# Reading resources

The standard reads a collection with `GET /api/v1/{plural}` and one resource with
`GET /api/v1/{plural}/{id}`, both answering `200`. The package gives each an attribute, response
helpers that build the document and its paging links, and query types that bind the caller's
paging, filters, sort and fields. The standard:
[Reading resources](https://contributing.bitwarden.com/architecture/server/apis/reading-resources).

## What the package does

| The standard                                    | The package                                                                                |
| ----------------------------------------------- | ------------------------------------------------------------------------------------------ |
| Read many: the verb `MUST` be `GET`             | `[StandardReadMany]` is a `GET`; the service won't start if its `Map` line uses another.   |
| Read many: the path `SHOULD` be the collection  | `[StandardReadMany]` defaults to the group's own route.                                    |
| Read one: the verb `MUST` be `GET`              | `[StandardReadOne]` is a `GET`, checked the same way.                                      |
| Read one: the path `SHOULD` be `/{plural}/{id}` | `[StandardReadOne]` defaults to `{id}`; the `Map` line must use it.                        |
| Success `MUST` be `200`                         | `200` for both. A read many that finds nothing answers `200` with an empty `data`.         |
| Each service declares what can be queried       | Query parameters are handler parameters; a `filter[...]` no parameter declares is refused. |

- **`ReadOneResponse(resource)`** builds a single-resource document, and
  `ReadOneResponse(resource, fields)` leaves out the attributes a sparse fieldset didn't ask for.
- **`ReadManyResponse`** has three forms: a whole list (`IReadOnlyList<T>`), an offset page
  (`Paged<T>` and the `Page` asked for), and a cursor page (`CursorPaged<T>` and the `CursorPage`).
  A paged answer carries `meta` and links built from the request, keeping the caller's filters and
  sort.
- **Not found is `404`.** Throw `NotFoundException` (Bitwarden.Server.Sdk.Common) and the caller
  gets the standard error document.
- **Reads demand the read scope** unless the operation declares a scope of its own.

## Example

A paged collection and a single widget, both honouring a sparse fieldset:

```csharp
using Bitwarden.Server.Sdk.Common;
using Bitwarden.Server.Sdk.Service;
using Bitwarden.Server.Sdk.Service.Resources;
using Bitwarden.Server.Sdk.Service.Querying;
using static Bitwarden.Server.Sdk.Service.StandardResponses;

public static class WidgetsEndpoints
{
    public const string Collection = "api/v1/widgets";

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var widgets = endpoints.MapGroup(Collection).WithStandards();
        widgets.MapGet("", ListWidgets);
        widgets.MapGet("{id}", GetWidget);
    }

    /// <summary>
    /// List widgets
    /// </summary>
    [StandardReadMany]
    public static async Task<PagedResourceListDocument<Widget>> ListWidgets(
        IWidgetStore store,
        Fields<Widget> fields = default,
        PageNumber pageNumber = default,
        PageSize pageSize = default,
        CancellationToken cancellationToken = default)
    {
        var page = new Page { Number = pageNumber, Size = pageSize };

        // Nothing found is an empty page, never a 404.
        var found = await store.ListAsync(page, cancellationToken);

        return ReadManyResponse(found, page, fields);
    }

    /// <summary>
    /// Get a widget
    /// </summary>
    [StandardReadOne]
    public static async Task<ResourceDocument<Widget>> GetWidget(
        string id, IWidgetStore store, Fields<Widget> fields = default, CancellationToken cancellationToken = default) =>
        ReadOneResponse(
            await store.FindAsync(id, cancellationToken) ?? throw new NotFoundException("widget", id),
            fields);
}
```

`IWidgetStore.ListAsync` returns a `Paged<Widget>`: the page's widgets and the total. A store that
queries with Entity Framework Core uses `Page(page)` and `ToPagedAsync`; one holding a list in
memory uses `ToPaged(page)`. See [Paging](../querying/paging.md).

Reading the second page of one widget each:

```http
GET /api/v1/widgets?page[size]=1&page[number]=2
Accept: application/json
```

```http
HTTP/1.1 200 OK
Content-Type: application/json; charset=utf-8
```

```json
{
    "data": [
        {
            "id": "cacba8c1-29fa-4018-8950-acd400ec76b7",
            "type": "widget",
            "attributes": { "name": "Cog" }
        }
    ],
    "meta": { "pageCount": 2, "pageNumber": 2, "pageSize": 1, "totalCount": 2 },
    "links": {
        "first": "/api/v1/widgets?page[number]=1&page[size]=1",
        "prev": "/api/v1/widgets?page[number]=1&page[size]=1",
        "last": "/api/v1/widgets?page[number]=2&page[size]=1"
    }
}
```

Reading one widget. `notes` is ciphertext, returned as it was stored:

```http
GET /api/v1/widgets/62bed180-1f78-45d4-8a56-c996936a2947
Accept: application/json
```

```json
{
    "data": {
        "id": "62bed180-1f78-45d4-8a56-c996936a2947",
        "type": "widget",
        "attributes": {
            "name": "Sprocket",
            "notes": "2.AAAA|BBBB|CCCC"
        }
    }
}
```

A widget that doesn't exist:

```json
{
    "errors": [
        {
            "status": "404",
            "code": "not-found",
            "title": "The resource does not exist.",
            "detail": "No widget with ID 'missing' exists."
        }
    ]
}
```

## What you decide

- **Whom a read reaches.** Answer `404`, not `403`, for a resource the caller may not know exists,
  and limit a collection to what the caller may see. The package authorizes the operation, not the
  rows. See
  [Authentication and authorization](../beyond-the-standards/authentication-and-authorization.md).
- **What can be queried.** Declare each filter, the sortable fields and the paging style as
  parameters and attributes. Each query has to be indexed and authorized, so offer only what callers
  need. See [Filtering](../querying/filtering.md), [Sorting](../querying/sorting.md) and
  [Sparse fieldsets](../querying/sparse-fieldsets.md).
- **Paged or whole.** Return `ResourceListDocument<T>` only for a collection that is small by
  nature; page anything that grows.
- **Soft-deleted resources.** See [Soft deletes](deleting-resources.md#soft-deletes).

## See also

- [Paging](../querying/paging.md) and [Advanced queries](../querying/advanced-queries.md)
- [Standard responses](../payloads-and-responses/standard-responses.md)
- The standard:
  [Reading resources](https://contributing.bitwarden.com/architecture/server/apis/reading-resources)
