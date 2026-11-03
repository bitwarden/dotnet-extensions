# Breaking changes and versioning

The standard asks APIs not to make breaking changes, to fix bugs in place, and otherwise to publish
a breaking change as a new version of the API beside the old one, which is then deprecated. Whether
a change breaks a caller is your judgement: the package doesn't detect breaking changes. It helps
you see them, by writing the contract to a file you commit, and it lets a `v2` live beside a `v1`
with its own operation identifiers. The standard:
[Breaking changes and versioning](https://contributing.bitwarden.com/architecture/server/apis/breaking-changes-and-versioning).

## What the package does

| The standard asks                         | The package                                                                                                                                                                                       |
| ----------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| APIs `SHOULD NOT` make breaking changes   | Nothing detects one. `--emit-openapi <path>` writes the document so a change to the contract is a diff in review. See [OpenAPI](openapi.md#committing-the-contract).                              |
| Bugs `MUST` be fixed in place             | Nothing to do: the fix ships in the same version.                                                                                                                                                 |
| A breaking change `MUST` be a new version | A `v2` route group maps beside the `v1` one. Its standard operations are published as `...V2` (`getWidgetV2`), in the same document, and the service refuses to start if two identifiers collide. |
| The old version `MUST` be deprecated      | Not provided. See [Deprecation](deprecation.md) for what a service adds.                                                                                                                          |

The package's own tests pin the answers and the OpenAPI document its handlers produce
(`MinimalApiParity.golden.txt` and `MinimalApiParity.openapi.golden.json`), so an upgrade of the
package that changes what your service publishes is a deliberate change in the package, not an
accident.

### What counts as breaking here

Several of the standard's breaking changes come from ordinary-looking edits to a resource, because
the package publishes what the types say:

| Edit                                                                         | Why it breaks                                                                              |
| ---------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------ |
| Adding a non-nullable member to a resource                                   | A non-nullable member is `required`, in the document and when a request is validated.      |
| Making a nullable member non-nullable                                        | It becomes required.                                                                       |
| Changing a member's type                                                     | The schema's type changes, and a request with the old type is refused `422`.               |
| Adding `[StringLength]`, `[Range]` or a lower `[Encrypted(MaxLength = ...)]` | A constraint is added. A string member with no bound of its own is already limited to 255. |
| Removing a member, or marking it `[ReadOnly]`                                | A response loses a field, or a request's value is dropped.                                 |
| Adding paging to a list, or lowering `[DefaultValue]` on its `PageSize`      | The same request returns fewer results.                                                    |

## Example

A `v2` makes `Size` required on every widget. It is a breaking change for `v1` callers, so it gets
its own resource type and its own routes, and `v1` keeps working from the same store:

```csharp
using Bitwarden.Server.Sdk.Common;
using Bitwarden.Server.Sdk.Service;
using Bitwarden.Server.Sdk.Service.Resources;
using static Bitwarden.Server.Sdk.Service.StandardResponses;

/// <summary>
/// A widget, as version 2 describes it: every widget now has a size.
/// </summary>
[ResourceType("widget")]
public sealed record WidgetV2 : Resource
{
    public required string Name { get; init; }

    [Encrypted(MaxLength = 10_000)]
    public string? Notes { get; init; }

    /// <summary>
    /// The widget's size. Required from version 2, which is why version 2 exists.
    /// </summary>
    public required WidgetSize Size { get; init; }
}

public enum WidgetSize
{
    Small,
    Large,
}

[ApiTag("Widgets")]
public static class WidgetsV2Endpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var widgets = endpoints.MapGroup("api/v2/widgets").WithStandards();
        widgets.MapPost("", CreateWidget);
        widgets.MapGet("{id}", GetWidget);
    }

    /// <summary>
    /// Create a widget
    /// </summary>
    [StandardCreate]
    public static async Task<ResourceDocument<WidgetV2>> CreateWidget(
        ResourceDocument<WidgetV2> request, IWidgetStore store, CancellationToken cancellationToken) =>
        CreateResponse(ToV2(await store.AddAsync(
            new WidgetRecord(request.Data.Name, request.Data.Notes, request.Data.Size), cancellationToken)));

    /// <summary>
    /// Get a widget
    /// </summary>
    [StandardReadOne]
    public static async Task<ResourceDocument<WidgetV2>> GetWidget(
        string id, IWidgetStore store, CancellationToken cancellationToken) =>
        ReadOneResponse(ToV2(await store.FindAsync(id, cancellationToken)
            ?? throw new NotFoundException("widget", id)));

    private static WidgetV2 ToV2(WidgetRecord record) =>
        new() { Id = record.Id, Name = record.Name, Notes = record.Notes, Size = record.Size };
}
```

- **`[ResourceType("widget")]`** keeps the wire type `widget`. Without it the type would be
  `widget-v2`, from the record's name. The OpenAPI schemas are still named after the record
  (`WidgetV2`, `WidgetV2Document`), so they don't collide with `v1`'s.
- **`[ApiTag("Widgets")]`** puts both versions in one section. The `v1` endpoints class carries the
  description.
- **Both are mapped** in `MapEndpoints`: `WidgetsEndpoints.Map(endpoints)` for `v1` and
  `WidgetsV2Endpoints.Map(endpoints)` for `v2`. The document then publishes `createWidget`,
  `getWidget`, `createWidgetV2` and `getWidgetV2`.
- **`v1` writes a default** for the field it can't send. Here its create stores `WidgetSize.Small`.

`v2` exists only for the operations that changed. `GET /api/v2/widgets/{id}` doesn't imply that any
other `v2` path exists.

### Adding values to constrained fields

An enumeration is published as a string with its permitted values, in upper snake case:

```json
"size": {
  "enum": ["SMALL", "LARGE"],
  "type": "string",
  "description": "The widget's size. Required from version 2, which is why version 2 exists."
}
```

Adding `Medium` is additive, and the service accepts it at once. A client generated from the
previous document, with a closed enumeration, fails when it reads `MEDIUM` in a response. Check
whether the field's callers tolerate unknown values before adding one.

## What you decide

- **Whether a change breaks a caller,** and, if so, whether you can account for every caller and
  change it in place, or must publish a new version. The standard allows the first for
  service-to-service APIs.
- **How you catch a breaking change.** Regenerate the committed document in CI and fail on a
  difference, so every contract change is reviewed:

    ```bash
    dotnet run --project src/WidgetService -- --emit-openapi "$PWD/openapi/widget-service.json"
    git diff --exit-code openapi/widget-service.json
    ```

    A tool that compares two OpenAPI documents for breaking changes can run on the same file. The
    package doesn't include one.

- **How versions share code.** The package puts no rule on it. Keeping one stored shape and mapping
  each version's resource to it, as above, keeps a fix in one place.
- **When the old version goes.** Deprecate it with a sunset date, as
  [Deprecation](deprecation.md) shows, and remove its endpoints class when the date passes.

## See also

- [OpenAPI](openapi.md): operation identifiers and committing the contract.
- [API paths](api-paths.md): the version in the path.
- [Deprecation](deprecation.md)
- [JSON](../payloads-and-responses/json.md): how enumerations travel.
- [Paging](../querying/paging.md): default page sizes.
- The standard:
  [Breaking changes and versioning](https://contributing.bitwarden.com/architecture/server/apis/breaking-changes-and-versioning)
