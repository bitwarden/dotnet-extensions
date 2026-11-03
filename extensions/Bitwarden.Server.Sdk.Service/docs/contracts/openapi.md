# OpenAPI

The standard asks every API to be documented in OpenAPI, written for the caller, with required,
nullable and read-only fields marked and an explicit, stable operation identifier on every
operation. The package generates the document from your handlers and types, serves it with a
documentation UI, names standard operations for you, and refuses to start a service whose operation
identifiers collide. The standard:
[OpenAPI](https://contributing.bitwarden.com/architecture/server/apis/openapi).

## What the package does

| The standard asks                                     | The package                                                                                                                                                                       |
| ----------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| APIs `MUST` be documented in OpenAPI                  | Serves an OpenAPI 3.0 document at `/api/openapi.json` and a documentation UI at `/docs`, both anonymous. `GET /` and `HEAD /` redirect (`302`) to `/docs/index.html`.             |
| A description written for the caller                  | Takes each operation's summary and description from the handler's XML `<summary>` and `<remarks>`, and each section's description from `[ApiTag(Description = ...)]`.             |
| Filterable and sortable attributes `SHOULD` be listed | Publishes each `Filter<T>` parameter as `filter[name]`, lists the sortable fields and the default order in `sort`'s description, and lists the attributes `fields[type]` accepts. |
| Always-present fields `MUST` be `required`            | A non-nullable member is `required`; a nullable one is optional.                                                                                                                  |
| Nullable fields `MUST` be `nullable`                  | Writes OpenAPI 3.0, where a nullable member carries `nullable: true`.                                                                                                             |
| Server-set fields `MUST` be `readOnly`, but `version` | Marks `[ReadOnly]` members and the inherited `createdAt`, `createdBy`, `updatedAt`, `updatedBy`, `deletedAt` and `deletedBy` `readOnly`. `version` is not.                        |
| A read-only field in a request `MUST` be ignored      | Drops read-only members, and members the resource doesn't have, from a single resource's request document before the handler sees it.                                             |
| Every operation `MUST` carry an `operationId`         | Derives one for every standard operation from the handler's name and the route's version (below). An explicit `.WithName(...)` or `[EndpointName]` wins.                          |
| Example JSON `SHOULD` be provided                     | Not generated. Add examples with a document transformer (below).                                                                                                                  |

The document also describes the wire rather than your records: a resource's members appear under
`attributes`, every operation has one `default` response with the standard error document, and
paths and sections are sorted alphabetically so the committed file diffs cleanly.

### Operation identifiers

A standard operation's identifier is the handler's name in camel case, plus the route's version
when it is not `v1`:

| Handler                          | Route            | `operationId`            |
| -------------------------------- | ---------------- | ------------------------ |
| `GetWidget`                      | `api/v1/widgets` | `getWidget`              |
| `GetWidget`                      | `api/v2/widgets` | `getWidgetV2`            |
| `CreateWidget`                   | `api/v1/widgets` | `createWidget`           |
| `GetWidgetSummary`, not standard | `api/v1/widgets` | none, unless you name it |

Because the identifier comes from the method name, renaming a handler renames the operation. Treat a
handler's name as part of the contract.

### Startup checks

`Build()` checks every endpoint once they are all mapped, and refuses to start with every problem
listed at once:

- Two endpoints would publish the same operation identifier.
- A standard endpoint is excluded from the document (`ExcludeFromDescription()`): every API a
  service exposes is published.
- A standard endpoint is mapped with a verb or route other than its attribute's.

Two handlers called `GetWidget` on `v1` routes, in two endpoints classes, would collide:

```text
System.InvalidOperationException: The service's endpoints do not meet the standards:
  - 'WidgetsEndpoints.GetWidget' and 'SharedWidgetsEndpoints.GetWidget' would both be published as 'getWidget', and an operation identifier must be unique across the service.
```

## Example

The XML documentation reaches the document only when the service turns on the documentation file,
lets the OpenAPI source generator intercept, and calls `AddOpenApi()` itself. Without the call the
document has no summaries or descriptions. In the project file:

```xml
<PropertyGroup>
  <GenerateDocumentationFile>true</GenerateDocumentationFile>
  <EnableRequestDelegateGenerator>true</EnableRequestDelegateGenerator>
  <InterceptorsNamespaces>$(InterceptorsNamespaces);Microsoft.AspNetCore.Http.Generated;Microsoft.AspNetCore.OpenApi.Generated</InterceptorsNamespaces>
</PropertyGroup>
```

And in the service, where the same call adds any transformers of your own:

```csharp
using Bitwarden.Server.Sdk.Service;

internal sealed class WidgetService(string[] args) : Service("widget-service", "Widget Service", args)
{
    protected override void ConfigureServices(IServiceCollection services)
    {
        // Reads the handlers' XML documentation into the document, and adds the service's own
        // document transformers after the package's.
        services.AddOpenApi(options => options.AddDocumentTransformer<WidgetExamples>());
        services.AddSingleton<IWidgetStore, WidgetStore>();
    }

    protected override void MapEndpoints(IEndpointRouteBuilder endpoints) =>
        WidgetsEndpoints.Map(endpoints);
}
```

The resource documents its members. `PolishCount` is set by the service, so it is `[ReadOnly]`:

```csharp
using System.ComponentModel.DataAnnotations;
using Bitwarden.Server.Sdk.Common;
using Bitwarden.Server.Sdk.Service.Resources;
using Bitwarden.Server.Sdk.Service.Querying;

[Sortable(nameof(CreatedAt), Default = true, Descending = true)]
public sealed record Widget : Resource
{
    /// <summary>
    /// What the widget is called.
    /// </summary>
    [Sortable]
    [StringLength(100)]
    public required string Name { get; init; }

    /// <summary>
    /// The person's notes, encrypted on their device.
    /// </summary>
    [Encrypted(MaxLength = 10_000)]
    public string? Notes { get; init; }

    /// <summary>
    /// How many times the widget has been polished. Set by the service.
    /// </summary>
    [ReadOnly]
    public int PolishCount { get; init; }
}
```

In the document, `name` and `polishCount` are `required`, `notes` is `nullable`, and `polishCount`
and the inherited audit fields are `readOnly`.

The endpoints class names its section with `[ApiTag]`. Standard operations are named for you; the
one handler without a standard attribute is named with `WithName`:

```csharp
using Bitwarden.Server.Sdk.Common;
using Bitwarden.Server.Sdk.Service;
using Bitwarden.Server.Sdk.Service.Resources;
using Bitwarden.Server.Sdk.Service.Querying;
using static Bitwarden.Server.Sdk.Service.StandardResponses;

[ApiTag(Description = "Widgets, which a person keeps. Each has a name and encrypted notes.")]
public static class WidgetsEndpoints
{
    public const string Collection = "api/v1/widgets";

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var widgets = endpoints.MapGroup(Collection).WithStandards();
        widgets.MapPost("", CreateWidget);
        widgets.MapGet("", ListWidgets);
        widgets.MapGet("{id}", GetWidget);

        // Not a standard operation, so it needs an operation identifier of its own.
        widgets.MapGet("{id}/summary", GetWidgetSummary).WithName("getWidgetSummary");
    }

    /// <summary>
    /// Create a widget
    /// </summary>
    /// <remarks>
    /// The widget's notes are encrypted on the person's device before they are sent.
    /// </remarks>
    [StandardCreate]
    public static async Task<ResourceDocument<Widget>> CreateWidget(
        ResourceDocument<Widget> request, IWidgetStore store, CancellationToken cancellationToken) =>
        CreateResponse(await store.AddAsync(request.Data, cancellationToken));

    /// <summary>
    /// List widgets
    /// </summary>
    [StandardReadMany]
    public static async Task<ResourceListDocument<Widget>> ListWidgets(
        Sort? sort, IWidgetStore store, CancellationToken cancellationToken) =>
        ReadManyResponse(await store.ListAsync(sort ?? Sort.Default<Widget>(), cancellationToken));

    /// <summary>
    /// Get a widget
    /// </summary>
    [StandardReadOne]
    public static async Task<ResourceDocument<Widget>> GetWidget(
        string id, IWidgetStore store, CancellationToken cancellationToken) =>
        ReadOneResponse(await store.FindAsync(id, cancellationToken)
            ?? throw new NotFoundException("widget", id));

    /// <summary>
    /// Summarize a widget
    /// </summary>
    public static async Task<WidgetSummary> GetWidgetSummary(
        string id, IWidgetStore store, CancellationToken cancellationToken) =>
        new((await store.FindAsync(id, cancellationToken) ?? throw new NotFoundException("widget", id)).Name);
}
```

The operations are published as `createWidget`, `listWidgets`, `getWidget` and `getWidgetSummary`,
under a "Widgets" section. `listWidgets` documents `sort` as accepting `createdAt` and `name`, with
`-createdAt` by default.

### Examples

The package writes no example JSON. A document transformer added in your `AddOpenApi` call runs
after the package's, so it sees each schema in its published, enveloped shape:

```csharp
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

public sealed class WidgetExamples : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        if (document.Components?.Schemas is { } schemas
            && schemas.TryGetValue("WidgetDocument", out var found)
            && found is OpenApiSchema schema)
        {
            // Notes are encrypted on the person's device, so the example is ciphertext too.
            schema.Examples = [JsonNode.Parse("""
                {
                  "data": {
                    "id": "2f0c4a8e-5b1d-4c6e-9a7f-3d2b1e0f9c8a",
                    "type": "widget",
                    "attributes": {
                      "name": "Desk lamp",
                      "notes": "2.c2FtcGxlIGl2|c2FtcGxlIGNpcGhlcnRleHQ=|c2FtcGxlIG1hYw==",
                      "polishCount": 3,
                      "createdAt": "2026-10-01T09:30:00Z"
                    }
                  }
                }
                """)!];
        }

        return Task.CompletedTask;
    }
}
```

### Committing the contract

Run the service with `--emit-openapi <path>` to write the document and exit without serving. The
service's own hosted services don't run and options validated on start aren't checked, so no
database, queue or Identity needs to be reachable. `dotnet run --project` runs the service from the
project's directory, so pass an absolute path. The log names the full path written:

```bash
dotnet run --project src/WidgetService -- --emit-openapi "$PWD/openapi/widget-service.json"
```

Writing fails when a section has no description: give every endpoints class an
`[ApiTag(Description = ...)]`. The served document doesn't need one; the committed one does.

## What you decide

- **Words.** Summaries, remarks, `<param>` text and section descriptions are yours. Write them for
  a caller, free of implementation detail. A service publishes one document, holding every version
  of every API it serves.
- **Operations outside the standard attributes.** Only standard operations are named for you. Give
  any other endpoint you publish an identifier with `.WithName(...)` or `[EndpointName]`. The
  service starts without one, so check the committed document.
- **Handler names.** Renaming a handler, or moving it to a `v2` route, changes its `operationId`
  and every generated client's method. Rename only when you mean to change the contract.
- **Examples.** Add them with a transformer, as above. Never put real Vault Data in one: an
  encrypted member's example is ciphertext.
- **Where the committed document lives,** and whether CI regenerates it and fails on a difference.

## See also

- [API paths](api-paths.md)
- [Breaking changes and versioning](breaking-changes-and-versioning.md)
- [Deprecation](deprecation.md): marking an operation `deprecated` in the document.
- [Verbs](../operations/verbs.md): the standard attributes and how routes are checked.
- [Standard fields](../payloads-and-responses/standard-fields.md)
- The standard: [OpenAPI](https://contributing.bitwarden.com/architecture/server/apis/openapi)
