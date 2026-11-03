# Deleting resources

The standard deletes a resource with `DELETE /api/v1/{plural}/{id}` and answers `204`. A service may
soft delete instead, with `permanent=true` for a hard delete and `includeDeleted=true` to see what
was deleted. The package gives `[StandardDelete]` and every resource `DeletedAt` and `DeletedBy`;
soft deletes are the service's to build. The standard:
[Deleting resources](https://contributing.bitwarden.com/architecture/server/apis/deleting-resources).

## What the package does

| The standard                          | The package                                                                                |
| ------------------------------------- | ------------------------------------------------------------------------------------------ |
| The verb `MUST` be `DELETE`           | `[StandardDelete]` is a `DELETE`; the service won't start if its `Map` line uses another.  |
| The path `SHOULD` be `/{plural}/{id}` | `[StandardDelete]` defaults to `{id}`; the `Map` line must use it.                         |
| Success `SHOULD` be `204`             | `204` when the handler returns nothing (`Task` or `void`).                                 |
| `MAY` be `202` with a job             | Return an `IResult` of your own. See [Jobs](../bulk-and-asynchronous-work/jobs.md).        |
| Soft deletes                          | Not built in. A `bool` query parameter, a filter in your store and the fields below do it. |
| `deletedBy` and `deletedDate` fields  | Every `Resource` has read-only `DeletedBy` and `DeletedAt` (`deletedAt` on the wire).      |

- **A missing resource is `404`.** Throw `NotFoundException`; the package answers in the standard
  error document.
- **Query flags are checked.** A `bool` parameter that doesn't parse (`permanent=maybe`) is refused
  `422`, naming the parameter.

## Example

A hard delete:

```csharp
using Bitwarden.Server.Sdk.Common;
using Bitwarden.Server.Sdk.Service;

public static class WidgetsEndpoints
{
    public const string Collection = "api/v1/widgets";

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var widgets = endpoints.MapGroup(Collection).WithStandards();
        widgets.MapDelete("{id}", DeleteWidget);
    }

    /// <summary>
    /// Delete a widget
    /// </summary>
    [StandardDelete]
    public static async Task DeleteWidget(string id, IWidgetStore store, CancellationToken cancellationToken)
    {
        if (!await store.DeleteAsync(id, cancellationToken))
        {
            throw new NotFoundException("widget", id);
        }
    }
}
```

```http
DELETE /api/v1/widgets/62bed180-1f78-45d4-8a56-c996936a2947
```

```http
HTTP/1.1 204 No Content
```

## Soft deletes

A soft delete records when and by whom the resource was deleted, and reads leave it out unless the
caller asks. The delete takes `permanent`, and the reads take `includeDeleted`:

```csharp
using Bitwarden.Server.Sdk.Common;
using Bitwarden.Server.Sdk.Service;
using Bitwarden.Server.Sdk.Service.Resources;
using Bitwarden.Server.Sdk.Service.Querying;
using Microsoft.AspNetCore.Mvc;
using static Bitwarden.Server.Sdk.Service.StandardResponses;

public static class SoftDeletingWidgetsEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var widgets = endpoints.MapGroup(WidgetsEndpoints.Collection).WithStandards();
        widgets.MapGet("", ListWidgets);
        widgets.MapGet("{id}", GetWidget);
        widgets.MapDelete("{id}", DeleteWidget);
    }

    /// <summary>
    /// Delete a widget
    /// </summary>
    /// <param name="permanent">Delete it for good rather than move it to the trash.</param>
    [StandardDelete]
    public static async Task DeleteWidget(
        string id,
        IWidgetStore store,
        IContext context,
        [FromQuery] bool permanent = false,
        CancellationToken cancellationToken = default)
    {
        var deleted = permanent
            ? await store.DeleteAsync(id, cancellationToken)
            // Sets DeletedAt to now and DeletedBy to the acting user.
            : await store.TrashAsync(id, context.UserId, cancellationToken);

        if (!deleted)
        {
            throw new NotFoundException("widget", id);
        }
    }

    /// <summary>
    /// Get a widget
    /// </summary>
    /// <param name="includeDeleted">Find it even when it is in the trash.</param>
    [StandardReadOne]
    public static async Task<ResourceDocument<Widget>> GetWidget(
        string id,
        IWidgetStore store,
        [FromQuery] bool includeDeleted = false,
        CancellationToken cancellationToken = default)
    {
        var widget = await store.FindAsync(id, cancellationToken);

        // A widget in the trash is not found unless the caller asks for it.
        if (widget is null || (widget.DeletedAt is not null && !includeDeleted))
        {
            throw new NotFoundException("widget", id);
        }

        return ReadOneResponse(widget);
    }

    /// <summary>
    /// List widgets
    /// </summary>
    /// <param name="includeDeleted">Include widgets in the trash.</param>
    [StandardReadMany]
    public static async Task<PagedResourceListDocument<Widget>> ListWidgets(
        IWidgetStore store,
        [FromQuery] bool includeDeleted = false,
        PageNumber pageNumber = default,
        PageSize pageSize = default,
        CancellationToken cancellationToken = default)
    {
        var page = new Page { Number = pageNumber, Size = pageSize };

        return ReadManyResponse(await store.ListAsync(page, includeDeleted, cancellationToken), page);
    }
}
```

After `DELETE /api/v1/widgets/62bed180-1f78-45d4-8a56-c996936a2947` (`204`), a plain read answers
`404`, and a read that asks for deleted widgets finds it. (This call named no person, so
`IContext.UserId` was null and there is no `deletedBy`.)

```http
GET /api/v1/widgets/62bed180-1f78-45d4-8a56-c996936a2947?includeDeleted=true
Accept: application/json
```

```json
{
    "data": {
        "id": "62bed180-1f78-45d4-8a56-c996936a2947",
        "type": "widget",
        "attributes": {
            "name": "Sprocket",
            "deletedAt": "2026-11-03T14:10:00Z"
        }
    }
}
```

The paging links carry `includeDeleted`, so a caller following `next` stays in the same query:

```http
GET /api/v1/widgets?includeDeleted=true
```

```json
{
    "data": [
        {
            "id": "62bed180-1f78-45d4-8a56-c996936a2947",
            "type": "widget",
            "attributes": {
                "name": "Sprocket",
                "deletedAt": "2026-11-03T14:10:00Z"
            }
        }
    ],
    "meta": {
        "pageCount": 1,
        "pageNumber": 1,
        "pageSize": 50,
        "totalCount": 1
    },
    "links": {
        "first": "/api/v1/widgets?includeDeleted=true&page[number]=1&page[size]=50",
        "last": "/api/v1/widgets?includeDeleted=true&page[number]=1&page[size]=50"
    }
}
```

`DELETE /api/v1/widgets/62bed180-1f78-45d4-8a56-c996936a2947?permanent=true` removes it for good.

## What you decide

- **Whether to soft delete.** The standard leaves it to the service. If you do, a delete is soft by
  default and reads exclude soft-deleted resources by default.
- **Who may see and purge deleted resources.** `includeDeleted` and `permanent` are ordinary
  parameters. Refuse them, or give them a scope of their own, where a caller shouldn't have them.
- **Filling `DeletedAt` and `DeletedBy`.** The package drops what a caller sends for them and
  doesn't set them. `IContext.UserId` names the acting user. The standard calls the date
  `deletedDate`; the package's member is `DeletedAt`.
- **Restoring.** An undelete changes a resource without being an update: make it an
  [action](acting-upon-resources.md), such as `POST /api/v1/widgets/{id}/actions/restore`.
- **Encrypted data in the trash** stays as the ciphertext it was. Delete it for good when the
  standard's `permanent=true` or your retention rule says so.

## See also

- [Reading resources](reading-resources.md)
- [Standard fields](../payloads-and-responses/standard-fields.md)
- [Bulk deletes](../bulk-and-asynchronous-work/bulk-operations/bulk-deletes.md)
- The standard:
  [Deleting resources](https://contributing.bitwarden.com/architecture/server/apis/deleting-resources)
