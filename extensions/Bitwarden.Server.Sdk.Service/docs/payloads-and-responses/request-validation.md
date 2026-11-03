# Request validation

A request is validated in full before anything acts on it, and all its errors are returned at
once. The package binds and validates every request before the handler runs, from the handler's
parameter types, their nullability and their DataAnnotations, and refuses a request that fails with
`422` (or `400` for a resource document that isn't JSON). The standard:
[Request validation](https://contributing.bitwarden.com/architecture/server/apis/request-validation).

## What the package does

| The standard's check                                                          | The package                                                                                                                                                                                                                                                                                                                                     |
| ----------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Every required field, parameter and header is present, not null and not empty | A non-nullable member is required: a resource's attributes, a plain body's reference-typed members, and members of nested objects. A resource attribute that is `null`, `""` or blank counts as absent. Elsewhere `""` passes the implied rule; declare `[Required]` to refuse it. A non-nullable parameter (route, query, header) is required. |
| Every value has the right type, and a parameter converts to it                | A resource attribute of the wrong JSON kind is refused `invalid-type`. A plain body that doesn't deserialize is refused with the serializer's message. A route, query or header value that doesn't convert is refused.                                                                                                                          |
| `sort` and `fields` name real fields                                          | Yes. See [Sorting](../querying/sorting.md) and [Sparse fieldsets](../querying/sparse-fieldsets.md).                                                                                                                                                                                                                                             |
| Constrained values are among the enumerated values                            | Yes, unevenly. See [JSON](json.md#enumerations).                                                                                                                                                                                                                                                                                                |
| Values are within their minimum, maximum and length                           | Every DataAnnotations attribute runs (`[Range]`, `[StringLength]`, `[RegularExpression]`, `[EmailAddress]` and the rest), on nested objects and list items too. A resource's own string members are limited to 255 characters unless they declare `[StringLength]`, `[MaxLength]` or `[Encrypted(MaxLength = ...)]`.                            |
| References to other resources are valid                                       | Not done: only the service can look. Throw `InvalidRequestException`.                                                                                                                                                                                                                                                                           |
| `type` is accepted, and `id` matches the path                                 | `type` is checked. `id` is not; see [Payload shape](payload-shape.md).                                                                                                                                                                                                                                                                          |
| All errors at once                                                            | Within a level, yes, up to 200. A resource document is checked a level at a time: its shape, then each attribute's presence and kind, then the declared rules, then each object's `IValidatableObject`. A request that fails one level is not checked at the next, so an absent `name` is reported without the over-long `colour` beside it.    |

Every refusal is the standard error document, each error pointing at what failed: `source.pointer`
into the body (`/data/attributes/part/code`), or `source.parameter` for a query parameter. The same
rules apply to `[FromForm]` and `[AsParameters]` models.

`[Encrypted(MaxLength = ...)]` (Bitwarden.Server.Sdk.Common) checks ciphertext's length and nothing
else: the value is never decrypted or inspected. Its `MaxLength` must be positive, or validating the
resource and writing the OpenAPI document both fail.

Known gaps in what is checked:

- A member of a nested object declared with C#'s `required` keyword, when absent, or of the wrong
  JSON kind, is a `500`, not a `422`. Declare nested members nullable, or non-nullable without
  `required` (`= null!`), and they are reported like any other.
- The 255-character default doesn't apply to nested objects or plain bodies. Bound their strings
  yourself.

## Example

A resource with constraints on its own members, on a nested object, and across members:

```csharp
using System.ComponentModel.DataAnnotations;
using Bitwarden.Server.Sdk.Common;
using Bitwarden.Server.Sdk.Service.Resources;

public sealed record Widget : Resource, IValidatableObject
{
    // Required, being non-nullable, and at most 100 characters.
    [StringLength(100)]
    public required string Name { get; init; }

    // Ciphertext: only its length is checked.
    [Encrypted(MaxLength = 10_000)]
    public string? Notes { get; init; }

    // Optional, and held to the 255-character default.
    public string? Colour { get; init; }

    [Range(1, 500)]
    public int? Size { get; init; }

    // Its own members are validated too.
    public WidgetPart? Part { get; init; }

    public DateTimeOffset? AvailableDate { get; init; }

    public DateTimeOffset? ExpirationDate { get; init; }

    // A rule across members, checked once every member passes its own.
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (AvailableDate is { } available && ExpirationDate is { } expiration && expiration <= available)
        {
            yield return new ValidationResult(
                "A widget must expire after it becomes available.", [nameof(ExpirationDate)]);
        }
    }
}

public sealed record WidgetPart
{
    [StringLength(20)]
    public string? Label { get; init; }

    [RegularExpression("^[A-Z]{3}$")]
    public string? Code { get; init; }
}
```

A create with three members out of bounds gets all three back:

```json
{
    "errors": [
        {
            "status": "422",
            "code": "invalid-value",
            "title": "A field has an invalid value.",
            "detail": "The field Size must be between 1 and 500.",
            "source": { "pointer": "/data/attributes/size" }
        },
        {
            "status": "422",
            "code": "invalid-value",
            "title": "A field has an invalid value.",
            "detail": "The field Colour must be a string with a maximum length of 255.",
            "source": { "pointer": "/data/attributes/colour" }
        },
        {
            "status": "422",
            "code": "invalid-value",
            "title": "A field has an invalid value.",
            "detail": "The field Code must match the regular expression '^[A-Z]{3}$'.",
            "source": { "pointer": "/data/attributes/part/code" }
        }
    ]
}
```

What only the service can know, it checks in the handler, after everything declared has passed. An
action's plain body is validated the same way:

```csharp
using System.ComponentModel.DataAnnotations;
using Bitwarden.Server.Sdk.Common;
using Bitwarden.Server.Sdk.Common.Errors;
using Bitwarden.Server.Sdk.Service;
using Bitwarden.Server.Sdk.Service.Resources;
using static Bitwarden.Server.Sdk.Service.StandardResponses;

/// <summary>
/// The arguments of the polish action.
/// </summary>
/// <param name="Shine">How hard to polish, from 1 to 10.</param>
/// <remarks>
/// Nullable and [Required], so a body without it is refused rather than read as 0.
/// </remarks>
public sealed record PolishWidget([property: Required, Range(1, 10)] int? Shine);

[ApiTag(Description = "Widgets, which a person keeps.")]
public static class WidgetsEndpoints
{
    public const string Collection = "api/v1/widgets";

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var widgets = endpoints.MapGroup(Collection).WithStandards();
        widgets.MapPost("", CreateWidget);
        widgets.MapPost("{id}/actions/polish", Polish);
    }

    /// <summary>
    /// Create a widget
    /// </summary>
    [StandardCreate]
    public static async Task<ResourceDocument<Widget>> CreateWidget(
        ResourceDocument<Widget> request, IWidgetStore store, CancellationToken cancellationToken)
    {
        // Everything declared has been checked. What needs the store is the service's to check.
        if (await store.NameTakenAsync(request.Data.Name, cancellationToken))
        {
            throw new InvalidRequestException(
                $"A widget called '{request.Data.Name}' already exists.", "/data/attributes/name",
                ErrorCodes.AlreadyExists);
        }

        return CreateResponse(await store.AddAsync(request.Data, cancellationToken));
    }

    /// <summary>
    /// Polish a widget
    /// </summary>
    [StandardAction]
    public static async Task<ResourceDocument<Widget>> Polish(
        string id, PolishWidget request, IWidgetStore store, CancellationToken cancellationToken) =>
        ActionResponse(await store.PolishAsync(id, request.Shine!.Value, cancellationToken));
}
```

`POST /api/v1/widgets/{id}/actions/polish` with `{}` is refused before `Polish` runs:

```json
{
    "errors": [
        {
            "status": "422",
            "code": "invalid-value",
            "title": "A field has an invalid value.",
            "detail": "The Shine field is required.",
            "source": { "pointer": "/shine" }
        }
    ]
}
```

### A route whose shape somebody else fixed

`[AcceptsInvalidRequests]`, on a handler or its class, runs the handler even when binding or
validation failed, for a route that must answer a bad request in its own shape. A body it can't
read binds as `null`, and its bytes are in the request's `UnreadableRequestBody` feature. A body
that isn't JSON is still refused `415`, and a route, query or header value that doesn't convert is
still refused `400`.

```csharp
using Bitwarden.Server.Sdk.Service;

/// <summary>
/// The body an existing client sends to the legacy route.
/// </summary>
public sealed record LegacyWidgetRename(string NewName);

/// <summary>
/// The answer the existing client expects.
/// </summary>
public sealed record LegacyAnswer(bool Ok, string? Message);

[ApiTag(Description = "Routes kept for clients that predate the standards.")]
[AcceptsInvalidRequests]
public static class LegacyWidgetsEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var legacy = endpoints.MapGroup("api/legacy/widgets").WithStandards();
        legacy.MapPost("{id}/rename", Rename);
    }

    /// <summary>
    /// Rename a widget, as existing clients do
    /// </summary>
    public static IResult Rename(string id, LegacyWidgetRename? request, HttpContext http)
    {
        // Not readable: the handler still runs, and answers in the shape its clients know.
        if (request is null)
        {
            var sent = http.Features.Get<UnreadableRequestBody>()?.Body.Length ?? 0;
            return Results.BadRequest(new LegacyAnswer(false, $"Could not read the {sent} bytes sent."));
        }

        return Results.Ok(new LegacyAnswer(true, null));
    }
}
```

## What you decide

- **Bounds.** Give every string the bound its data needs: `[StringLength]` for text,
  `[Encrypted(MaxLength = ...)]` for ciphertext, which is longer than the text it hides.
- **Rules that need data.** A name already taken, a reference that doesn't resolve, a state change
  that isn't allowed: throw `InvalidRequestException` with a pointer and, where a caller branches on
  it, a code such as `ErrorCodes.AlreadyExists`.
- **The `id` in a request.** Compare it with the path yourself.
- **Plain bodies.** Make a number or flag that must be sent nullable and `[Required]`, as
  `PolishWidget` does; otherwise its absence reads as `0` or `false`.
- **`[AcceptsInvalidRequests]`.** Only for routes whose contract predates the standards. The
  handler then validates everything itself.

## See also

- [Errors](errors.md): the error document and `InvalidRequestException`.
- [JSON](json.md): trimming, empty strings and enumerations.
- [Unrecognized fields, query parameters, and headers](../contracts/unrecognized-fields-query-parameters-and-headers.md):
  what is dropped rather than refused.
- [Filtering](../querying/filtering.md): how filter values are checked.
- The standard:
  [Request validation](https://contributing.bitwarden.com/architecture/server/apis/request-validation).
