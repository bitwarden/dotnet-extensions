# Bulk updates

A bulk update applies one change, `update`, to every targeted resource, with partial-update
semantics: an absent field is left alone and an explicit `null` clears it. The package reads the
targeting and requires `update`; reading and validating `update`, and applying it, is your
handler's job. The standard:
[Bulk updates](https://contributing.bitwarden.com/architecture/server/apis/bulk-operations/bulk-updates).

## What the package does

| The standard's rule                                                                      | The package                                                                                                                                                                                                                  |
| ---------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `update` is present (`MUST`)                                                             | `BulkUpdate<T>.Update` is required; a request without it is refused `422`.                                                                                                                                                   |
| Partial-update semantics                                                                 | `Update` is an `IReadOnlyDictionary<string, JsonElement>`, by the fields' published names, exactly as sent. An absent field is absent from it, and an explicit `null` is a `JsonValueKind.Null` value. Applying it is yours. |
| Only fields the single update accepts; a read-only field is refused `422` (`MUST`)       | Not enforced. Refuse any field you do not accept, as below.                                                                                                                                                                  |
| The single update's rules apply to each resource, as it is and after the change (`MUST`) | Not enforced. `update` is raw JSON, so even the resource's data annotations (`[StringLength]`, `[Encrypted(MaxLength = ...)]`) are not applied to it. Check each value yourself.                                             |
| No version check, but the version increments on every changed resource (`MUST`)          | Not enforced. Increment it as you save.                                                                                                                                                                                      |
| An `update` including `version` is refused `422` (`MUST`)                                | Not enforced. Refuse it with the other read-only fields.                                                                                                                                                                     |

Targeting is `ids` or `filter`, as `Targeting` on `[StandardBulkUpdate(collection)]` allows, and
`Supports` sets the atomicity modes. See [Targeting](./targeting.md).

## Example

The endpoint reads the change first, so a bad `update` refuses the request before any widget is
looked at, then holds each matched widget to the single update's rules:

```csharp
using System.Text.Json;
using Bitwarden.Server.Sdk.Common;
using Bitwarden.Server.Sdk.Common.Errors;
using Bitwarden.Server.Sdk.Service;
using Bitwarden.Server.Sdk.Service.Querying;

[ApiTag(Description = "Widgets, which a person keeps.")]
public static class WidgetsEndpoints
{
    public const string Collection = "api/v1/widgets";

    public static void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapPost($"{Collection}:bulk-update", UpdateWidgets).WithStandards();

    /// <summary>
    /// Update widgets
    /// </summary>
    /// <remarks>
    /// Changes `name` or `notes` on every widget a `filter` matches. All-or-nothing. Responds
    /// synchronously.
    /// </remarks>
    [StandardBulkUpdate(Collection, Targeting = Targeting.Filter)]
    public static async Task<BulkResult> UpdateWidgets(
        BulkUpdate<Widget> request, IContext context, IWidgetStore store, CancellationToken cancellationToken)
    {
        // The update is a request-level check: a bad one refuses the request before any widget.
        var change = WidgetChange.From(request.Update);

        var widgets = await store.ListAsync(
            store.Visible(context.OrganizationId).Matching(request.Filter!), cancellationToken);

        var failures = new List<Error>();
        foreach (var widget in widgets)
        {
            // The single update's rules, held against the widget as it is.
            if (widget.Archived)
            {
                failures.Add(BulkFailure.At(
                    new InvalidRequestException("An archived widget cannot be changed.", code: "archived"),
                    "/filter") with
                {
                    Source = null,
                    Meta = new Dictionary<string, object?> { ["resource"] = new FailedResource("widget", widget.Id) },
                });
                continue;
            }

            change.ApplyTo(widget);

            // No version check, but every widget that changes moves to a new version.
            widget.Version++;
        }

        if (failures.Count > 0)
        {
            throw new BulkRefusedException(failures);
        }

        await store.SaveAsync(widgets, cancellationToken);

        return BulkResult.Affected(widgets.Count);
    }
}
```

`WidgetChange` accepts only the fields a single update may change, checks each value as the
resource's own annotations would, and keeps the difference between an absent field and a `null`:

```csharp
/// <summary>
/// The fields a bulk update may change, read from its <c>update</c> member.
/// </summary>
public sealed class WidgetChange
{
    private bool _setsName;
    private string _name = "";
    private bool _setsNotes;
    private string? _notes;

    public static WidgetChange From(IReadOnlyDictionary<string, JsonElement> update)
    {
        var change = new WidgetChange();

        foreach (var (field, value) in update)
        {
            var pointer = $"/update/{field}";

            switch (field)
            {
                case "name":
                    // Required on the resource, so it can be changed but never cleared.
                    if (value.ValueKind != JsonValueKind.String || value.GetString() is not { Length: > 0 and <= 100 } name)
                    {
                        throw new InvalidRequestException("'name' is a string of 1 to 100 characters.", pointer);
                    }

                    (change._setsName, change._name) = (true, name);
                    break;

                case "notes":
                    // Protected Data: an explicit null clears it, and a string is stored as sent.
                    // Only its length is checked, as [Encrypted] would on a single update.
                    if (value.ValueKind == JsonValueKind.Null)
                    {
                        (change._setsNotes, change._notes) = (true, null);
                    }
                    else if (value.ValueKind == JsonValueKind.String && value.GetString() is { Length: <= 10_000 } notes)
                    {
                        (change._setsNotes, change._notes) = (true, notes);
                    }
                    else
                    {
                        throw new InvalidRequestException("'notes' is an encrypted string of at most 10000 characters.", pointer);
                    }

                    break;

                default:
                    // version, id, createdAt and the rest are read-only, or not fields at all.
                    throw new InvalidRequestException($"'{field}' cannot be changed by a bulk update.", pointer);
            }
        }

        return change;
    }

    public void ApplyTo(WidgetRow widget)
    {
        if (_setsName)
        {
            widget.Name = _name;
        }

        if (_setsNotes)
        {
            widget.Notes = _notes;
        }
    }
}
```

`notes` holds Protected Data, ciphertext the person's device produced. The service checks only its
length, stores it as sent, and never decrypts or inspects it.

An `update` naming `version` is refused before any widget is changed:

```http
POST /api/v1/widgets:bulk-update
Content-Type: application/json

{"filter":{"==":[{"var":"name"},"Gear"]},"update":{"version":"3"}}
```

```http
HTTP/1.1 422 Unprocessable Entity
Content-Type: application/json; charset=utf-8

{"errors":[{"status":"422","code":"invalid-value","title":"A field has an invalid value.","detail":"'version' cannot be changed by a bulk update.","source":{"pointer":"/update/version"}}]}
```

A matched widget that breaks a rule is identified by `meta.resource`, since a filter gives it no
position in the request:

```http
HTTP/1.1 422 Unprocessable Entity
Content-Type: application/json; charset=utf-8

{"errors":[{"status":"422","code":"archived","title":"A field has an invalid value.","detail":"An archived widget cannot be changed.","meta":{"resource":{"type":"widget","id":"0d4d2a6e-8a0f-4d55-9d0c-0f8f8b3b2f10"}}}]}
```

## What you decide

- **Which fields a bulk update may change.** Only those the single update accepts. A field that
  changes only through an action, such as a status, is read-only here: changing it for many
  resources is a [bulk action](./bulk-actions.md).
- **How each value is validated.** Repeat the resource's own rules: required members can't be
  cleared, lengths and `[Encrypted]` bounds hold, and enumerations take their published names.
- **Targeting.** A large `filter` match is loaded and checked before it is written, so set a
  maximum on what one request may change, or offer `ids` targeting only.

## See also

- [Bulk operations](./index.md)
- [Targeting](./targeting.md)
- [Bulk responses](./bulk-responses.md)
- [Bulk actions](./bulk-actions.md)
- The standard:
  [Bulk updates](https://contributing.bitwarden.com/architecture/server/apis/bulk-operations/bulk-updates)
  and [Partial updates](https://contributing.bitwarden.com/architecture/server/apis/partial-updates)
