# Naming conventions

Field names are camel case; a date's name ends in `Date`, a boolean's doesn't start with `is`, and
a reference to another resource ends in `Id`. The package derives every name on the wire from your
C# names, so you meet the standard by naming your members well. The standard:
[Naming conventions](https://contributing.bitwarden.com/architecture/server/apis/naming-conventions).

## What the package does

| The standard says                                    | The package                                                                                                                                      |
| ---------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------ |
| Field names `MUST` be camel case                     | Every member is written and read in camel case: `ExpirationDate` is `expirationDate`. Filter, sort and sparse fieldset names use the same names. |
| A date's name `SHOULD` end in `Date`                 | Not checked. Name the member `...Date`. The inherited audit members don't follow this rule; see [Standard fields](standard-fields.md).           |
| A boolean `MUST NOT` start with `is`                 | Not checked. Name the member `Active`, not `IsActive`.                                                                                           |
| A reference to another resource `SHOULD` end in `Id` | Not checked. Name the member `OrganizationId`.                                                                                                   |

The package names other things on the wire for you, the same way every time:

| What                    | How it is named                                                                                                                     | Example                                                    |
| ----------------------- | ----------------------------------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------- |
| A resource's `type`     | The record's name in kebab case, with no plural. A run of capitals stays one word. `[ResourceType("...")]` replaces it, as written. | `Widget` is `widget`, `ApiKey` is `api-key`                |
| An enumeration's values | The member's name in upper snake case (`EnumValues`), in the JSON and in the OpenAPI document.                                      | `BrushedSteel` is `BRUSHED_STEEL`                          |
| An action               | The method's name in kebab case, or the name passed to `[StandardAction]`, also kebab-cased.                                        | `SendReminder` is `actions/send-reminder`                  |
| A filter                | The handler parameter's name.                                                                                                       | `Filter<Guid>? organizationId` is `filter[organizationId]` |
| A sparse fieldset       | The resource's `type`.                                                                                                              | `fields[widget]`                                           |
| The service             | The identifier passed to `Service`: lowercase letters, digits and hyphens, or the constructor refuses it.                           | `widget-service`                                           |

## Example

Each member's comment is its name on the wire:

```csharp
using System.ComponentModel.DataAnnotations;
using Bitwarden.Server.Sdk.Common;
using Bitwarden.Server.Sdk.Service.Resources;

public sealed record Widget : Resource
{
    // name
    [StringLength(100)]
    public required string Name { get; init; }

    // notes
    [Encrypted(MaxLength = 10_000)]
    public string? Notes { get; init; }

    // active, not isActive
    public bool Active { get; init; }

    // expirationDate: a date ends in Date
    public DateTimeOffset? ExpirationDate { get; init; }

    // organizationId: another resource's identifier ends in Id
    public Guid? OrganizationId { get; init; }

    // finish: PAINTED or BRUSHED_STEEL
    public WidgetFinish? Finish { get; init; }
}

public enum WidgetFinish
{
    Painted,
    BrushedSteel,
}
```

When the record's name isn't the resource's name, `[ResourceType]` says what it is:

```csharp
/// <summary>
/// A widget template. The record's name would make its type "widget-template-resource".
/// </summary>
[ResourceType("widget-template")]
public sealed record WidgetTemplateResource : Resource
{
    [StringLength(100)]
    public required string Name { get; init; }

    // shared
    public bool Shared { get; init; }
}
```

## What you decide

- **Member names.** The rules on dates, booleans and references are yours to follow; nothing warns
  you. The OpenAPI document shows every name as callers see it, so review it with the code.
- **Renaming with `[JsonPropertyName]`.** Avoid it. The JSON follows the new name, but a validation
  error still points at the C# name: a `Caption` written as `label` that is too long is reported at
  `/data/attributes/caption`, as "The field Caption ...". Give the C# member the name you want on
  the wire instead.
- **`[ResourceType]` values.** They are used as written, so write them in lowercase kebab case and
  singular, as the derived ones are. They are not checked.
- **Enumeration members.** Their names become the values callers send, so rename one only as a
  breaking change. Keep them ASCII.

## See also

- [Payload shape](payload-shape.md): where `type` and the attributes appear.
- [JSON](json.md): how enumeration values are read.
- [Standard fields](standard-fields.md): the names every resource inherits.
- [API paths](../contracts/api-paths.md): naming collections and paths.
- The standard:
  [Naming conventions](https://contributing.bitwarden.com/architecture/server/apis/naming-conventions).
