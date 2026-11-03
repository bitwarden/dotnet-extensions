# Creating resources

The standard creates a resource with a `POST` to its collection, refuses a document of the wrong
`type` with `422`, and answers `201` with the new resource. The package binds and checks the
document before your handler runs, and answers `201` with a `Location` header when the handler
returns. The standard:
[Creating resources](https://contributing.bitwarden.com/architecture/server/apis/creating-resources).

## What the package does

| The standard                                  | The package                                                                                  |
| --------------------------------------------- | -------------------------------------------------------------------------------------------- |
| The verb `MUST` be `POST`                     | `[StandardCreate]` is a `POST`; the service won't start if its `Map` line uses another verb. |
| The path `SHOULD` be `/api/v1/{plural}`       | `[StandardCreate]` defaults to the group's own route, the collection.                        |
| A `type` the collection doesn't accept: `422` | Refused `422` with `invalid-resource-type` at `/data/type`, before the handler runs.         |
| Success `SHOULD` be `201`                     | `201` when the handler returns a document, with `Location` set to the new resource.          |
| `MAY` be `204`                                | `204` when the handler returns nothing (`Task` or `void`).                                   |

- **The document is checked.** A `ResourceDocument<T>` parameter is bound from the body and
  validated: a missing `data`, a missing required attribute or an attribute of the wrong JSON type
  is refused `422`, and malformed JSON `400`, all in the standard error document. See
  [Request validation](../payloads-and-responses/request-validation.md).
- **Read-only members are dropped.** `createdAt`, `updatedBy` and the other read-only members, and
  any member marked `[ReadOnly]`, never reach the handler. Neither do attributes the resource
  doesn't declare.
- **`CreateResponse`** builds the response document. Leave out its `location` and the platform
  derives `Location` from the request path and the resource's `Id`. Pass one when the new resource
  is read somewhere else.
- **The operation is documented.** The OpenAPI document shows the `201` response with the handler's
  own return type, and the operation identifier comes from the method name (`createWidget`).

## Example

The handler hands the bound resource to the store and returns what was stored:

```csharp
using Bitwarden.Server.Sdk.Common;
using Bitwarden.Server.Sdk.Service;
using Bitwarden.Server.Sdk.Service.Resources;
using static Bitwarden.Server.Sdk.Service.StandardResponses;

public static class WidgetsEndpoints
{
    public const string Collection = "api/v1/widgets";

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var widgets = endpoints.MapGroup(Collection).WithStandards();
        widgets.MapPost("", CreateWidget);
    }

    /// <summary>
    /// Create a widget
    /// </summary>
    [StandardCreate]
    public static async Task<ResourceDocument<Widget>> CreateWidget(
        ResourceDocument<Widget> request, IWidgetStore store, CancellationToken cancellationToken)
    {
        // request.Data is bound and validated, and its read-only members (createdAt and the
        // rest) were dropped. The store assigns the identifier, whatever id the caller sent.
        var created = await store.AddAsync(request.Data, cancellationToken);

        // 201, with Location derived from the request path and created.Id.
        return CreateResponse(created);
    }
}
```

The caller sends the attributes. `notes` is ciphertext the person's device made, which the service
stores as it came:

```http
POST /api/v1/widgets
Content-Type: application/json
```

```json
{
    "data": {
        "type": "widget",
        "attributes": {
            "name": "Sprocket",
            "notes": "2.AAAA|BBBB|CCCC"
        }
    }
}
```

```http
HTTP/1.1 201 Created
Content-Type: application/json; charset=utf-8
Location: /api/v1/widgets/62bed180-1f78-45d4-8a56-c996936a2947
```

```json
{
    "data": {
        "id": "62bed180-1f78-45d4-8a56-c996936a2947",
        "type": "widget",
        "attributes": {
            "name": "Sprocket",
            "notes": "2.AAAA|BBBB|CCCC",
            "createdAt": "2026-11-03T14:00:00Z",
            "version": "1"
        }
    }
}
```

A document of another type never reaches the handler:

```json
{
    "errors": [
        {
            "status": "422",
            "code": "invalid-resource-type",
            "title": "The resource type is not the one this endpoint serves.",
            "detail": "A resource of type 'widget' was expected, but 'gadget' was declared.",
            "source": { "pointer": "/data/type" }
        }
    ]
}
```

## What you decide

- **Ignore the caller's `id`.** The package passes an `id` sent in the document through to
  `request.Data.Id`. A create assigns its own identifier, so don't store the one sent.
- **Fill the audit fields.** The package drops the caller's `createdAt` and `createdBy` but doesn't
  set them: the store does. `IContext.UserId` names the acting user.
- **`202` for a create that is scheduled.** The package has no job response for a single create.
  Return an `IResult` of your own, which is written as it is. See
  [Jobs](../bulk-and-asynchronous-work/jobs.md).
- **Don't decrypt.** Store an `[Encrypted]` member as it came. The package checks only its length.

## See also

- [Verbs](verbs.md)
- [Payload shape](../payloads-and-responses/payload-shape.md) and
  [Standard responses](../payloads-and-responses/standard-responses.md)
- [Bulk creates](../bulk-and-asynchronous-work/bulk-operations/bulk-creates.md)
- The standard:
  [Creating resources](https://contributing.bitwarden.com/architecture/server/apis/creating-resources)
