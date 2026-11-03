# Bulk creates

A bulk create is the equivalent of creating each resource in `data`, and should answer with the
created resources so the caller learns the identifiers it did not choose. The package reads each
item as the single-resource create reads it, reads and checks the optional `lid`, and answers with
the created resources in request order. The standard:
[Bulk creates](https://contributing.bitwarden.com/architecture/server/apis/bulk-operations/bulk-creates).

## What the package does

- **`[StandardBulkCreate(collection)]`** maps `POST {collection}:bulk-create`. `Supports` sets the
  atomicity modes (all-or-nothing by default).
- **`BulkCreate<T>`** carries `Data`, the resources, each enveloped on the wire with `type` and
  `attributes` as a single create takes it. An item of another `type` is refused `422` at
  `/data/{n}/type`, and each item is bound and validated as the single create's resource is.
- **`lid`.** Each item may carry a `lid` beside its `type`. The platform refuses one that is not a
  non-empty string, or that two items share, `422` at `/data/{n}/lid`, and keeps them in
  `BulkCreate<T>.Lids` (`LidAt(index)` reads one).
- **`BulkCreatedDocument<T>.For(request, created)`** answers with the created resources in request
  order, each item's `lid` beside the created resource's `id`, and `meta.affectedCount`. It throws
  `ArgumentException` unless there is exactly one created resource for each item.
- **Not checked by the package:** the maximum (call `BulkRequests.RequireAtMost`) and the single
  create's business rules.

## Example

The single create's rule (a widget's name is unique) is applied to every item, including against
earlier items in the same request, before anything is created:

```csharp
using Bitwarden.Server.Sdk.Common;
using Bitwarden.Server.Sdk.Common.Errors;
using Bitwarden.Server.Sdk.Service;
using Bitwarden.Server.Sdk.Service.Resources;
using Bitwarden.Server.Sdk.Service.Querying;

[ApiTag(Description = "Widgets, which a person keeps.")]
public static class WidgetsEndpoints
{
    public const string Collection = "api/v1/widgets";

    public static void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapPost($"{Collection}:bulk-create", CreateWidgets).WithStandards();

    /// <summary>
    /// Create widgets
    /// </summary>
    /// <remarks>
    /// At most 100 widgets, each as a single create takes it, with an optional `lid`.
    /// All-or-nothing. Responds synchronously.
    /// </remarks>
    [StandardBulkCreate(Collection)]
    public static async Task<BulkCreatedDocument<Widget>> CreateWidgets(
        BulkCreate<Widget> request, IContext context, IWidgetStore store, CancellationToken cancellationToken)
    {
        BulkRequests.RequireAtMost(request.Data.Count, 100, "/data");

        // The single create's business rule, applied to every item: a name is unique.
        var failures = new List<Error>();
        for (var index = 0; index < request.Data.Count; index++)
        {
            var name = request.Data[index].Name;
            var takenEarlier = request.Data.Take(index).Any(earlier => earlier.Name == name);

            if (takenEarlier || await store.NameTakenAsync(context, name, cancellationToken))
            {
                failures.Add(BulkFailure.At(
                    new InvalidRequestException($"A widget named '{name}' already exists.", code: "name-in-use"),
                    $"/data/{index}/attributes/name"));
            }
        }

        if (failures.Count > 0)
        {
            throw new BulkRefusedException(failures);
        }

        var created = await store.AddAllAsync(context, request.Data, cancellationToken);

        // Echoes each item's lid beside the created widget's id, in request order.
        return BulkCreatedDocument<Widget>.For(request, created);
    }
}
```

A caller creating two widgets, one with a `lid`:

```http
POST /api/v1/widgets:bulk-create
Content-Type: application/json

{"data":[{"type":"widget","lid":"first","attributes":{"name":"Flange","notes":"2.AAAA|BBBB|CCCC"}},{"type":"widget","attributes":{"name":"Bracket"}}]}
```

```http
HTTP/1.1 200 OK
Content-Type: application/json; charset=utf-8

{"data":[{"id":"3f2b8c6e-1d4a-4b9e-8f7a-2c5d6e7f8a9b","type":"widget","lid":"first","attributes":{"name":"Flange","notes":"2.AAAA|BBBB|CCCC","version":"1"}},{"id":"a1b2c3d4-e5f6-4a7b-8c9d-0e1f2a3b4c5d","type":"widget","attributes":{"name":"Bracket","version":"1"}}],"meta":{"affectedCount":2}}
```

`notes` is Protected Data, encrypted on the person's device. The service stores and returns it as
sent and never reads it.

An item that breaks the rule refuses the whole request:

```http
HTTP/1.1 422 Unprocessable Entity
Content-Type: application/json; charset=utf-8

{"errors":[{"status":"422","code":"name-in-use","title":"A field has an invalid value.","detail":"A widget named 'Sprocket' already exists.","source":{"pointer":"/data/1/attributes/name"}}]}
```

## What you decide

- **The maximum,** documented on the operation and enforced with `RequireAtMost`.
- **Whether to accept `lid`.** The platform always reads it. Answering with
  `BulkCreatedDocument<T>.For` echoes it, which the standard requires of an API that accepts it.
- **Best-effort.** `BulkCreatedDocument<T>` has no `errors` and requires one created resource per
  item, so it is for all-or-nothing creates. A best-effort create can answer
  `BulkResult.BestEffort(created, failures)`, which reports each failure but not the created
  resources or their identifiers. The standard says a bulk create should return them, so prefer
  all-or-nothing where the caller needs the identifiers.

## See also

- [Bulk operations](./index.md)
- [Bulk responses](./bulk-responses.md)
- [Per-resource rules](./per-resource-rules.md)
- The standard:
  [Bulk creates](https://contributing.bitwarden.com/architecture/server/apis/bulk-operations/bulk-creates)
  and [Creating resources](https://contributing.bitwarden.com/architecture/server/apis/creating-resources)
