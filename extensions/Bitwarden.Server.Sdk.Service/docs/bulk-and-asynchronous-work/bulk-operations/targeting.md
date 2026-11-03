# Targeting

A bulk request names its resources in exactly one way: `data` (each item carries its own content),
`ids`, or a `filter` expression. It never reaches a resource the caller could not reach one at a
time. The package reads the form, refuses a request that supplies the wrong number of them, and
checks the expression; keeping the request within the caller's reach is your handler's job. The
standard:
[Targeting](https://contributing.bitwarden.com/architecture/server/apis/bulk-operations/targeting).

## What the package does

`BulkUpdate<T>`, `BulkDelete<T>` and the `BulkAction` types derive from `BulkTargeting<T>`, which
carries `Ids` (`IReadOnlyList<string>?`) and `Filter` (`Query?`). `BulkCreate<T>` and
`BulkReplace<T>` carry `Data` instead.

| The standard's rule                                        | The package                                                                                                                                                                                                                                                           |
| ---------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Exactly one form of targeting (`MUST`)                     | Both `ids` and `filter` is refused `422` at `/ids`; neither is refused `422` at `/ids`, saying which form the operation accepts. A form the attribute's `Targeting` excludes is refused `422` at `/ids` or `/filter`.                                                 |
| A `filter` is not empty (`MUST NOT`)                       | `{}` is refused `422`. An expression may use only `var`, `==`, `!=`, `>`, `>=`, `<`, `<=`, `in`, `and`, `or` and `!`, hold at most 500 terms (`Query.MaxTerms`), and name only fields the resource marks `[Filterable]`. Anything else is refused `422` at `/filter`. |
| `ids` and `data` never name a resource twice (`MUST NOT`)  | `BulkRequests.RequireDistinct(ids)` refuses a repeated identifier `422` at its second position, compared without regard to case. You call it. Two bulk-create items sharing a `lid` are refused by the platform. Repeated `id`s in a bulk replace are yours to check. |
| An empty `ids` or `data` is valid                          | Accepted. `{"ids":[]}` reaches your handler, and `BulkResult.Affected(0)` answers `{"meta":{"affectedCount":0}}`.                                                                                                                                                     |
| More than the documented maximum is refused `422` (`MUST`) | `BulkRequests.RequireAtMost(count, maximum, pointer)`. You call it, with the maximum you document.                                                                                                                                                                    |
| Targeting never widens the caller's reach (`MUST NOT`)     | Not enforced by the package. Start every lookup from what the caller may see, as below.                                                                                                                                                                               |

`Matching` (on `IQueryable<T>`) turns a checked expression into a predicate, translating each field
to the entity property it is stored as (`[Filterable(MappedTo = ...)]` names it when the names
differ). It throws `InvalidOperationException` for an expression read from a request
that the platform never checked, so take expressions only through the bulk and search request
types on their attributes.

## Example

This bulk delete accepts either form. Both paths start from the widgets the caller may see, so a
filter cannot match a widget outside them, and a named widget the caller may not see fails exactly
as a missing one does:

```csharp
using Bitwarden.Server.Sdk.Common;
using Bitwarden.Server.Sdk.Service;
using Bitwarden.Server.Sdk.Service.Querying;

[ApiTag(Description = "Widgets, which a person keeps.")]
public static class WidgetsEndpoints
{
    public const string Collection = "api/v1/widgets";

    public static void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapPost($"{Collection}:bulk-delete", DeleteWidgets).WithStandards();

    /// <summary>
    /// Delete widgets
    /// </summary>
    /// <remarks>
    /// Names the widgets by `ids`, at most 500, or describes them with a `filter`. All-or-nothing.
    /// Responds synchronously.
    /// </remarks>
    [StandardBulkDelete(Collection)]
    public static async Task<BulkResult> DeleteWidgets(
        BulkDelete<Widget> request, IContext context, IWidgetStore store, CancellationToken cancellationToken)
    {
        // Every lookup starts from what the caller may see, so targeting never widens their reach.
        var visible = store.Visible(context.OrganizationId, context.UserId);

        if (request.Ids is { } ids)
        {
            BulkRequests.RequireAtMost(ids.Count, 500, "/ids");
            BulkRequests.RequireDistinct(ids);

            var found = visible.Where(row => ids.Contains(row.Id)).Select(row => row.Id).ToHashSet();

            // A named widget that is missing, or that the caller may not see, is the same 404.
            var missing = ids
                .Select((id, index) => (id, index))
                .Where(item => !found.Contains(item.id))
                .Select(item => BulkFailure.At(new NotFoundException("widget", item.id), $"/ids/{item.index}"))
                .ToList();

            if (missing.Count > 0)
            {
                throw new BulkRefusedException(missing);
            }

            return BulkResult.Affected(await store.DeleteAsync(found, cancellationToken));
        }

        // A filter describes a set: whatever it matches within the caller's reach, and nothing else.
        var matched = visible.Matching(request.Filter!).Select(row => row.Id).ToHashSet();

        return BulkResult.Affected(await store.DeleteAsync(matched, cancellationToken));
    }
}
```

Naming a widget that does not exist, or that the caller may not see, refuses the request with a
`404` error pointing at the identifier:

```http
POST /api/v1/widgets:bulk-delete
Content-Type: application/json

{"ids":["62bed180-1f78-45d4-8a56-c996936a2947","7c1d9a3e-0000-4000-8000-000000000000"]}
```

```http
HTTP/1.1 422 Unprocessable Entity
Content-Type: application/json; charset=utf-8

{"errors":[{"status":"404","code":"not-found","title":"The resource does not exist.","detail":"No widget with ID '7c1d9a3e-0000-4000-8000-000000000000' exists.","source":{"pointer":"/ids/1"}}]}
```

A filter naming a field the resource does not mark `[Filterable]` never reaches the handler:

```http
POST /api/v1/widgets:bulk-delete
Content-Type: application/json

{"filter":{"==":[{"var":"notes"},"x"]}}
```

```http
HTTP/1.1 422 Unprocessable Entity
Content-Type: application/json; charset=utf-8

{"errors":[{"status":"422","code":"invalid-value","title":"A field has an invalid value.","detail":"'notes' is not a field this resource may be filtered on. The fields available are 'createdAt', 'name'.","source":{"pointer":"/filter"}}]}
```

Mark only fields a server can compare. An `[Encrypted]` member holds Protected Data, ciphertext
the server cannot read, so filtering on it would compare ciphertext and is never meaningful.

## What you decide

- **Which forms each operation accepts.** Set `Targeting` on the attribute. `ids` suits a caller
  that knows exactly which resources it means and wants each missing one reported; `filter` suits a
  set described by its properties.
- **The maximum,** for `ids` and `data`. Document it on the operation and enforce it with
  `RequireAtMost` before touching any resource. A `filter` has no item count; bound its cost by what
  the operation does with each match.
- **The caller's reach.** Restrict every query to the current organization and to what the caller
  may see, using `IContext` (`OrganizationId`, `UserId`, `ProviderId`). Report a hidden resource
  named by `ids` with the same `404` as a nonexistent one.

## See also

- [Bulk operations](./index.md)
- [All-or-nothing and best-effort](./all-or-nothing-and-best-effort.md), for how failures are
  reported
- [Bulk responses](./bulk-responses.md)
- The standard:
  [Targeting](https://contributing.bitwarden.com/architecture/server/apis/bulk-operations/targeting)
  and [Advanced queries](https://contributing.bitwarden.com/architecture/server/apis/advanced-queries)
