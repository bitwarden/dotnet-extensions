# Standard fields

Where a resource records its version, who created, changed or deleted it, and when, it uses the
standard's field names. Every resource inherits members for these from `Resource`, and two
attributes say how a caller may use a member: `[ReadOnly]` and `[Verbatim]`. The standard:
[Standard fields](https://contributing.bitwarden.com/architecture/server/apis/standard-fields).

## What the package does

Every resource inherits these members. Each is left out of a response while it is `null`, so a
resource that doesn't record something says nothing about it.

| The standard's field | Inherited member | On the wire | A caller may set it | Notes                                                                               |
| -------------------- | ---------------- | ----------- | ------------------- | ----------------------------------------------------------------------------------- |
| (`id`)               | `Id`             | `id`        | Yes                 | A `string?`. See [Payload shape](payload-shape.md).                                 |
| (`type`)             | `Type`           | `type`      | Yes, and checked    | Set by the constructor from the record's name.                                      |
| `version`            | `Version`        | `version`   | Yes                 | A `string?`. See [Optimistic concurrency](../operations/optimistic-concurrency.md). |
| `createdBy`          | `CreatedBy`      | `createdBy` | No                  |                                                                                     |
| `creationDate`       | `CreatedAt`      | `createdAt` | No                  | Not the standard's name.                                                            |
| `updatedBy`          | `UpdatedBy`      | `updatedBy` | No                  |                                                                                     |
| `revisionDate`       | `UpdatedAt`      | `updatedAt` | No                  | Not the standard's name.                                                            |
| `deletedBy`          | `DeletedBy`      | `deletedBy` | No                  | See [soft deletes](../operations/deleting-resources.md).                            |
| `deletedDate`        | `DeletedAt`      | `deletedAt` | No                  | Not the standard's name.                                                            |

The standard says resources `MUST` use its names, and three of the inherited members don't. See
[What you decide](#what-you-decide) for how a service uses the standard's names today.

The two attributes, both in `Bitwarden.Server.Sdk.Service.Resources`:

- **`[ReadOnly]`** marks a member a caller reads but never sets. The audit members carry it. A
  read-only member in a request is dropped before validation, not refused, so a caller can send back
  what it read. The OpenAPI document marks it `readOnly`. If a file also imports
  `System.ComponentModel`, which has its own `ReadOnlyAttribute`, the compiler asks you to qualify
  it.
- **`[Verbatim]`** keeps a string member as the caller sent it: not trimmed, and a blank value kept
  rather than read as absent. An absent or `null` value is still absent. Its declared validation
  still runs, so spaces count towards its length.

## Example

A resource with a member only the service sets, and one whose spaces matter:

```csharp
using System.ComponentModel.DataAnnotations;
using Bitwarden.Server.Sdk.Common;
using Bitwarden.Server.Sdk.Service.Resources;

public sealed record Widget : Resource
{
    [StringLength(100)]
    public required string Name { get; init; }

    [Encrypted(MaxLength = 10_000)]
    public string? Notes { get; init; }

    /// <summary>
    /// How many times the widget has been polished. The service counts; a caller only reads it.
    /// </summary>
    [ReadOnly]
    public int PolishCount { get; init; }

    /// <summary>
    /// The text engraved on the widget, spaces and all.
    /// </summary>
    [Verbatim]
    [StringLength(40)]
    public string? Engraving { get; init; }
}
```

The service fills in the inherited members from what it stores, checks the caller's `version`, and
records who made the change:

```csharp
using Bitwarden.Server.Sdk.Common;
using Bitwarden.Server.Sdk.Service;
using Bitwarden.Server.Sdk.Service.Resources;
using Bitwarden.Server.Sdk.Service.Querying;
using static Bitwarden.Server.Sdk.Service.StandardResponses;

/// <summary>
/// Replace a widget
/// </summary>
[Authorization(User = ContextRequirement.Required)]
[StandardUpdate]
public static async Task<ResourceDocument<Widget>> UpdateWidget(
    string id, ResourceDocument<Widget> request, Person person, WidgetDatabase database,
    CancellationToken cancellationToken)
{
    var entity = await database.FindAsync(id, cancellationToken)
        ?? throw new NotFoundException("widget", id);

    // 409 when the caller sent a version that is no longer the stored one.
    Concurrency.Require(request.Data.Version, entity.Version);

    entity.Name = request.Data.Name;
    entity.Notes = request.Data.Notes;
    entity.Engraving = request.Data.Engraving;
    entity.UpdatedAt = DateTimeOffset.UtcNow;
    entity.UpdatedBy = person.Id;
    entity.Version++;
    await database.SaveAsync(entity, cancellationToken);

    return UpdateResponse(ToResource(entity));
}

// The service fills in the inherited members from what it stores.
private static Widget ToResource(WidgetEntity entity) => new()
{
    Id = entity.Id.ToString(),
    Version = entity.Version.ToString(System.Globalization.CultureInfo.InvariantCulture),
    Name = entity.Name,
    Notes = entity.Notes,
    Engraving = entity.Engraving,
    PolishCount = entity.PolishCount,
    CreatedAt = entity.CreatedAt,
    CreatedBy = entity.CreatedBy.ToString(),
    UpdatedAt = entity.UpdatedAt,
    UpdatedBy = entity.UpdatedBy?.ToString(),
};
```

A `polishCount` or `updatedAt` in the request is dropped, so the caller can't set either. The
`engraving` reaches the handler with its spaces.

## What you decide

- **Which fields a resource records.** The package stores nothing. Set the inherited members from
  your own data, as `ToResource` does, and leave the rest `null`.
- **The standard's date names.** Until the inherited names change, a service that must use
  `creationDate`, `revisionDate` and `deletedDate` declares them itself and leaves `CreatedAt`,
  `UpdatedAt` and `DeletedAt` unset, so they are left out:

    ```csharp
    public sealed record Widget : Resource
    {
        [StringLength(100)]
        public required string Name { get; init; }

        [Encrypted(MaxLength = 10_000)]
        public string? Notes { get; init; }

        // The standard's names. Leave CreatedAt, UpdatedAt and DeletedAt unset, and they are left out.
        [ReadOnly]
        public DateTimeOffset? CreationDate { get; init; }

        [ReadOnly]
        public DateTimeOffset? RevisionDate { get; init; }

        [ReadOnly]
        public DateTimeOffset? DeletedDate { get; init; }
    }
    ```

- **What a version is.** `Version` is a string on the wire. `Concurrency.Require` compares it with a
  stored `int`; a service that versions some other way compares it itself and throws
  `ConflictException`.
- **Who `createdBy` names.** The acting user, from `Person` or `IContext.UserId`. A change made by a
  service with no person behind it has no user to record.
- **Members only the service sets.** Mark each `[ReadOnly]`. Without it, a caller's value reaches
  the handler.

## See also

- [Optimistic concurrency](../operations/optimistic-concurrency.md): `version` and `409`.
- [Deleting resources](../operations/deleting-resources.md): soft deletes and `deletedAt`.
- [JSON](json.md): how strings are trimmed, and how dates are written.
- [Naming conventions](naming-conventions.md)
- The standard:
  [Standard fields](https://contributing.bitwarden.com/architecture/server/apis/standard-fields).
