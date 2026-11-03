# Synchronous and asynchronous

A synchronous bulk operation answers with its outcome. An asynchronous one answers `202 Accepted`
with a job the caller follows. The package provides synchronous execution only: every bulk
operation answers when the work is done. The standard:
[Synchronous and asynchronous](https://contributing.bitwarden.com/architecture/server/apis/bulk-operations/synchronous-and-asynchronous).

## What the package does

| The standard's rule                                                                                       | The package                                                                                                                                       |
| --------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------- |
| Synchronous: the response carries the outcome                                                             | Every bulk operation answers `200 OK` with what the handler returns, or `204` when it returns nothing. See [Bulk responses](./bulk-responses.md). |
| Asynchronous: `202` and a [job](../jobs.md)                                                               | Not provided. No bulk attribute documents `202`, and the package has no job resource.                                                             |
| A service supporting both responds synchronously unless the caller sends `Prefer: respond-async` (`MUST`) | Not applicable: the package supports one mode. It does not read `Prefer`, and a request carrying it is answered synchronously.                    |
| `Preference-Applied: respond-async` when honoring the preference (`SHOULD`)                               | Not provided.                                                                                                                                     |
| Respond asynchronously above a documented size (`MAY`)                                                    | Not provided. Refuse an oversized request instead, with `BulkRequests.RequireAtMost`.                                                             |

`Prefer` is a preference (RFC 7240), so a synchronous-only operation that ignores it still answers
the caller correctly. The standard requires callers to handle every response the API documents.

## Example

Keep each operation bounded so that answering synchronously stays cheap, and say so in its
documentation:

```csharp
using Bitwarden.Server.Sdk.Service;
using Bitwarden.Server.Sdk.Service.Querying;

[ApiTag(Description = "Widgets, which a person keeps.")]
public static class WidgetsEndpoints
{
    public const string Collection = "api/v1/widgets";

    // The largest request the operation processes while the caller waits.
    public const int MaxIds = 500;

    public static void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapPost($"{Collection}:bulk-archive", ArchiveWidgets).WithStandards();

    /// <summary>
    /// Archive widgets
    /// </summary>
    /// <remarks>
    /// Names the widgets by `ids`, at most 500. All-or-nothing. Responds synchronously: the
    /// response carries the outcome, and a larger request is refused with 422.
    /// </remarks>
    [StandardBulkAction(Collection, "archive", Targeting = Targeting.Ids)]
    public static async Task<BulkResult> ArchiveWidgets(
        BulkAction<Widget> request, IWidgetStore store, CancellationToken cancellationToken)
    {
        var ids = request.Ids ?? [];
        BulkRequests.RequireAtMost(ids.Count, MaxIds, "/ids");
        BulkRequests.RequireDistinct(ids);

        return BulkResult.Affected(await store.ArchiveAsync(ids, cancellationToken));
    }
}
```

A request over the maximum is refused before any widget is touched:

```http
HTTP/1.1 422 Unprocessable Entity
Content-Type: application/json; charset=utf-8

{"errors":[{"status":"422","code":"invalid-value","title":"A field has an invalid value.","detail":"501 were named and this operation takes at most 500 at a time.","source":{"pointer":"/ids"}}]}
```

## What you decide

- **The maximum.** Choose one that keeps the slowest request well inside the time a caller and
  the infrastructure in front of the service will wait. Each resource that raises events or calls
  another service counts for more.
- **Documenting execution.** The standard requires every bulk API to say whether it responds
  synchronously, asynchronously or both. The package does not write this for you; put it in the
  handler's XML documentation, as above.
- **Work that cannot be bounded.** The standard's answer is an asynchronous operation and a job,
  and neither the package nor the standard defines the job resource yet. See [Jobs](../jobs.md)
  before designing one. Where the work is another service reacting to a change, the standard
  prefers events on the messaging plane.

## See also

- [Bulk operations](./index.md)
- [Bulk responses](./bulk-responses.md)
- [Jobs](../jobs.md)
- The standard:
  [Synchronous and asynchronous](https://contributing.bitwarden.com/architecture/server/apis/bulk-operations/synchronous-and-asynchronous)
