# Bulk responses

A bulk operation answers `200` with how many resources succeeded (and, for best-effort, how many
failed and why), `202` with a job, or `204` with nothing. An all-or-nothing failure is an ordinary
error document carrying every failure. The package gives you `BulkResult`, `BulkCreatedDocument<T>`,
`BulkFailure` and `BulkRefusedException` to build these. The standard:
[Bulk responses](https://contributing.bitwarden.com/architecture/server/apis/bulk-operations/bulk-responses).

## What the package does

| The standard's rule                                                                                            | The package                                                                                                                                                                                                                                                   |
| -------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `200`, with `meta.affectedCount` (`MUST`)                                                                      | `BulkResult.Affected(count)`, `BulkResult.BestEffort(...)` and `BulkCreatedDocument<T>.For(...)` all write it. A bulk handler that returns one of them answers `200`.                                                                                         |
| `meta.failedCount` on best-effort (`MUST`)                                                                     | `BulkResult.BestEffort(affected, failures)` always writes it, even when it is `0`, and leaves `errors` out when there are none.                                                                                                                               |
| `data` on creates (`SHOULD`), in request order (`MUST`)                                                        | `BulkCreatedDocument<T>.For(request, created)` returns the created resources and throws unless there is exactly one for each item. See [Bulk creates](./bulk-creates.md).                                                                                     |
| `data` on updates and replaces (`MAY`)                                                                         | No document type. `BulkResult` carries counts only.                                                                                                                                                                                                           |
| `errors` on best-effort: at least one per failed resource                                                      | `BulkFailure.At(exception, pointer)` builds one.                                                                                                                                                                                                              |
| Each error identifies its resource by `source.pointer`, or by `meta.resource` with `filter` targeting (`MUST`) | `BulkFailure.At` sets `source.pointer`. For a filter-matched resource, replace it with `meta.resource`, a `FailedResource(type, id)` from Bitwarden.Server.Sdk.Common, as below.                                                                              |
| An error's status is what the single-resource API would return (`MUST`)                                        | `BulkFailure.At` uses the same exception mapping as the exception handler: `404` for `NotFoundException`, `409` for `ConflictException`, `422` for `InvalidRequestException` (keeping its `code`), and a `500` without the message for anything unrecognized. |
| All-or-nothing failure: `409` if every failure is a version conflict, `422` otherwise (`MUST`)                 | `BulkRefusedException(errors)` answers `422` with every error. It never answers `409`; throw your own `IProvideErrors` for that case, as below.                                                                                                               |
| `lid` echoed on the created resource when accepted (`MUST`)                                                    | The platform reads and checks each `lid`, and `BulkCreatedDocument<T>` echoes it beside the created resource's `id`.                                                                                                                                          |
| `202 Accepted` with a job                                                                                      | Not provided. See [Synchronous and asynchronous](./synchronous-and-asynchronous.md).                                                                                                                                                                          |
| `204 No Content`, all-or-nothing only                                                                          | A bulk handler that returns nothing (`Task`) answers `204`, and the OpenAPI document says so. The package does not stop a best-effort operation doing the same; don't, because its caller needs `failedCount`.                                                |

## Example

A failed resource is identified by its position when the caller named it, and by `meta.resource`
when a filter matched it:

```csharp
using Bitwarden.Server.Sdk.Common;
using Bitwarden.Server.Sdk.Common.Errors;
using Bitwarden.Server.Sdk.Service.Querying;

public static class WidgetBulkFailures
{
    /// <summary>
    /// A widget named in the request, by its position there.
    /// </summary>
    public static Error Named(Exception failure, int index) =>
        BulkFailure.At(failure, $"/ids/{index}");

    /// <summary>
    /// A widget a filter matched, which has no position in the request to point at.
    /// </summary>
    public static Error Matched(Exception failure, string id) =>
        BulkFailure.At(failure, "/filter") with
        {
            Source = null,
            Meta = new Dictionary<string, object?> { ["resource"] = new FailedResource("widget", id) },
        };
}
```

The standard asks an all-or-nothing failure made only of version conflicts to answer `409`.
`IProvideErrors` (Bitwarden.Server.Sdk.Common) lets an exception choose its status and carry many
errors, which the exception handler writes as the standard error document:

```csharp
/// <summary>
/// An all-or-nothing refusal in which every failure is a version conflict: a 409.
/// </summary>
public sealed class BulkConflictException(IReadOnlyList<Error> errors)
    : Exception("The request was refused; nothing was changed."), IProvideErrors
{
    public int StatusCode => StatusCodes.Status409Conflict;

    public IReadOnlyList<Error> Errors { get; } = errors;
}

public static class BulkRefusals
{
    /// <summary>
    /// The refusal the standard asks for: 409 when every failure is a conflict, 422 otherwise.
    /// </summary>
    public static Exception For(IReadOnlyList<Error> failures) =>
        failures.All(failure => failure.Status == "409")
            ? new BulkConflictException(failures)
            : new BulkRefusedException(failures);
}
```

A best-effort response with one failure, from the delete on
[All-or-nothing and best-effort](./all-or-nothing-and-best-effort.md):

```http
HTTP/1.1 200 OK
Content-Type: application/json; charset=utf-8

{"meta":{"affectedCount":1,"failedCount":1},"errors":[{"status":"404","code":"not-found","title":"The resource does not exist.","detail":"No widget with ID '7c1d9a3e-0000-4000-8000-000000000000' exists.","source":{"pointer":"/ids/1"}}]}
```

An all-or-nothing refusal of a filter-targeted bulk update, identifying the widget by
`meta.resource` (from [Bulk updates](./bulk-updates.md)):

```http
HTTP/1.1 422 Unprocessable Entity
Content-Type: application/json; charset=utf-8

{"errors":[{"status":"422","code":"archived","title":"A field has an invalid value.","detail":"An archived widget cannot be changed.","meta":{"resource":{"type":"widget","id":"0d4d2a6e-8a0f-4d55-9d0c-0f8f8b3b2f10"}}}]}
```

An all-or-nothing bulk replace whose only failure is a version conflict, answered with
`BulkRefusals.For` (from [Bulk replaces](./bulk-replaces.md)):

```http
HTTP/1.1 409 Conflict
Content-Type: application/json; charset=utf-8

{"errors":[{"status":"409","code":"conflict","title":"The request conflicts with the current state.","detail":"The resource has changed since it was last read. Read it again and retry.","source":{"pointer":"/data/0"}}]}
```

## What you decide

- **Whether updates and replaces return `data`.** The standard allows it; the package has no
  document for counts and resources together, so `BulkResult` (counts only) is the supported
  answer.
- **Best-effort creates.** `BulkCreatedDocument<T>` is for all-or-nothing creates and carries no
  `errors`. A best-effort create can answer `BulkResult.BestEffort`, which reports the failures but
  not the created resources. See [Bulk creates](./bulk-creates.md).
- **The `code` of each failure.** `InvalidRequestException` takes a `code` argument; give each
  business rule its own (`name-in-use`, `archived`) so callers can tell them apart.

## See also

- [Bulk operations](./index.md)
- [All-or-nothing and best-effort](./all-or-nothing-and-best-effort.md)
- [Bulk creates](./bulk-creates.md)
- The standard:
  [Bulk responses](https://contributing.bitwarden.com/architecture/server/apis/bulk-operations/bulk-responses)
  and [Errors](https://contributing.bitwarden.com/architecture/server/apis/errors)
