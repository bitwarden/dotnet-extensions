# Partial updates

The standard says services should not support partial updates. One that does must use `PATCH`,
leave out what a caller leaves out, clear a field sent as `null`, and treat an empty string as not
sent. The package has no partial update: use `PUT`, or an action for a change to one field. The
standard:
[Partial updates](https://contributing.bitwarden.com/architecture/server/apis/partial-updates).

## What the package does

| The standard                                               | The package                                                                                        |
| ---------------------------------------------------------- | -------------------------------------------------------------------------------------------------- |
| Services `SHOULD NOT` support partial updates              | There is no standard attribute for `PATCH`. A `PATCH` nothing is mapped for answers `405`.         |
| A service that does `MUST` use `PATCH`                     | `MapPatch` works in a group mapped `WithStandards()`, as an endpoint without a standard attribute. |
| An absent field is unchanged, an explicit `null` clears it | A `ResourceDocument<T>` can't express this: binding treats `null` as absent.                       |
| An empty string counts as absent                           | Binding a `ResourceDocument<T>` already treats `""` as absent.                                     |
| A change to one element `MAY` be an action                 | `[StandardAction]`. See [Acting upon resources](acting-upon-resources.md).                         |

- **Why not a `ResourceDocument<T>`.** Binding a resource document drops a member sent as `null` or
  as a blank string, and refuses a document that leaves out a required attribute. That is right for
  a create or a `PUT`, and makes the document unable to say "clear this" or "leave that".
- **A `PATCH` endpoint is not a standard one.** It gets the standard error document, binding and
  validation of a plain body, and authorization with the write scope. It doesn't get an operation
  identifier, a `type` or `id` check, or a documented resource schema.

## Example

The recommended way: replace with `PUT` (see [Updating resources](updating-resources.md)), or give
a change its own action. Renaming a widget:

```csharp
using System.ComponentModel.DataAnnotations;
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
        widgets.MapPost("{id}/actions/rename", Rename);
    }

    /// <summary>
    /// Rename a widget
    /// </summary>
    [StandardAction]
    public static async Task<ResourceDocument<Widget>> Rename(
        string id, RenameWidget request, IWidgetStore store, CancellationToken cancellationToken) =>
        ActionResponse(await store.RenameAsync(id, request.Name, cancellationToken)
            ?? throw new NotFoundException("widget", id));
}

/// <summary>
/// A widget's new name.
/// </summary>
/// <param name="Name">What the widget is to be called.</param>
public sealed record RenameWidget([property: StringLength(100)] string Name);
```

```http
POST /api/v1/widgets/62bed180-1f78-45d4-8a56-c996936a2947/actions/rename
Content-Type: application/json
```

```json
{ "name": "Bobby" }
```

```json
{
    "data": {
        "id": "62bed180-1f78-45d4-8a56-c996936a2947",
        "type": "widget",
        "attributes": { "name": "Bobby", "notes": "2.AAAA|BBBB|CCCC" }
    }
}
```

### If you must support `PATCH`

Take the body as a `JsonObject`, so an absent member and an explicit `null` look different, and do
the checks a resource document would have done:

```csharp
using System.Text.Json;
using System.Text.Json.Nodes;
using Bitwarden.Server.Sdk.Common;
using Bitwarden.Server.Sdk.Service;
using Bitwarden.Server.Sdk.Service.Resources;
using static Bitwarden.Server.Sdk.Service.StandardResponses;

public static class PatchedWidgetsEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var widgets = endpoints.MapGroup(WidgetsEndpoints.Collection).WithStandards();

        // No standard attribute describes a PATCH, so name the operation here.
        widgets.MapPatch("{id}", PatchWidget).WithName("patchWidget");
    }

    /// <summary>
    /// Change some of a widget's fields
    /// </summary>
    public static async Task<ResourceDocument<Widget>> PatchWidget(
        string id, JsonObject request, IWidgetStore store, CancellationToken cancellationToken)
    {
        // A plain body, so the service sees an absent member and an explicit null apart. Checking
        // the document's type, id and every value is the service's own work.
        if (request["data"] is not JsonObject { } data
            || data["type"] is not JsonValue type || type.GetValueKind() != JsonValueKind.String
            || type.GetValue<string>() != "widget")
        {
            throw new InvalidRequestException("A resource of type 'widget' was expected.", "/data/type", "invalid-resource-type");
        }

        if (data["id"] is JsonValue sent && (sent.GetValueKind() != JsonValueKind.String || sent.GetValue<string>() != id))
        {
            throw new InvalidRequestException("The resource's id is not the one in the path.", "/data/id");
        }

        var attributes = data["attributes"] as JsonObject ?? [];
        var widget = await store.FindAsync(id, cancellationToken) ?? throw new NotFoundException("widget", id);

        // Absent or an empty string: unchanged. Null: cleared.
        if (attributes.TryGetPropertyValue("notes", out var notes) && !IsEmptyString(notes))
        {
            widget = widget with { Notes = notes is null ? null : Text(notes, "notes", 10_000) };
        }

        if (attributes.TryGetPropertyValue("name", out var name) && !IsEmptyString(name))
        {
            // Name is required, so it can be changed but not cleared.
            widget = widget with { Name = name is null ? throw Required("name") : Text(name, "name", 100) };
        }

        return UpdateResponse(await store.ReplaceAsync(id, widget, cancellationToken));
    }

    private static bool IsEmptyString(JsonNode? value) =>
        value is JsonValue text && text.GetValueKind() == JsonValueKind.String && text.GetValue<string>().Length == 0;

    private static string Text(JsonNode value, string field, int maxLength) =>
        value is JsonValue text && text.GetValueKind() == JsonValueKind.String && text.GetValue<string>().Length <= maxLength
            ? text.GetValue<string>()
            : throw new InvalidRequestException(
                $"The '{field}' field must be a string of at most {maxLength} characters.", $"/data/attributes/{field}");

    private static InvalidRequestException Required(string field) =>
        new($"The '{field}' field is required.", $"/data/attributes/{field}", "field-required");
}
```

Clearing the notes, and leaving the name alone:

```http
PATCH /api/v1/widgets/62bed180-1f78-45d4-8a56-c996936a2947
Content-Type: application/json
```

```json
{
    "data": {
        "id": "62bed180-1f78-45d4-8a56-c996936a2947",
        "type": "widget",
        "attributes": { "notes": null }
    }
}
```

```json
{
    "data": {
        "id": "62bed180-1f78-45d4-8a56-c996936a2947",
        "type": "widget",
        "attributes": { "name": "Bobby" }
    }
}
```

A body that isn't JSON is refused `422` before the handler runs, as every plain body is.

## What you decide

- **Whether to support `PATCH` at all.** The standard advises against it. A service that offers it
  usually has to offer `PUT` too, and two endpoints then do the same thing.
- **Every check on a `PATCH` body.** Type, `id`, each value's JSON type and bounds, which fields may
  be cleared, and that nothing read-only is changed. The sample checks two fields; a real resource
  needs one check per field it accepts.
- **The OpenAPI description.** The document describes the body as a free-form object. Describe the
  fields in the operation's XML comments.
- **Don't decrypt.** An encrypted member is replaced or cleared as a whole, never read.

## See also

- [Updating resources](updating-resources.md) and [Acting upon resources](acting-upon-resources.md)
- [Bulk updates](../bulk-and-asynchronous-work/bulk-operations/bulk-updates.md)
- The standard:
  [Partial updates](https://contributing.bitwarden.com/architecture/server/apis/partial-updates)
