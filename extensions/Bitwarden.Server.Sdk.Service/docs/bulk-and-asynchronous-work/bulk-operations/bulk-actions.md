# Bulk actions

A bulk action invokes a single-resource action (`{collection}/{id}/actions/{action}`) on every
targeted resource, at `{collection}:bulk-{action}`, with `data` holding exactly what the single
action takes as its body. The package maps the route, reserves the names of the other bulk
operations, and reads one body for every resource or one per resource. The standard:
[Bulk actions](https://contributing.bitwarden.com/architecture/server/apis/bulk-operations/bulk-actions).

## What the package does

| The standard's rule                                                                                 | The package                                                                                                                                                                                                                                                                               |
| --------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Path `{collection}:bulk-{action}`, named after the single action (`MUST`)                           | `[StandardBulkAction(collection, action)]`. The action's name is kebab-cased; left out, it is the method's name, so name the method after the action or pass the name.                                                                                                                    |
| Not named `create`, `update`, `replace` or `delete` (`MUST NOT`)                                    | The attribute refuses those names, so the service fails to start.                                                                                                                                                                                                                         |
| `data` is the single action's body, applied to every resource; may be omitted                       | `BulkAction<T, TInput>.Data` is a `BulkInput<TInput>`: a JSON object is `Every`, the same body for every resource. `BulkAction<T>` is for an action with no body.                                                                                                                         |
| Per-resource bodies: `data` items each carrying `id` and `body`, with no `ids` or `filter` (`MUST`) | Not as the standard shapes it. The package reads a `data` array as one body per resource, paired by position with `ids`. An array with a `filter`, or of a different length from `ids`, is refused `422` at `/data`. A request with `data` items and no `ids` is refused `422` at `/ids`. |
| A per-resource failure is reported against `/data/{n}`                                              | Yours. With the package's shape, point at `/ids/{n}`, the position that names the resource.                                                                                                                                                                                               |

Targeting is `ids` or `filter`, as `Targeting` allows, and `Supports` sets the atomicity modes.

## Example

`BulkInput<TInput>.At(index)` returns the shared body, or the one in the same position as the
resource, so one loop handles both shapes:

```csharp
using Bitwarden.Server.Sdk.Common;
using Bitwarden.Server.Sdk.Common.Errors;
using Bitwarden.Server.Sdk.Service;
using Bitwarden.Server.Sdk.Service.Resources;
using Bitwarden.Server.Sdk.Service.Querying;
using static Bitwarden.Server.Sdk.Service.StandardResponses;

[ApiTag(Description = "Widgets, which a person keeps.")]
public static class WidgetsEndpoints
{
    public const string Collection = "api/v1/widgets";

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var widgets = endpoints.MapGroup(Collection).WithStandards();
        widgets.MapPost("{id}/actions/polish", Polish);

        endpoints.MapPost($"{Collection}:bulk-polish", BulkPolish).WithStandards();
    }

    /// <summary>
    /// Polish a widget
    /// </summary>
    [StandardAction]
    public static async Task<ResourceDocument<Widget>> Polish(
        string id, PolishWidget request, IWidgetStore store, CancellationToken cancellationToken) =>
        ActionResponse(await store.PolishAsync(id, request.Shine, cancellationToken));

    /// <summary>
    /// Polish widgets
    /// </summary>
    /// <remarks>
    /// Names the widgets by `ids`, at most 100. `data` is one polish for every widget, or an
    /// array of one per widget in the order `ids` names them. Best-effort. Responds synchronously.
    /// </remarks>
    [StandardBulkAction(Collection, "polish", Targeting = Targeting.Ids, Supports = Atomicity.BestEffort)]
    public static async Task<BulkResult> BulkPolish(
        BulkAction<Widget, PolishWidget> request, IWidgetStore store, CancellationToken cancellationToken)
    {
        var ids = request.Ids ?? [];
        BulkRequests.RequireAtMost(ids.Count, 100, "/ids");
        BulkRequests.RequireDistinct(ids);

        if (request.Data is not { } data)
        {
            throw new InvalidRequestException("Say how much to polish.", "/data");
        }

        var failures = new List<Error>();
        var polished = 0;

        for (var index = 0; index < ids.Count; index++)
        {
            // The same body for every widget, or the one in the same position as its id.
            var shine = data.At(index)?.Shine ?? 0;

            try
            {
                await store.PolishAsync(ids[index], shine, cancellationToken);
                polished++;
            }
            catch (NotFoundException failure)
            {
                failures.Add(BulkFailure.At(failure, $"/ids/{index}"));
            }
        }

        return BulkResult.BestEffort(polished, failures);
    }
}

public sealed record PolishWidget(int Shine);
```

One polish for every widget:

```http
POST /api/v1/widgets:bulk-polish
Content-Type: application/json

{"ids":["62bed180-1f78-45d4-8a56-c996936a2947","7c1d9a3e-0000-4000-8000-000000000000"],"data":{"shine":3}}
```

```http
HTTP/1.1 200 OK
Content-Type: application/json; charset=utf-8

{"meta":{"affectedCount":1,"failedCount":1},"errors":[{"status":"404","code":"not-found","title":"The resource does not exist.","detail":"No widget with ID '7c1d9a3e-0000-4000-8000-000000000000' exists.","source":{"pointer":"/ids/1"}}]}
```

A polish each, in the order `ids` names the widgets:

```http
POST /api/v1/widgets:bulk-polish
Content-Type: application/json

{"ids":["62bed180-1f78-45d4-8a56-c996936a2947","cacba8c1-29fa-4018-8950-acd400ec76b7"],"data":[{"shine":3},{"shine":5}]}
```

```http
HTTP/1.1 200 OK
Content-Type: application/json; charset=utf-8

{"meta":{"affectedCount":2,"failedCount":0}}
```

## What you decide

- **One body or one per resource.** Per-resource bodies are for input the server cannot compute,
  such as a key the client wraps for each user: the server never holds the key, so only the client
  can produce one per resource. Everything else takes one body for all.
- **The same rules as the single action.** Each resource goes through the single action's
  authorization, transition rules and side effects. Share one implementation between the two, as
  on [Per-resource rules](./per-resource-rules.md).
- **Status changes in bulk.** A field governed by an action is read-only to a bulk update, so a
  status change for many resources is a bulk action (`:bulk-archive`), not a bulk update.

## See also

- [Bulk operations](./index.md)
- [Per-resource rules](./per-resource-rules.md)
- [Bulk updates](./bulk-updates.md)
- The standard:
  [Bulk actions](https://contributing.bitwarden.com/architecture/server/apis/bulk-operations/bulk-actions)
  and [Acting upon resources](https://contributing.bitwarden.com/architecture/server/apis/acting-upon-resources)
