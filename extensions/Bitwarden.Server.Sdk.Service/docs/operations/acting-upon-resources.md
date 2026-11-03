# Acting upon resources

The standard exposes an operation that isn't a create, read, update or delete as an action:
`POST /api/v1/{plural}/{id}/actions/{action}`, named in kebab case, with any JSON body it needs. A
field that only actions change must be read-only. The package gives `[StandardAction]`, which builds
the route from the method's name, and `ActionResponse` for an action that returns the resource. The
standard:
[Acting upon resources](https://contributing.bitwarden.com/architecture/server/apis/acting-upon-resources).

## What the package does

| The standard                                           | The package                                                                                            |
| ------------------------------------------------------ | ------------------------------------------------------------------------------------------------------ |
| The verb `MUST` be `POST`                              | `[StandardAction]` is a `POST`; the service won't start if its `Map` line uses another verb.           |
| The path `SHOULD` be `/{plural}/{id}/actions/{action}` | `[StandardAction]` builds `{id}/actions/{action}`; the `Map` line must use it.                         |
| The action named in kebab case                         | The name is the method's, kebab-cased (`SendEmail` is `send-email`). A name passed is kebab-cased too. |
| No restriction on the JSON posted                      | The body is a plain JSON object bound to your type and validated, not a resource document.             |
| A field only actions change `MUST` be read-only        | Mark it `[ReadOnly]`: it is documented `readOnly` and dropped from creates and updates.                |
| Success `SHOULD` be `204`, `MAY` be `200` or `202`     | `204` when the handler returns nothing, `200` when it returns something.                               |

- **The `template` argument** names the resource the action is on, relative to the group. It
  defaults to `{id}`. A singleton the caller's scope names gives its literal instead:
  `[StandardAction("reset", template: "current")]` is `current/actions/reset`. The
  `actions/{action}` part is always the platform's.
- **Naming.** An acronym stays one word: `VerifySsoToken` and `VerifySSOToken` are both
  `verify-sso-token`. The operation identifier is the method's name in camel case (`archiveWidget`),
  whatever the action is called.
- **The body.** A body type's members are validated with data annotations, and a failure is refused
  `422` with a pointer such as `/shine`. An action with no body parameter takes none.
- **Authorization.** An action changes something, so it demands the write scope unless it declares
  a scope of its own. Give an action its own `[Authorization]` where it needs a narrower permission
  than an update.

## Example

Three actions: one with arguments that returns the widget, one that returns nothing, and one on a
singleton:

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
        widgets.MapPost("{id}/actions/polish", Polish);
        widgets.MapPost("{id}/actions/archive", ArchiveWidget);
        widgets.MapPost("current/actions/reset", Reset);
    }

    /// <summary>
    /// Polish a widget
    /// </summary>
    [StandardAction]
    public static async Task<ResourceDocument<Widget>> Polish(
        string id, PolishWidget request, IWidgetStore store, CancellationToken cancellationToken) =>
        ActionResponse(await store.PolishAsync(id, request.Shine, cancellationToken)
            ?? throw new NotFoundException("widget", id));

    /// <summary>
    /// Archive a widget
    /// </summary>
    [StandardAction("archive")]
    public static async Task ArchiveWidget(string id, IWidgetStore store, CancellationToken cancellationToken)
    {
        if (!await store.ArchiveAsync(id, cancellationToken))
        {
            throw new InvalidRequestException("Only an active widget can be archived.", code: "invalid-state");
        }
    }

    /// <summary>
    /// Reset the caller's widgets
    /// </summary>
    [StandardAction("reset", template: "current")]
    public static Task Reset(IWidgetStore store, CancellationToken cancellationToken) =>
        store.ResetAsync(cancellationToken);
}

/// <summary>
/// How to polish a widget.
/// </summary>
/// <param name="Shine">How much shine to add.</param>
public sealed record PolishWidget([property: Range(1, 10)] int Shine);
```

The widget's status changes only through actions, so it is read-only:

```csharp
public sealed record Widget : Resource
{
    [StringLength(100)]
    public required string Name { get; init; }

    [Encrypted(MaxLength = 10_000)]
    public string? Notes { get; init; }

    /// <summary>
    /// Where the widget is in its life. Changed only by its actions.
    /// </summary>
    [ReadOnly]
    public WidgetStatus? Status { get; init; }
}

public enum WidgetStatus
{
    Active,
    Archived,
}
```

`[ReadOnly]` is `Bitwarden.Server.Sdk.Service.Resources.ReadOnlyAttribute`, not the one in
`System.ComponentModel`.

Polishing posts the arguments as they are, with no `data` envelope:

```http
POST /api/v1/widgets/62bed180-1f78-45d4-8a56-c996936a2947/actions/polish
Content-Type: application/json
```

```json
{ "shine": 3 }
```

```json
{
    "data": {
        "id": "62bed180-1f78-45d4-8a56-c996936a2947",
        "type": "widget",
        "attributes": { "name": "Sprocket", "status": "ACTIVE" }
    }
}
```

An argument out of range:

```json
{
    "errors": [
        {
            "status": "422",
            "code": "invalid-value",
            "title": "A field has an invalid value.",
            "detail": "The field Shine must be between 1 and 10.",
            "source": { "pointer": "/shine" }
        }
    ]
}
```

Archiving answers `204` with no body, and archiving a widget that is already archived:

```json
{
    "errors": [
        {
            "status": "422",
            "code": "invalid-state",
            "title": "A field has an invalid value.",
            "detail": "Only an active widget can be archived."
        }
    ]
}
```

## What you decide

- **What is an action.** Anything with its own rules or its own permission: a status transition,
  sending an email, adding one element to a collection. Don't model these as writes to a field.
- **Which errors.** A transition that isn't allowed from the current state is the caller's mistake,
  `422` (`InvalidRequestException`, with a code the caller can branch on). A race with another write
  is `409` (`ConflictException`).
- **`202` for an action that is scheduled.** Return an `IResult` of your own. See
  [Jobs](../bulk-and-asynchronous-work/jobs.md).
- **Arguments that are Vault Data.** An argument a person encrypts is declared `[Encrypted]` on the
  body type, and stored as it came.

## See also

- [Partial updates](partial-updates.md), where an action is the recommended way to change one field
- [Bulk actions](../bulk-and-asynchronous-work/bulk-operations/bulk-actions.md)
- [Authentication and authorization](../beyond-the-standards/authentication-and-authorization.md)
- The standard:
  [Acting upon resources](https://contributing.bitwarden.com/architecture/server/apis/acting-upon-resources)
