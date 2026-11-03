# Optimistic concurrency

The standard detects concurrent changes with a `version` field: the caller sends back the version it
read, a stale one is refused `409 Conflict`, and each successful update changes the version. It
uses the field instead of `ETag` and `If-Match`. The package gives every resource a `Version` and
gives you `Concurrency.Require` for the check; storing and incrementing the version is the
service's. The standard:
[Optimistic concurrency](https://contributing.bitwarden.com/architecture/server/apis/optimistic-concurrency).

## What the package does

| The standard                                            | The package                                                                                            |
| ------------------------------------------------------- | ------------------------------------------------------------------------------------------------------ |
| Updates `SHOULD` implement optimistic concurrency       | `Concurrency.Require(sent, stored)` (`Bitwarden.Server.Sdk.Service.Querying`), called by your handler. |
| Using a `version` field, not `readOnly`                 | Every `Resource` has `Version`, a `string?` that a caller may send.                                    |
| The `version` field marked `required`                   | Not done: `version` is optional in the OpenAPI document and in binding. Refuse a missing one yourself. |
| `SHOULD` be an integer, `MUST` be formatted as a string | `Concurrency.Require` compares the sent string with an `int` stored version, formatted invariantly.    |
| A version that doesn't match: `409 Conflict`            | `Concurrency.Require` throws `ConflictException`, answered `409` with the `conflict` code.             |
| A successful update `MUST` update the version           | Not done: your store increments it.                                                                    |
| `ETag`, `If-Match` and `412`                            | Not used, as the standard says. The package sets no `ETag`.                                            |

- **A caller who sends no version passes.** `Concurrency.Require` checks only a version the caller
  sent. An empty string is treated as absent when the document is bound, so it passes too.
- **The same check for bulk replaces.** Each resource in a bulk replace carries its own `version`;
  call `Concurrency.Require` for each.

## Example

The handler checks the sent version against the stored one, and the store writes only if nobody has
written since:

```csharp
using System.Globalization;
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
        widgets.MapPut("{id}", UpdateWidget);
    }

    /// <summary>
    /// Replace a widget
    /// </summary>
    [StandardUpdate]
    public static async Task<ResourceDocument<Widget>> UpdateWidget(
        string id, ResourceDocument<Widget> request, IWidgetStore store, CancellationToken cancellationToken)
    {
        var stored = await store.FindAsync(id, cancellationToken) ?? throw new NotFoundException("widget", id);

        // 409 when the caller sent a version that is no longer the stored one.
        Concurrency.Require(request.Data.Version, stored.Version);

        // The store writes only if the row is still at stored.Version, and adds one to it.
        var replaced = await store.ReplaceAsync(id, request.Data, stored.Version, cancellationToken)
            ?? throw new ConflictException();

        return UpdateResponse(ToResource(replaced));
    }

    private static Widget ToResource(WidgetEntity entity) => new()
    {
        Id = entity.Id,
        Name = entity.Name,
        Notes = entity.Notes,
        Version = entity.Version.ToString(CultureInfo.InvariantCulture),
    };
}
```

`Concurrency.Require` catches a caller who read an old version. The store's conditional write
catches two callers who read the same version and write at once: with Entity Framework Core, mark
the version as a concurrency token, or update `WHERE Version = @expected` and check the row count.

Sending the version that was read:

```http
PUT /api/v1/widgets/62bed180-1f78-45d4-8a56-c996936a2947
Content-Type: application/json
```

```json
{
    "data": {
        "id": "62bed180-1f78-45d4-8a56-c996936a2947",
        "type": "widget",
        "attributes": { "name": "Sprocket", "version": "3" }
    }
}
```

```json
{
    "data": {
        "id": "62bed180-1f78-45d4-8a56-c996936a2947",
        "type": "widget",
        "attributes": { "name": "Sprocket", "version": "4" }
    }
}
```

Sending `"3"` again, now that the widget is at `"4"`:

```http
HTTP/1.1 409 Conflict
Content-Type: application/json; charset=utf-8
```

```json
{
    "errors": [
        {
            "status": "409",
            "code": "conflict",
            "title": "The request conflicts with the current state.",
            "detail": "The resource has changed since it was last read. Read it again and retry."
        }
    ]
}
```

### Requiring the version

To make the version required, as the standard does, refuse an update without one before the check:

```csharp
// Concurrency.Require passes a caller who sends no version. Refuse one here to make the
// version required, as the standard does.
if (request.Data.Version is null)
{
    throw new InvalidRequestException(
        ErrorDetails.FieldRequired("version"), "/data/attributes/version", ErrorCodes.FieldRequired);
}
```

`ErrorDetails` and `ErrorCodes` are in `Bitwarden.Server.Sdk.Common.Errors`. The caller gets:

```json
{
    "errors": [
        {
            "status": "422",
            "code": "field-required",
            "title": "A field has an invalid value.",
            "detail": "The 'version' field is required.",
            "source": { "pointer": "/data/attributes/version" }
        }
    ]
}
```

The OpenAPI document still shows `version` as optional.

## What you decide

- **Whether to check at all.** The standard says `SHOULD`. A resource that is only ever replaced
  whole by its owner may not need it.
- **What the version is.** An integer incremented on each write is simplest. Store it as the
  service's own column and return it as a string in every representation, including creates and
  reads, so callers have one to send back.
- **Whether it is required.** As above.
- **`ETag` for caching.** The standard allows one for HTTP caching. The package doesn't set it.

## See also

- [Updating resources](updating-resources.md)
- [Standard fields](../payloads-and-responses/standard-fields.md)
- [Errors](../payloads-and-responses/errors.md)
- [Bulk replaces](../bulk-and-asynchronous-work/bulk-operations/bulk-replaces.md)
- The standard:
  [Optimistic concurrency](https://contributing.bitwarden.com/architecture/server/apis/optimistic-concurrency)
