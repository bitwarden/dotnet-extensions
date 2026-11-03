# Verbs

The standard gives each HTTP verb one meaning: `POST` creates, `GET` reads, `PUT` replaces, `PATCH`
partially updates and `DELETE` deletes, with actions, advanced queries and bulk operations using
`POST`. The package gives each standard operation an attribute that fixes its verb, and refuses to
start a service whose `Map` line uses another. The standard:
[Verbs](https://contributing.bitwarden.com/architecture/server/apis/verbs).

## What the package does

Every handler in a group mapped with `WithStandards()` carries one standard attribute. The attribute
names the operation, and with it the verb, the route below the group and the success status:

| Attribute            | Verb     | Route below the group   | Succeeds with                           |
| -------------------- | -------- | ----------------------- | --------------------------------------- |
| `[StandardCreate]`   | `POST`   | the collection (`""`)   | `201`, with `Location`                  |
| `[StandardReadMany]` | `GET`    | the collection (`""`)   | `200`                                   |
| `[StandardReadOne]`  | `GET`    | `{id}`                  | `200`                                   |
| `[StandardUpdate]`   | `PUT`    | `{id}`                  | `200`                                   |
| `[StandardDelete]`   | `DELETE` | `{id}`                  | `204`                                   |
| `[StandardAction]`   | `POST`   | `{id}/actions/{action}` | `200`, or `204` when it returns nothing |

- **One verb per meaning.** Each attribute derives from ASP.NET Core's `HttpPost`, `HttpGet`,
  `HttpPut` or `HttpDelete` attribute, so the verb is part of the declaration. There is no attribute
  for `PATCH`: see [Partial updates](partial-updates.md).
- **`POST` beyond creates.** Actions use `[StandardAction]`, and searches and bulk operations use
  `[StandardSearch]` and the `[StandardBulk...]` attributes, all of them `POST`.
- **The `Map` line must agree.** The service refuses to start when a standard endpoint is mapped
  with a verb other than its attribute's, or at a route other than its attribute's. It lists every
  problem at once, naming the class and method.
- **A route can be given.** Each attribute takes a `template` for a route other than its default,
  such as `[StandardReadOne("by-email/{email}")]`. The `Map` line must then use that route.
- **Reads are reads.** `[StandardReadOne]`, `[StandardReadMany]` and `[StandardSearch]` are reads,
  so an operation that declares no scope of its own demands the service's read scope. Every other
  operation demands the write scope. See
  [Authentication and authorization](../beyond-the-standards/authentication-and-authorization.md).
- **Writes finish.** A request with any verb but `GET`, `HEAD`, `OPTIONS` and `TRACE` runs to the
  end even when its client disconnects.
- **Other verbs are refused.** A verb nothing is mapped for answers `405` in the standard error
  document.

## Example

A resource's endpoints, one `Map` line per handler and one attribute per handler:

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
        widgets.MapGet("", ListWidgets);
        widgets.MapGet("{id}", GetWidget);
        widgets.MapPut("{id}", UpdateWidget);
        widgets.MapDelete("{id}", DeleteWidget);
        widgets.MapPost("{id}/actions/polish", Polish);
    }

    /// <summary>
    /// Create a widget
    /// </summary>
    [StandardCreate]
    public static async Task<ResourceDocument<Widget>> CreateWidget(
        ResourceDocument<Widget> request, IWidgetStore store, CancellationToken cancellationToken) =>
        CreateResponse(await store.AddAsync(request.Data, cancellationToken));

    /// <summary>
    /// List widgets
    /// </summary>
    [StandardReadMany]
    public static async Task<ResourceListDocument<Widget>> ListWidgets(
        IWidgetStore store, CancellationToken cancellationToken) =>
        ReadManyResponse(await store.ListAsync(cancellationToken));

    /// <summary>
    /// Get a widget
    /// </summary>
    [StandardReadOne]
    public static async Task<ResourceDocument<Widget>> GetWidget(
        string id, IWidgetStore store, CancellationToken cancellationToken) =>
        ReadOneResponse(await store.FindAsync(id, cancellationToken)
            ?? throw new NotFoundException("widget", id));

    /// <summary>
    /// Replace a widget
    /// </summary>
    [StandardUpdate]
    public static async Task<ResourceDocument<Widget>> UpdateWidget(
        string id, ResourceDocument<Widget> request, IWidgetStore store, CancellationToken cancellationToken) =>
        UpdateResponse(await store.ReplaceAsync(id, request.Data, cancellationToken));

    /// <summary>
    /// Delete a widget
    /// </summary>
    [StandardDelete]
    public static Task DeleteWidget(string id, IWidgetStore store, CancellationToken cancellationToken) =>
        store.DeleteAsync(id, cancellationToken);

    /// <summary>
    /// Polish a widget
    /// </summary>
    [StandardAction]
    public static Task Polish(string id, IWidgetStore store, CancellationToken cancellationToken) =>
        store.PolishAsync(id, cancellationToken);
}
```

Map `GetWidget` with `MapPost` instead of `MapGet` and the service stops at startup:

```text
System.InvalidOperationException: The service's endpoints do not meet the standards:
  - 'WidgetsEndpoints.GetWidget' is [StandardReadOne], which is GET, but is mapped with POST.
```

A verb the resource doesn't map is refused:

```http
PATCH /api/v1/widgets/62bed180-1f78-45d4-8a56-c996936a2947
Content-Type: application/json
```

```http
HTTP/1.1 405 Method Not Allowed
Content-Type: application/json; charset=utf-8
```

```json
{
    "errors": [
        {
            "status": "405",
            "code": "method-not-allowed",
            "title": "The method is not allowed on this resource.",
            "detail": "The resource does not support PATCH requests."
        }
    ]
}
```

## What you decide

- **Map every group with `WithStandards()`.** The attributes do nothing on a minimal API by
  themselves: `WithStandards()` is what reads them, checks the `Map` line and applies the status.
  A group mapped without it gets none of this.
- **Keep `GET` safe.** The package can't tell whether a handler changes state. A `GET` handler must
  not, and an operation that changes something without being a create, update or delete is an
  [action](acting-upon-resources.md).
- **Collection routes are not compared.** An attribute whose route is the collection itself
  (`[StandardCreate]` and `[StandardReadMany]` by default) matches any route its `Map` line gives,
  so only the verb is checked for them. Map them at `""`.
- **Create-or-update goes through `PUT`.** See
  [Updating resources](updating-resources.md#create-or-update).

## See also

- [Creating resources](creating-resources.md), [Reading resources](reading-resources.md),
  [Updating resources](updating-resources.md), [Deleting resources](deleting-resources.md) and
  [Acting upon resources](acting-upon-resources.md)
- [Advanced queries](../querying/advanced-queries.md) and
  [Bulk operations](../bulk-and-asynchronous-work/bulk-operations/index.md), the other uses of
  `POST`
- [API paths](../contracts/api-paths.md)
- The standard: [Verbs](https://contributing.bitwarden.com/architecture/server/apis/verbs)
