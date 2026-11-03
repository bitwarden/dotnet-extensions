# Bulk operations

The standard lets a caller create, update, replace, delete or act on many resources in one
request, as shorthand for calling the single-resource API once per resource. The package gives
each bulk operation an attribute and a request type, checks the request's targeting and atomicity
before your handler runs, and publishes both in the OpenAPI document. Every bulk operation it
provides responds synchronously. The standard:
[Bulk operations](https://contributing.bitwarden.com/architecture/server/apis/bulk-operations).

## What the package does

Each operation is a `POST` to a suffix on the collection. The attribute names the collection's path
and the handler takes the matching request type, all in
`Bitwarden.Server.Sdk.Service.Querying` except the attributes, which are in
`Bitwarden.Server.Sdk.Service`.

| Operation                          | Attribute                                    | Request type                               | Route                        | Targeting                                  |
| ---------------------------------- | -------------------------------------------- | ------------------------------------------ | ---------------------------- | ------------------------------------------ |
| [Bulk create](./bulk-creates.md)   | `[StandardBulkCreate(collection)]`           | `BulkCreate<T>`                            | `{collection}:bulk-create`   | `data`                                     |
| [Bulk update](./bulk-updates.md)   | `[StandardBulkUpdate(collection)]`           | `BulkUpdate<T>`                            | `{collection}:bulk-update`   | `ids` or `filter`, plus `update`           |
| [Bulk replace](./bulk-replaces.md) | `[StandardBulkReplace(collection)]`          | `BulkReplace<T>`                           | `{collection}:bulk-replace`  | `data`                                     |
| [Bulk delete](./bulk-deletes.md)   | `[StandardBulkDelete(collection)]`           | `BulkDelete<T>`                            | `{collection}:bulk-delete`   | `ids` or `filter`                          |
| [Bulk action](./bulk-actions.md)   | `[StandardBulkAction(collection, "action")]` | `BulkAction<T>` or `BulkAction<T, TInput>` | `{collection}:bulk-{action}` | `ids` or `filter`, plus an optional `data` |

The standard gives every bulk operation three properties. This is how the package handles each:

| Property                                         | The standard                | The package                                                                                                                                                                            |
| ------------------------------------------------ | --------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| [Targeting](./targeting.md)                      | `data`, `ids` or `filter`   | `Targeting` on the update, delete and action attributes (`Ids`, `Filter`, or `Either`, the default). Exactly one form is accepted, and a `filter` may name only `[Filterable]` fields. |
| [Atomicity](./all-or-nothing-and-best-effort.md) | All-or-nothing, best-effort | `Supports` on every bulk attribute (`Atomicity.AllOrNothing` by default). The caller's `atomic` parameter is checked against it and handed to you as `request.Atomicity`.              |
| [Execution](./synchronous-and-asynchronous.md)   | Synchronous, asynchronous   | Synchronous only. The package has no `202 Accepted`, no [job](../jobs.md) and no handling of `Prefer: respond-async`.                                                                  |

- **Documentation.** The standard requires every bulk API to document its targeting, its atomicity
  modes and default, and whether it responds synchronously. The package publishes the `atomic`
  query parameter with its default and, for a single-mode operation, its only allowed value. It
  writes the request body's description from the attribute, naming the targeting it accepts
  ("named by 'ids'", for example). Say that the operation responds synchronously, and its maximum,
  in the handler's XML documentation.
- **Checked before your handler runs,** and refused `422`:
    - both `ids` and `filter`, neither, or a form the operation does not accept;
    - a `filter` that is empty, uses an operator outside the eleven the standard allows, or names a
      field the resource does not mark `[Filterable]`;
    - an `atomic` value the operation does not support.
- **Success.** A bulk operation answers `200 OK` with what its handler returns: a `BulkResult`
  (counts and, for best-effort, errors) or, for a bulk create, a `BulkCreatedDocument<T>`. A
  handler that returns nothing answers `204`. See [Bulk responses](./bulk-responses.md).
- **The service refuses to start** when a bulk endpoint's `Map` line disagrees with its attribute's
  route, when a handler takes a `BulkUpdate<T>`, `BulkDelete<T>`, `BulkAction<T>` or `Search<T>`
  without the matching attribute (its filter would reach the handler unchecked), and when a bulk
  action is named `create`, `update`, `replace` or `delete`.

## Example

The routes are a suffix on the collection (`api/v1/widgets:bulk-delete`) rather than a segment
beneath it, and a route group can only join with a slash. So declare the collection's path as a
constant, pass it to each bulk attribute, and map bulk endpoints on the route builder itself,
each with `WithStandards()`:

```csharp
using Bitwarden.Server.Sdk.Service;
using Bitwarden.Server.Sdk.Service.Resources;
using Bitwarden.Server.Sdk.Service.Querying;

[ApiTag(Description = "Widgets, which a person keeps.")]
public static class WidgetsEndpoints
{
    public const string Collection = "api/v1/widgets";

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var widgets = endpoints.MapGroup(Collection).WithStandards();
        widgets.MapPost("{id}/actions/archive", ArchiveWidget);

        // Bulk operations are a suffix on the collection, so they are mapped on the route
        // builder itself rather than on the group.
        endpoints.MapPost($"{Collection}:bulk-create", CreateWidgets).WithStandards();
        endpoints.MapPost($"{Collection}:bulk-update", UpdateWidgets).WithStandards();
        endpoints.MapPost($"{Collection}:bulk-replace", ReplaceWidgets).WithStandards();
        endpoints.MapPost($"{Collection}:bulk-delete", DeleteWidgets).WithStandards();
        endpoints.MapPost($"{Collection}:bulk-archive", ArchiveWidgets).WithStandards();
    }

    /// <summary>
    /// Archive a widget
    /// </summary>
    [StandardAction("archive")]
    public static Task ArchiveWidget(string id, IWidgetStore store, CancellationToken cancellationToken) =>
        store.ArchiveAsync([id], cancellationToken);

    /// <summary>
    /// Create widgets
    /// </summary>
    [StandardBulkCreate(Collection)]
    public static async Task<BulkCreatedDocument<Widget>> CreateWidgets(
        BulkCreate<Widget> request, IWidgetStore store, CancellationToken cancellationToken)
    {
        BulkRequests.RequireAtMost(request.Data.Count, 100, "/data");
        return BulkCreatedDocument<Widget>.For(request, await store.AddAllAsync(request.Data, cancellationToken));
    }

    /// <summary>
    /// Update widgets
    /// </summary>
    [StandardBulkUpdate(Collection, Targeting = Targeting.Filter)]
    public static async Task<BulkResult> UpdateWidgets(
        BulkUpdate<Widget> request, IWidgetStore store, CancellationToken cancellationToken) =>
        BulkResult.Affected(await store.UpdateMatchingAsync(request.Filter!, request.Update, cancellationToken));

    /// <summary>
    /// Replace widgets
    /// </summary>
    [StandardBulkReplace(Collection)]
    public static async Task<BulkResult> ReplaceWidgets(
        BulkReplace<Widget> request, IWidgetStore store, CancellationToken cancellationToken)
    {
        BulkRequests.RequireAtMost(request.Data.Count, 100, "/data");
        return BulkResult.Affected(await store.ReplaceAllAsync(request.Data, cancellationToken));
    }

    /// <summary>
    /// Delete widgets
    /// </summary>
    [StandardBulkDelete(Collection, Targeting = Targeting.Ids, Supports = Atomicity.AllOrNothing | Atomicity.BestEffort)]
    public static async Task<BulkResult> DeleteWidgets(
        BulkDelete<Widget> request, IWidgetStore store, CancellationToken cancellationToken)
    {
        var ids = request.Ids ?? [];
        BulkRequests.RequireAtMost(ids.Count, 500, "/ids");
        BulkRequests.RequireDistinct(ids);
        return await store.DeleteAllAsync(ids, request.Atomicity, cancellationToken);
    }

    /// <summary>
    /// Archive widgets
    /// </summary>
    [StandardBulkAction(Collection, "archive", Targeting = Targeting.Ids)]
    public static async Task<BulkResult> ArchiveWidgets(
        BulkAction<Widget> request, IWidgetStore store, CancellationToken cancellationToken)
    {
        var ids = request.Ids ?? [];
        BulkRequests.RequireAtMost(ids.Count, 500, "/ids");
        BulkRequests.RequireDistinct(ids);
        return BulkResult.Affected(await store.ArchiveAsync(ids, cancellationToken));
    }
}
```

A caller asking for a mode an operation does not support is refused before the handler runs:

```http
POST /api/v1/widgets:bulk-archive?atomic=false
Content-Type: application/json

{"ids":[]}
```

```http
HTTP/1.1 422 Unprocessable Entity
Content-Type: application/json; charset=utf-8

{"errors":[{"status":"422","code":"invalid-value","title":"A field has an invalid value.","detail":"This operation is all-or-nothing and does not support 'atomic=false'. Every resource it touches succeeds, or none of them does.","source":{"pointer":"/atomic"}}]}
```

## What you decide

- **Which combination each operation offers.** Targeting, atomicity and execution are each the
  operation's choice. The standard's guide: all-or-nothing when a partial result would leave
  related resources inconsistent, best-effort when each resource stands alone (as in an import),
  and synchronous when the set is small, bounded and cheap to process.
- **The maximum.** Each operation documents how many items it takes and refuses more with
  `BulkRequests.RequireAtMost`. See [Targeting](./targeting.md).
- **The per-resource rules.** The package checks the request. Holding each resource to the
  single-resource API's authorization, validation, business rules and side effects is your
  handler's job. See [Per-resource rules](./per-resource-rules.md).
- **Asynchronous execution.** Not provided. See
  [Synchronous and asynchronous](./synchronous-and-asynchronous.md) and [Jobs](../jobs.md).

## See also

- [Targeting](./targeting.md), [Per-resource rules](./per-resource-rules.md),
  [All-or-nothing and best-effort](./all-or-nothing-and-best-effort.md),
  [Synchronous and asynchronous](./synchronous-and-asynchronous.md) and
  [Bulk responses](./bulk-responses.md)
- [Bulk creates](./bulk-creates.md), [Bulk updates](./bulk-updates.md),
  [Bulk replaces](./bulk-replaces.md), [Bulk deletes](./bulk-deletes.md) and
  [Bulk actions](./bulk-actions.md)
- [Jobs](../jobs.md)
- The standard:
  [Bulk operations](https://contributing.bitwarden.com/architecture/server/apis/bulk-operations)
- `[StandardSearch]` and `Search<T>`, the read-only `POST {collection}:search`, share the
  expression language:
  [Advanced queries](https://contributing.bitwarden.com/architecture/server/apis/advanced-queries)
