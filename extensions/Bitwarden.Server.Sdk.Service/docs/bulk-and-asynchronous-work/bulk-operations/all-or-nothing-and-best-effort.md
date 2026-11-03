# All-or-nothing and best-effort

When some of the targeted resources fail, an all-or-nothing operation changes nothing and reports
every failure, and a best-effort operation lets each resource succeed or fail on its own. The caller
chooses with the `atomic` query parameter. The package reads `atomic`, refuses a mode the operation
does not support, and gives you the result types for both outcomes. The standard:
[All-or-nothing and best-effort](https://contributing.bitwarden.com/architecture/server/apis/bulk-operations/all-or-nothing-and-best-effort).

## What the package does

| The standard's rule                                                              | The package                                                                                                                                                                                                                     |
| -------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| A service may support either mode or both                                        | `Supports` on every bulk attribute: `Atomicity.AllOrNothing` (the default), `Atomicity.BestEffort`, or both (`Atomicity.AllOrNothing \| Atomicity.BestEffort`).                                                                 |
| Default to all-or-nothing when both are supported (`MUST`)                       | An absent `atomic` means all-or-nothing wherever it is supported, and the operation's only mode otherwise.                                                                                                                      |
| Recognize `atomic`, and refuse an unsupported value with `422` (`MUST`)          | `atomic=true` or `atomic=false` is checked against `Supports`; an unsupported mode is refused `422`, and anything other than `true` or `false` is refused `422`. The parameter is published in the OpenAPI document either way. |
| All-or-nothing reports every failure, not just the first                         | Collect them and throw `BulkRefusedException(errors)`. The response is the standard error document carrying each one, with status `422`.                                                                                        |
| Best-effort: one failure never prevents or undoes another's success (`MUST NOT`) | `BulkResult.BestEffort(affected, failures)` answers `200` with `meta.affectedCount`, `meta.failedCount` (present even when zero) and an `errors` entry for each failure.                                                        |
| Problems with the request itself reject it whole, in either mode                 | `BulkRequests.RequireAtMost` and `RequireDistinct` throw before any resource is touched, and the platform's targeting and expression checks run before your handler.                                                            |

The mode the caller chose, already checked, is `request.Atomicity` on every bulk request type.

## Example

This bulk delete supports both modes. The request-level checks come first; then each widget is
looked at, and the outcome depends on the mode:

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
        endpoints.MapPost($"{Collection}:bulk-delete", DeleteWidgets).WithStandards();

    /// <summary>
    /// Delete widgets
    /// </summary>
    [StandardBulkDelete(Collection, Targeting = Targeting.Ids, Supports = Atomicity.AllOrNothing | Atomicity.BestEffort)]
    public static async Task<BulkResult> DeleteWidgets(
        BulkDelete<Widget> request, IContext context, IWidgetStore store, CancellationToken cancellationToken)
    {
        // Problems with the request itself refuse it whole, in either mode.
        var ids = request.Ids ?? [];
        BulkRequests.RequireAtMost(ids.Count, 500, "/ids");
        BulkRequests.RequireDistinct(ids);

        var failures = new List<Error>();
        var deletable = new List<string>();

        for (var index = 0; index < ids.Count; index++)
        {
            try
            {
                var widget = await store.FindVisibleAsync(context, ids[index], cancellationToken)
                    ?? throw new NotFoundException("widget", ids[index]);
                deletable.Add(widget.Id);
            }
            catch (NotFoundException failure)
            {
                failures.Add(BulkFailure.At(failure, $"/ids/{index}"));
            }
        }

        // request.Atomicity is the caller's atomic parameter, already checked against Supports.
        if (request.Atomicity == Atomicity.AllOrNothing)
        {
            if (failures.Count > 0)
            {
                throw new BulkRefusedException(failures);
            }

            return BulkResult.Affected(await store.DeleteAsync(deletable, cancellationToken));
        }

        // Best-effort: each widget stands alone, and one failure undoes nothing else.
        return BulkResult.BestEffort(await store.DeleteAsync(deletable, cancellationToken), failures);
    }
}
```

All-or-nothing, the default, with one identifier that names nothing. Nothing changes, and the
response carries every failure:

```http
POST /api/v1/widgets:bulk-delete
Content-Type: application/json

{"ids":["62bed180-1f78-45d4-8a56-c996936a2947","7c1d9a3e-0000-4000-8000-000000000000"]}
```

```http
HTTP/1.1 422 Unprocessable Entity
Content-Type: application/json; charset=utf-8

{"errors":[{"status":"404","code":"not-found","title":"The resource does not exist.","detail":"No widget with ID '7c1d9a3e-0000-4000-8000-000000000000' exists.","source":{"pointer":"/ids/1"}}]}
```

The same request, best-effort. The widget that exists is deleted, and the failure is reported:

```http
POST /api/v1/widgets:bulk-delete?atomic=false
Content-Type: application/json

{"ids":["62bed180-1f78-45d4-8a56-c996936a2947","7c1d9a3e-0000-4000-8000-000000000000"]}
```

```http
HTTP/1.1 200 OK
Content-Type: application/json; charset=utf-8

{"meta":{"affectedCount":1,"failedCount":1},"errors":[{"status":"404","code":"not-found","title":"The resource does not exist.","detail":"No widget with ID '7c1d9a3e-0000-4000-8000-000000000000' exists.","source":{"pointer":"/ids/1"}}]}
```

A value that is neither `true` nor `false` is refused whatever the operation supports:

```http
HTTP/1.1 422 Unprocessable Entity
Content-Type: application/json; charset=utf-8

{"errors":[{"status":"422","code":"invalid-value","title":"A field has an invalid value.","detail":"'perhaps' is not a value 'atomic' accepts. It is 'true' for all-or-nothing or 'false' for best-effort.","source":{"pointer":"/atomic"}}]}
```

## What you decide

- **Which modes to offer.** All-or-nothing when a partial result would leave related resources
  inconsistent, or the caller treats the set as one. Best-effort when each resource stands alone
  and the caller can report or retry individual failures.
- **Making all-or-nothing atomic.** The package does not open a transaction. Check every resource
  before writing any, and write the changes in one transaction, so a failure part way through
  leaves nothing changed.
- **An all-or-nothing failure made only of version conflicts.** The standard wants `409` for it.
  `BulkRefusedException` always answers `422`; see [Bulk responses](./bulk-responses.md) for how to
  answer `409`.
- **Which exceptions are per-resource failures.** Catch the ones your rules throw
  (`NotFoundException`, `ConflictException`, `InvalidRequestException` and so on) and pass them to
  `BulkFailure.At`. Anything else is a fault: let it propagate, or note that `BulkFailure.At`
  describes an unrecognized exception as a `500` without its message.

## See also

- [Bulk operations](./index.md)
- [Bulk responses](./bulk-responses.md)
- [Targeting](./targeting.md)
- The standard:
  [All-or-nothing and best-effort](https://contributing.bitwarden.com/architecture/server/apis/bulk-operations/all-or-nothing-and-best-effort)
