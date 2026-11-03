# Updating resources

The standard replaces a resource with a `PUT` to `/api/v1/{plural}/{id}`, refuses a document whose
`type` or `id` isn't the resource's with `422`, and answers `200` with the resource as it now
stands. The package checks the verb and the `type`; checking the `id` is left to your handler. The
standard:
[Updating resources](https://contributing.bitwarden.com/architecture/server/apis/updating-resources).

## What the package does

| The standard                                            | The package                                                                                                  |
| ------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------ |
| The verb `MUST` be `PUT`                                | `[StandardUpdate]` is a `PUT`; the service won't start if its `Map` line uses another verb.                  |
| The path `SHOULD` be `/{plural}/{id}`                   | `[StandardUpdate]` defaults to `{id}`; the `Map` line must use it.                                           |
| A `type` that isn't the resource's: `422`               | Refused `422` with `invalid-resource-type` at `/data/type`, before the handler runs.                         |
| An `id` that isn't the one in the path: `422`           | Not checked. The body's `id` reaches the handler as `request.Data.Id`; refuse it yourself.                   |
| Success `SHOULD` be `200`, or `201` if it created one   | `200` when the handler returns a document. A handler that sets `201` itself keeps it.                        |
| `MAY` be `204`                                          | `204` when the handler returns nothing.                                                                      |
| Resources `SHOULD` carry `updatedBy` and `revisionDate` | Every `Resource` has read-only `UpdatedBy` and `UpdatedAt` (`updatedAt` on the wire). The service sets them. |

- **Completely replace.** The document is validated like a create's: every required attribute must
  be present, and read-only members and undeclared attributes are dropped. An attribute the caller
  leaves out is absent from `request.Data`, so replacing with it clears the stored value.
- **`UpdateResponse`** builds the response document.

## Example

The handler refuses a mismatched `id`, then replaces:

```csharp
using Bitwarden.Server.Sdk.Common;
using Bitwarden.Server.Sdk.Common.Errors;
using Bitwarden.Server.Sdk.Service;
using Bitwarden.Server.Sdk.Service.Resources;
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
        // The platform checks the type; the id in the body is the service's to check.
        if (request.Data.Id is { } sent && sent != id)
        {
            throw new InvalidRequestException(
                $"The resource's id '{sent}' is not the one in the path.", "/data/id", ErrorCodes.InvalidValue);
        }

        var replaced = await store.ReplaceAsync(id, request.Data, cancellationToken)
            ?? throw new NotFoundException("widget", id);

        return UpdateResponse(replaced);
    }
}
```

```http
PUT /api/v1/widgets/62bed180-1f78-45d4-8a56-c996936a2947
Accept: application/json
Content-Type: application/json
```

```json
{
    "data": {
        "id": "62bed180-1f78-45d4-8a56-c996936a2947",
        "type": "widget",
        "attributes": {
            "name": "Sprocket",
            "notes": "2.AAAA|BBBB|CCCC"
        }
    }
}
```

```http
HTTP/1.1 200 OK
Content-Type: application/json; charset=utf-8
```

```json
{
    "data": {
        "id": "62bed180-1f78-45d4-8a56-c996936a2947",
        "type": "widget",
        "attributes": {
            "name": "Sprocket",
            "notes": "2.AAAA|BBBB|CCCC",
            "updatedAt": "2026-11-03T14:05:00Z"
        }
    }
}
```

The same request with another widget's `id` in the body:

```json
{
    "errors": [
        {
            "status": "422",
            "code": "invalid-value",
            "title": "A field has an invalid value.",
            "detail": "The resource's id 'cacba8c1-29fa-4018-8950-acd400ec76b7' is not the one in the path.",
            "source": { "pointer": "/data/id" }
        }
    ]
}
```

### Create-or-update

A service that lets a caller create a resource at an identifier it chooses does so with `PUT`. The
handler sets `201` itself when it created the resource, and passes the `Location`:

```csharp
/// <summary>
/// Create or replace a widget
/// </summary>
[StandardUpdate]
public static async Task<ResourceDocument<Widget>> PutWidget(
    string id, ResourceDocument<Widget> request, IWidgetStore store, HttpContext http, CancellationToken cancellationToken)
{
    var (widget, created) = await store.UpsertAsync(id, request.Data, cancellationToken);

    if (!created)
    {
        return UpdateResponse(widget);
    }

    // A status the handler sets is kept. Pass the location: the one the platform derives for a
    // 201 is the request path followed by the id, which suits a POST to the collection only.
    http.Response.StatusCode = StatusCodes.Status201Created;
    return CreateResponse(widget, location: http.Request.PathBase.Add(http.Request.Path).Value);
}
```

A `PUT` to `/api/v1/widgets/new-one` that creates the widget answers `201` with
`Location: /api/v1/widgets/new-one`. The OpenAPI document shows only the `200` response.

## What you decide

- **Check the `id`.** As above, until the package does.
- **Fill `UpdatedAt` and `UpdatedBy`.** The package drops what a caller sends for them and doesn't
  set them. `IContext.UserId` names the acting user. The standard's names are `revisionDate` and
  `updatedBy`; the package's member is `UpdatedAt`.
- **Detect conflicting updates.** See [Optimistic concurrency](optimistic-concurrency.md).
- **`202` for an update that is scheduled.** Return an `IResult` of your own. See
  [Jobs](../bulk-and-asynchronous-work/jobs.md).
- **Don't decrypt.** Replace an `[Encrypted]` member with the ciphertext the caller sent.

## See also

- [Partial updates](partial-updates.md) and [Acting upon resources](acting-upon-resources.md)
- [Standard fields](../payloads-and-responses/standard-fields.md)
- [Bulk updates](../bulk-and-asynchronous-work/bulk-operations/bulk-updates.md) and
  [Bulk replaces](../bulk-and-asynchronous-work/bulk-operations/bulk-replaces.md)
- The standard:
  [Updating resources](https://contributing.bitwarden.com/architecture/server/apis/updating-resources)
