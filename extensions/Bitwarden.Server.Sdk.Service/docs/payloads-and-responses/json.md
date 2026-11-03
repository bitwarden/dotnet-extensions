# JSON

Dates are ISO 8601 with a time zone, preferably UTC; a fixed set of values is written in capitals
and read without regard to case; strings are trimmed, an empty string counts as absent, absent
counts as `null`, and responses leave `null` out. The package configures the serializer every
endpoint uses to do most of this, and some of it only for resource attributes. The standard:
[JSON](https://contributing.bitwarden.com/architecture/server/apis/json).

## What the package does

| The standard says                                                 | The package                                                                                                                                                                                                                                                                                           |
| ----------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Dates `MUST` be ISO 8601 with a zone, and `SHOULD` be UTC         | `DateTimeOffset` and `DateTime` are written in UTC with a literal `Z` (`2027-03-01T07:30:00Z`), the fraction to the tick with trailing zeros left off (`UtcDates`). Any ISO 8601 offset is read; a `DateTime` without a zone is taken as UTC. The OpenAPI document types them as `date-time` strings. |
| Fixed values `SHOULD` be all caps, and `MUST` be read in any case | Enumerations are written as their names in upper snake case (`SpecialEdition` is `SPECIAL_EDITION`), in the JSON and in the OpenAPI document's `enum` lists (`EnumValues`). Reading them is uneven: see [Enumerations](#enumerations).                                                                |
| Strings `SHOULD` be trimmed                                       | A resource's string attributes are trimmed before validation. Strings inside a nested object, and in a body that isn't a resource document, are not.                                                                                                                                                  |
| An empty string `MUST` count as absent                            | A resource attribute that is `""` or blank is dropped before validation, so a required one is refused `field-required` and an optional one is `null`. Inside a nested object, and in a body that isn't a resource document, `""` is kept as sent.                                                     |
| Absent `MUST` count as `null`                                     | An absent member binds as `null`. A non-nullable member of a resource is required. In a plain body an absent value type binds as its default (`0`, `false`), so declare it nullable and `[Required]` to refuse its absence.                                                                           |
| Responses `SHOULD NOT` include `null` fields                      | Every response, error documents included, leaves out members whose value is `null`.                                                                                                                                                                                                                   |

Two more things the serializer does:

- **Field names** are camel case. See [Naming conventions](naming-conventions.md).
- **A member typed `JsonElement` or a `JsonNode`** takes any JSON value as sent: not trimmed, not
  type-checked, not validated. The service checks it itself.

### Enumerations

How a value is read depends on where it arrives:

| Where                                    | What is read                                                                                                                                                                                    |
| ---------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| A `filter[...]` parameter (`Filter<T>`)  | The upper snake case name in any case. Anything else is refused `422`.                                                                                                                          |
| A resource attribute                     | The upper snake case name exactly. A name that is not the enumeration's is refused `422` with the permitted values; the right name in another case passes that check and then fails as a `500`. |
| A plain JSON body                        | The upper snake case name exactly, or a number (any number, defined or not). Anything else is refused `422` with the serializer's message.                                                      |
| A plain route, query or header parameter | The C# member name exactly, or a number (any number), as ASP.NET Core parses an enumeration. Anything else, the upper snake case name included, is refused `422` or `400`.                      |

Until the package reads every one of them without regard to case, use `Filter<T>` for a filter, and
for any other parameter or plain-body member take a `string` and read it with
`EnumValues.TryParse`, which matches the published names in any case. A resource attribute keeps its
enumeration type, and callers must send the name in capitals.

## Example

A resource with a date, an enumeration and a free-form member. `WidgetKind` needs nothing to be
written as `PLAIN` and `SPECIAL_EDITION`:

```csharp
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Bitwarden.Server.Sdk.Common;
using Bitwarden.Server.Sdk.Service.Resources;

/// <summary>
/// How a widget is made. Written as PLAIN and SPECIAL_EDITION.
/// </summary>
public enum WidgetKind
{
    Plain,
    SpecialEdition,
}

public sealed record Widget : Resource
{
    [StringLength(100)]
    public required string Name { get; init; }

    [Encrypted(MaxLength = 10_000)]
    public string? Notes { get; init; }

    /// <summary>
    /// How the widget is made.
    /// </summary>
    public WidgetKind Kind { get; init; }

    /// <summary>
    /// When the widget stops working, if it does.
    /// </summary>
    public DateTimeOffset? ExpirationDate { get; init; }

    /// <summary>
    /// Settings a client keeps for the widget, as a JSON object.
    /// </summary>
    public JsonElement? Settings { get; init; }
}
```

The handlers check the free-form member, filter by the enumeration, and write a date into a message
the way the JSON writes one, with `UtcDates.Text`:

```csharp
using System.Text.Json;
using Bitwarden.Server.Sdk.Common;
using Bitwarden.Server.Sdk.Service;
using Bitwarden.Server.Sdk.Service.Resources;
using Bitwarden.Server.Sdk.Service.Querying;
using static Bitwarden.Server.Sdk.Service.StandardResponses;

/// <summary>
/// The arguments of the expire action.
/// </summary>
public sealed record ExpireWidget(DateTimeOffset ExpirationDate);

[ApiTag(Description = "Widgets, which a person keeps.")]
public static class WidgetsEndpoints
{
    public const string Collection = "api/v1/widgets";

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var widgets = endpoints.MapGroup(Collection).WithStandards();
        widgets.MapPost("", CreateWidget);
        widgets.MapGet("", ListWidgets);
        widgets.MapPost("{id}/actions/expire", Expire);
    }

    /// <summary>
    /// Create a widget
    /// </summary>
    [StandardCreate]
    public static async Task<ResourceDocument<Widget>> CreateWidget(
        ResourceDocument<Widget> request, IWidgetStore store, CancellationToken cancellationToken)
    {
        // A free-form member takes any JSON value, so the service checks it itself.
        if (request.Data.Settings is { ValueKind: not JsonValueKind.Object })
        {
            throw new InvalidRequestException(
                "The 'settings' field must be an object.", "/data/attributes/settings");
        }

        return CreateResponse(await store.AddAsync(request.Data, cancellationToken));
    }

    /// <summary>
    /// List widgets
    /// </summary>
    /// <param name="kind">Only widgets made this way, such as SPECIAL_EDITION.</param>
    [StandardReadMany]
    public static async Task<ResourceListDocument<Widget>> ListWidgets(
        Filter<WidgetKind>? kind, IWidgetStore store, CancellationToken cancellationToken) =>
        ReadManyResponse(await store.ListAsync(kind?.Value, cancellationToken));

    /// <summary>
    /// Expire a widget
    /// </summary>
    [StandardAction]
    public static async Task<ResourceDocument<Widget>> Expire(
        string id, ExpireWidget request, IWidgetStore store, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;

        if (request.ExpirationDate < now)
        {
            // UtcDates.Text writes a date in a message as the JSON does: UTC, with a Z.
            throw new InvalidRequestException(
                $"A widget can't expire before now ({UtcDates.Text(now)}).", "/expirationDate");
        }

        return ActionResponse(await store.ExpireAsync(id, request.ExpirationDate, cancellationToken));
    }
}
```

A create with a padded name, an empty `notes` and a date at another offset:

```http
POST /api/v1/widgets
Content-Type: application/json

{"data":{"type":"widget","attributes":{"name":"  Sprocket ","notes":"","kind":"SPECIAL_EDITION","expirationDate":"2027-03-01T09:30:00+02:00","settings":{"theme":"dark"}}}}
```

The name comes back trimmed, `notes` was absent and so is left out, and the date is in UTC:

```http
HTTP/1.1 201 Created
Content-Type: application/json; charset=utf-8
Location: /api/v1/widgets/62bed180-1f78-45d4-8a56-c996936a2947

{"data":{"id":"62bed180-1f78-45d4-8a56-c996936a2947","type":"widget","attributes":{"name":"Sprocket","kind":"SPECIAL_EDITION","expirationDate":"2027-03-01T07:30:00Z","settings":{"theme":"dark"}}}}
```

A value the enumeration doesn't have is refused with the ones it does:

```json
{
    "errors": [
        {
            "status": "422",
            "code": "invalid-value",
            "title": "A field has an invalid value.",
            "detail": "The 'kind' field must be one of: PLAIN, SPECIAL_EDITION.",
            "source": { "pointer": "/data/attributes/kind" }
        }
    ]
}
```

## What you decide

- **`DateTimeOffset` or `DateTime`.** Both are written the same way. Keep `DateTimeOffset` for an
  instant; the offset a caller sent is not kept, and is not sent back.
- **A string kept as sent.** Mark it `[Verbatim]` and it is neither trimmed nor dropped when blank.
  See [Standard fields](standard-fields.md).
- **Nested objects and plain bodies.** Trim their strings, and treat `""` as absent, in the handler.
  A declared `[Required]` refuses `""` and blank strings, where the implied one does not.
- **Free-form members.** Check the kind and shape of a `JsonElement` before you store it, as
  `CreateWidget` does.
- **Enumerations outside resource attributes.** Take them as `Filter<T>`, or as a string read with
  `EnumValues.TryParse` (see [Enumerations](#enumerations)).

## See also

- [Naming conventions](naming-conventions.md): field names and enumeration values.
- [Request validation](request-validation.md): what is refused, and how.
- [Filtering](../querying/filtering.md): `Filter<T>` and ranges of dates.
- The standard: [JSON](https://contributing.bitwarden.com/architecture/server/apis/json).
