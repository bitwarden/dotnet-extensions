# Sparse fieldsets

An API may let a caller choose which fields come back; `id` and `type` always do. The package
offers this as a `Fields<T>` parameter, named `fields[{type}]`, validates the
names, and leaves the other attributes out of the response. The standard:
[Sparse fieldsets](https://contributing.bitwarden.com/architecture/server/apis/sparse-fieldsets).

## What the package does

| The standard                                                   | The package                                                                                                                                                                              |
| -------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `MAY` let callers choose fields                                | Opt in per operation with a `Fields<T> fields = default` parameter.                                                                                                                      |
| `MUST` accept a `fields` parameter, a comma-delimited list     | The parameter is `fields[{type}]`, for example `fields[widget]=name,notes`, not `fields`. A plain `fields` parameter is ignored.                                                         |
| `id` and `type` are always returned                            | They are.                                                                                                                                                                                |
| A `fields` naming an unsupported field `MUST` be refused `422` | A name that isn't one of the resource's attributes is refused `422` at the parameter. Every published attribute is allowed, including the inherited `createdAt`, `version` and the rest. |

- **No fieldset is every attribute.** `default`, and a request without the parameter, return the
  full resource, so offering a fieldset never changes what an existing caller gets.
- **The response does the work.** Pass `fields` to `ReadOneResponse(resource, fields)` or
  `ReadManyResponse(page, requested, fields)`, and attributes outside it are left out when the
  document is written. Offset page links carry the fieldset.
- **`fields.Includes("name")`** says whether the caller wants an attribute, so you can skip the work
  of producing one they left out. It is true when no fieldset was sent.
- **The OpenAPI document** publishes `fields[widget]` with the attributes it accepts.

## Example

A read that only counts a widget's gadgets when the caller asks for the count, and a list that
honours the fieldset, with Entity Framework Core:

```csharp
using System.ComponentModel.DataAnnotations;
using Bitwarden.Server.Sdk.Common;
using Bitwarden.Server.Sdk.Service;
using Bitwarden.Server.Sdk.Service.Resources;
using Bitwarden.Server.Sdk.Service.Querying;
using Microsoft.EntityFrameworkCore;
using static Bitwarden.Server.Sdk.Service.StandardResponses;

[Sortable(nameof(CreatedAt), Default = true, Descending = true)]
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

    /// <summary>
    /// How many gadgets use the widget. Counted when read.
    /// </summary>
    [ReadOnly]
    public int? GadgetCount { get; init; }
}

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
    /// Get a widget
    /// </summary>
    [StandardReadOne]
    public static async Task<ResourceDocument<Widget>> GetWidget(
        Guid id,
        WidgetDatabase database,
        Fields<Widget> fields = default,
        CancellationToken cancellationToken = default)
    {
        var entity = await database.Widgets.AsNoTracking()
            .SingleOrDefaultAsync(widget => widget.Id == id, cancellationToken)
            ?? throw new NotFoundException("widget", id.ToString());

        // Only count the gadgets when the caller wants the count.
        int? gadgets = fields.Includes("gadgetCount")
            ? await database.Gadgets.CountAsync(gadget => gadget.WidgetId == id, cancellationToken)
            : null;

        return ReadOneResponse(ToResource(entity, gadgets), fields);
    }

    /// <summary>
    /// List widgets
    /// </summary>
    [StandardReadMany]
    public static async Task<PagedResourceListDocument<Widget>> ListWidgets(
        WidgetDatabase database,
        Fields<Widget> fields = default,
        PageNumber pageNumber = default,
        PageSize pageSize = default,
        CancellationToken cancellationToken = default)
    {
        var page = new Page { Number = pageNumber, Size = pageSize };
        var widgets = database.Widgets.AsNoTracking();

        var rows = await widgets
            .SortedBy(Sort.Default<Widget>(), widget => widget.Id)
            .Page(page)
            .ToListAsync(cancellationToken);

        return ReadManyResponse(
            await rows.ToPagedAsync(page, row => ToResource(row, gadgets: null), widgets.CountAsync, cancellationToken),
            page,
            fields);
    }

    private static Widget ToResource(WidgetEntity entity, int? gadgets) =>
        new()
        {
            Id = entity.Id.ToString(),
            Name = entity.Name,
            Notes = entity.Notes,
            CreatedAt = entity.CreatedAt,
            GadgetCount = gadgets,
        };
}
```

`[ReadOnly]` here is `Bitwarden.Server.Sdk.Service.Resources.ReadOnlyAttribute`: a caller reads the
count but never sets it. The fieldset only shapes the response, so the same code serves a resource
built from an in-memory collection.

```http
GET /api/v1/widgets/a0000000-0000-4000-8000-000000000001?fields[widget]=name,gadgetCount
```

```json
{
    "data": {
        "id": "a0000000-0000-4000-8000-000000000001",
        "type": "widget",
        "attributes": { "name": "Widget 1", "gadgetCount": 2 }
    }
}
```

A list with a fieldset carries it in its page links:

```http
GET /api/v1/widgets?fields[widget]=name&page[size]=1
```

```json
{
    "data": [
        {
            "id": "a0000000-0000-4000-8000-000000000002",
            "type": "widget",
            "attributes": { "name": "Widget 2" }
        }
    ],
    "meta": { "pageCount": 2, "pageNumber": 1, "pageSize": 1, "totalCount": 2 },
    "links": {
        "first": "/api/v1/widgets?fields%5Bwidget%5D=name&page[number]=1&page[size]=1",
        "next": "/api/v1/widgets?fields%5Bwidget%5D=name&page[number]=2&page[size]=1",
        "last": "/api/v1/widgets?fields%5Bwidget%5D=name&page[number]=2&page[size]=1"
    }
}
```

A name that isn't an attribute is refused:

```http
GET /api/v1/widgets/a0000000-0000-4000-8000-000000000001?fields[widget]=colour
```

```json
{
    "errors": [
        {
            "status": "422",
            "code": "invalid-value",
            "title": "A field has an invalid value.",
            "detail": "The 'fields[widget]' parameter names 'colour', which is not one of the resource's attributes.",
            "source": { "parameter": "fields[widget]" }
        }
    ]
}
```

`fields[widget]=` with no names returns `id`, `type` and empty `attributes`.

## What you decide

- **Whether to offer it.** It pays off when an attribute costs something to produce. Check
  `fields.Includes` before producing it.
- **The parameter's name.** The package names it `fields[{type}]`, where the standard
  shows a plain `fields`. A caller that sends `fields=name` gets every attribute back. There is no
  setting to accept the plain form.
- **Cursor-paged lists.** `ReadManyResponse` for a cursor page takes no fieldset, so a
  cursor-paged operation can't apply one. Leave `Fields<T>` off those operations.

## See also

- [Filtering](filtering.md), [Paging](paging.md) and [Sorting](sorting.md): the other query
  parameters.
- The standard:
  [Sparse fieldsets](https://contributing.bitwarden.com/architecture/server/apis/sparse-fieldsets).
