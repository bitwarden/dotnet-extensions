# Payload shape

Every body that carries resources holds them in a top-level `data`, with response metadata in
`meta` and failures in `errors`; each resource has an `id`, a `type` and its `attributes`. You write
a resource as a flat record and the package applies that envelope on the wire, in both directions.
The standard:
[Payload shape](https://contributing.bitwarden.com/architecture/server/apis/payload-shape).

## What the package does

| The standard says                                                     | The package                                                                                                                                                                                                              |
| --------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| Resources `MUST` be held in `data`: one, or an array                  | `ResourceDocument<T>` carries one resource; `ResourceListDocument<T>`, `PagedResourceListDocument<T>` and `CursorPagedResourceListDocument<T>` carry an array. Handlers take and return these, never raw `data` objects. |
| Response metadata `MUST` go in a top-level `meta`                     | Each document has a `Meta` dictionary. Paged lists put their paging fields there. `meta` is left out when empty.                                                                                                         |
| Errors are a top-level `errors` array                                 | Every refusal is the standard error document. See [Errors](errors.md).                                                                                                                                                   |
| Every resource `MUST` have `id`, `type` and `attributes`              | A resource derives from `Resource`, which declares `Id` and `Type`. Every other member is written under `attributes`, which is always present, as `{}` when no member has a value.                                       |
| `id` `MUST` be a string                                               | `Resource.Id` is a `string?`. Format a `Guid` or a number yourself.                                                                                                                                                      |
| `id` `MUST` be omitted on a create where the server assigns it        | An `Id` that is null is left out. The package does not refuse a create that sends one: the handler decides (see the example).                                                                                            |
| `type` is the singular name of the resource                           | `Type` is the record's name in kebab case, or what `[ResourceType]` says. See [Naming conventions](naming-conventions.md).                                                                                               |
| Each kind of resource `MUST` have its own `type`                      | Not checked. Keep the names, and any `[ResourceType]` values, unique within the service.                                                                                                                                 |
| In a request, `type` and `id` are checked against the path            | `type` is checked: absent is `422` `field-required`, another type is `422` `invalid-resource-type`, both at `/data/type`. `id` is not compared with the path: the handler does it.                                       |
| Other bodies (actions, bulk targeting, searches) have their own shape | A handler parameter that is not a resource document binds as plain JSON, with no envelope.                                                                                                                               |

On the way in, the package also:

- drops attributes the resource doesn't declare, and read-only ones (see
  [Standard fields](standard-fields.md)), rather than refusing the request;
- prefers `id` and `type` outside `attributes` to the same names inside it;
- answers a body that isn't JSON at all `400` `malformed-json`, and a JSON value that isn't an
  object, or has no `data` object, `422`.

## Example

The resource is a flat record. Nothing in it mentions `data` or `attributes`:

```csharp
using System.ComponentModel.DataAnnotations;
using Bitwarden.Server.Sdk.Common;
using Bitwarden.Server.Sdk.Service.Resources;

public sealed record Widget : Resource
{
    /// <summary>
    /// What the widget is called.
    /// </summary>
    [StringLength(100)]
    public required string Name { get; init; }

    /// <summary>
    /// The person's notes, encrypted on their device.
    /// </summary>
    [Encrypted(MaxLength = 10_000)]
    public string? Notes { get; init; }
}
```

The handlers take and return documents. The two checks the package leaves to you are here: whether a
create may name its own `id`, and whether a replace's `id` is the one in the path.

```csharp
using Bitwarden.Server.Sdk.Common;
using Bitwarden.Server.Sdk.Service;
using Bitwarden.Server.Sdk.Service.Resources;
using static Bitwarden.Server.Sdk.Service.StandardResponses;

[ApiTag(Description = "Widgets, which a person keeps.")]
public static class WidgetsEndpoints
{
    public const string Collection = "api/v1/widgets";

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var widgets = endpoints.MapGroup(Collection).WithStandards();
        widgets.MapPost("", CreateWidget);
        widgets.MapPut("{id}", UpdateWidget);
        widgets.MapGet("", ListWidgets);
    }

    /// <summary>
    /// Create a widget
    /// </summary>
    [StandardCreate]
    public static async Task<ResourceDocument<Widget>> CreateWidget(
        ResourceDocument<Widget> request, IWidgetStore store, CancellationToken cancellationToken)
    {
        // The service assigns the identifier, so one the caller sent is refused.
        if (request.Data.Id is not null)
        {
            throw new InvalidRequestException("The service assigns a widget's id.", "/data/id");
        }

        return CreateResponse(await store.AddAsync(request.Data, cancellationToken));
    }

    /// <summary>
    /// Replace a widget
    /// </summary>
    [StandardUpdate]
    public static async Task<ResourceDocument<Widget>> UpdateWidget(
        string id, ResourceDocument<Widget> request, IWidgetStore store, CancellationToken cancellationToken)
    {
        // The platform checks the type; the id in the body must name the widget in the path.
        if (request.Data.Id is { } sent && sent != id)
        {
            throw new InvalidRequestException(
                $"The id '{sent}' is not the widget in the path, '{id}'.", "/data/id");
        }

        return UpdateResponse(await store.ReplaceAsync(id, request.Data, cancellationToken));
    }

    /// <summary>
    /// List a person's widgets
    /// </summary>
    [StandardReadMany]
    public static async Task<ResourceListDocument<Widget>> ListWidgets(
        IWidgetStore store, CancellationToken cancellationToken)
    {
        var widgets = await store.ListAsync(cancellationToken);

        // Anything about the response rather than a resource in it goes in meta.
        return ReadManyResponse(widgets, new Dictionary<string, object?> { ["totalCount"] = widgets.Count });
    }
}
```

A create, and what the caller gets back. The `attributes` envelope appears on the wire only:

```http
POST /api/v1/widgets
Content-Type: application/json

{"data":{"type":"widget","attributes":{"name":"Sprocket","notes":"2.AAAAAAAAAAAAAAAAAAAAAA==|AAAAAAAAAAAAAAAAAAAAAA==|AAAAAAAAAAAAAAAAAAAAAA=="}}}
```

```http
HTTP/1.1 201 Created
Content-Type: application/json; charset=utf-8
Location: /api/v1/widgets/62bed180-1f78-45d4-8a56-c996936a2947

{"data":{"id":"62bed180-1f78-45d4-8a56-c996936a2947","type":"widget","attributes":{"name":"Sprocket","notes":"2.AAAAAAAAAAAAAAAAAAAAAA==|AAAAAAAAAAAAAAAAAAAAAA==|AAAAAAAAAAAAAAAAAAAAAA=="}}}
```

A body that declares another type is refused before the handler runs:

```http
HTTP/1.1 422 Unprocessable Entity
Content-Type: application/json; charset=utf-8

{"errors":[{"status":"422","code":"invalid-resource-type","title":"The resource type is not the one this endpoint serves.","detail":"A resource of type 'widget' was expected, but 'gadget' was declared.","source":{"pointer":"/data/type"}}]}
```

## What you decide

- **An `id` in a request.** Compare it with the path on every operation that names a resource, as
  `UpdateWidget` does. On a create, refuse it or ignore it; the package passes it through either
  way.
- **Unique types.** Two records with the same name in different namespaces, or the same
  `[ResourceType]`, share a `type`. Nothing stops the service starting.
- **What goes in `meta`.** Paging fills it for you. Anything else about the response, such as a
  count of an unpaged list, is yours to add through the response helper's `meta` argument.
- **Shapes of other bodies.** An action's arguments, a bulk operation's targeting and a search are
  plain JSON objects, set by their own pages:
  [Acting upon resources](../operations/acting-upon-resources.md),
  [Bulk operations](../bulk-and-asynchronous-work/bulk-operations/index.md) and
  [Advanced queries](../querying/advanced-queries.md).

## See also

- [Standard fields](standard-fields.md): the members every resource inherits.
- [JSON](json.md): how values inside `attributes` are written and read.
- [Request validation](request-validation.md): everything checked before a handler runs.
- [Standard responses](standard-responses.md): the helpers that build each document.
- The standard:
  [Payload shape](https://contributing.bitwarden.com/architecture/server/apis/payload-shape).
