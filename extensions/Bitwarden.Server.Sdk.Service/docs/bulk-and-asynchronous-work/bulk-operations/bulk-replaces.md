# Bulk replaces

A bulk replace is `PUT` once per resource in `data`: each item carries its `id`, a field left out is
cleared, and a service with optimistic concurrency checks each item's `version`. The package reads
each item as the single replace reads it; checking the `id`s and versions and applying the replace
is your handler's job. The standard:
[Bulk replaces](https://contributing.bitwarden.com/architecture/server/apis/bulk-operations/bulk-replaces).

## What the package does

| The standard's rule                                                       | The package                                                                                                                                                                                              |
| ------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Each item shaped as the single replace expects it                         | `BulkReplace<T>.Data` holds the resources, each enveloped on the wire with `id`, `type` and `attributes`. An item of another `type` is refused `422` at `/data/{n}/type`.                                |
| Each item carries its `id` (`MUST`)                                       | Not enforced. Refuse an item without one, as below.                                                                                                                                                      |
| `data` never names a resource twice (`MUST NOT`)                          | Not enforced. `BulkRequests.RequireDistinct` points at `/ids/{n}`, so check `data` yourself, as below.                                                                                                   |
| Check each item's `version`; a mismatch is a `409` for that item (`MUST`) | `Concurrency.Require(sent, stored)` throws `ConflictException` when the caller sent a `version` that no longer matches, and passes a caller who sent none. `BulkFailure.At` turns it into a `409` error. |
| Create-or-update when the single `PUT` supports it (`MAY`)                | Yours to implement.                                                                                                                                                                                      |

`[StandardBulkReplace(collection)]` maps `POST {collection}:bulk-replace`, and `Supports` sets the
atomicity modes.

## Example

This replace supports both modes. Request-level problems refuse the whole request; a missing or
changed widget fails on its own:

```csharp
using Bitwarden.Server.Sdk.Common;
using Bitwarden.Server.Sdk.Common.Errors;
using Bitwarden.Server.Sdk.Service;
using Bitwarden.Server.Sdk.Service.Querying;

[ApiTag(Description = "Widgets, which a person keeps.")]
public static class WidgetsEndpoints
{
    public const string Collection = "api/v1/widgets";

    public static void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapPost($"{Collection}:bulk-replace", ReplaceWidgets).WithStandards();

    /// <summary>
    /// Replace widgets
    /// </summary>
    /// <remarks>
    /// At most 100 widgets, each carrying its `id` and, optionally, the `version` it was read
    /// at. All-or-nothing by default, or best-effort with `atomic=false`. Responds synchronously.
    /// </remarks>
    [StandardBulkReplace(Collection, Supports = Atomicity.AllOrNothing | Atomicity.BestEffort)]
    public static async Task<BulkResult> ReplaceWidgets(
        BulkReplace<Widget> request, IContext context, IWidgetStore store, CancellationToken cancellationToken)
    {
        // Request-level checks: too many items, an item without an id, or one named twice.
        BulkRequests.RequireAtMost(request.Data.Count, 100, "/data");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < request.Data.Count; index++)
        {
            var id = request.Data[index].Id;
            if (string.IsNullOrEmpty(id) || !seen.Add(id))
            {
                throw new InvalidRequestException("Each item carries its own id, once.", $"/data/{index}/id");
            }
        }

        var failures = new List<Error>();
        var replaced = new List<WidgetRow>();

        for (var index = 0; index < request.Data.Count; index++)
        {
            var item = request.Data[index];

            try
            {
                var widget = await store.FindVisibleAsync(context, item.Id!, cancellationToken)
                    ?? throw new NotFoundException("widget", item.Id!);

                // 409 when the caller read an older version; a caller who sent none accepts any.
                Concurrency.Require(item.Version, widget.Version);

                // Replace semantics: a field left out is cleared.
                widget.Name = item.Name;
                widget.Notes = item.Notes;
                widget.Version++;
                replaced.Add(widget);
            }
            catch (Exception failure) when (failure is NotFoundException or ConflictException)
            {
                failures.Add(BulkFailure.At(failure, $"/data/{index}"));
            }
        }

        if (request.Atomicity == Atomicity.AllOrNothing && failures.Count > 0)
        {
            // 409 when every failure is a conflict, 422 otherwise: see the bulk responses page.
            throw BulkRefusals.For(failures);
        }

        await store.SaveAsync(replaced, cancellationToken);

        return request.Atomicity == Atomicity.AllOrNothing
            ? BulkResult.Affected(replaced.Count)
            : BulkResult.BestEffort(replaced.Count, failures);
    }
}
```

`BulkRefusals.For` is the helper from [Bulk responses](./bulk-responses.md): it answers `409`
when every failure is a conflict and `422` otherwise. An all-or-nothing replace sent with an old
version:

```http
POST /api/v1/widgets:bulk-replace
Content-Type: application/json

{"data":[{"id":"62bed180-1f78-45d4-8a56-c996936a2947","type":"widget","attributes":{"name":"Sprocket","version":"3"}}]}
```

```http
HTTP/1.1 409 Conflict
Content-Type: application/json; charset=utf-8

{"errors":[{"status":"409","code":"conflict","title":"The request conflicts with the current state.","detail":"The resource has changed since it was last read. Read it again and retry.","source":{"pointer":"/data/0"}}]}
```

The same item beside a current one, best-effort:

```http
POST /api/v1/widgets:bulk-replace?atomic=false
Content-Type: application/json

{"data":[{"id":"62bed180-1f78-45d4-8a56-c996936a2947","type":"widget","attributes":{"name":"Sprocket","version":"3"}},{"id":"cacba8c1-29fa-4018-8950-acd400ec76b7","type":"widget","attributes":{"name":"Gear","version":"7"}}]}
```

```http
HTTP/1.1 200 OK
Content-Type: application/json; charset=utf-8

{"meta":{"affectedCount":1,"failedCount":1},"errors":[{"status":"409","code":"conflict","title":"The request conflicts with the current state.","detail":"The resource has changed since it was last read. Read it again and retry.","source":{"pointer":"/data/0"}}]}
```

## What you decide

- **The maximum,** documented and enforced with `RequireAtMost`.
- **Whether a missing widget is created.** Only if the single `PUT` does so. Otherwise it is a
  `404` failure of that item, as above.
- **Where a failure points.** The example points at the item (`/data/{n}`). A validation failure
  of one attribute can point deeper (`/data/{n}/attributes/name`).

## See also

- [Bulk operations](./index.md)
- [Bulk updates](./bulk-updates.md), which change some fields rather than replacing all of them
- [Bulk responses](./bulk-responses.md)
- The standard:
  [Bulk replaces](https://contributing.bitwarden.com/architecture/server/apis/bulk-operations/bulk-replaces),
  [Updating resources](https://contributing.bitwarden.com/architecture/server/apis/updating-resources)
  and [Optimistic concurrency](https://contributing.bitwarden.com/architecture/server/apis/optimistic-concurrency)
