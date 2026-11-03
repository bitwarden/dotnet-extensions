# Bulk deletes

A bulk delete is the equivalent of deleting each targeted resource, named by `ids` or described by
a `filter`. On a service with soft deletes it soft-deletes by default and removes resources for good
when the caller passes `permanent=true`. The package reads and checks the targeting; the delete,
and `permanent`, are your handler's. The standard:
[Bulk deletes](https://contributing.bitwarden.com/architecture/server/apis/bulk-operations/bulk-deletes).

## What the package does

- **`[StandardBulkDelete(collection)]`** maps `POST {collection}:bulk-delete`. It is a `POST`
  because the targeting travels in the body. `Targeting` sets which forms it accepts (`Either` by
  default) and `Supports` the atomicity modes (all-or-nothing by default).
- **`BulkDelete<T>`** carries `Ids` or `Filter`, exactly one, checked as described in
  [Targeting](./targeting.md). A filter can't be empty, so a bulk delete never means "everything
  in scope" by omission.
- **`permanent`** is not part of the package. Declare it as a query parameter of the handler, and
  it is bound and published in the OpenAPI document like any other.
- **Not checked by the package:** the caller's reach, the single delete's rules and side effects.

## Example

This bulk delete takes a `filter`, keeps it within the caller's organization, and soft-deletes by
default:

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
    /// Describes the widgets with a `filter`. Soft-deletes them, or removes them for good with
    /// `permanent=true`. All-or-nothing. Responds synchronously.
    /// </remarks>
    [StandardBulkDelete(Collection, Targeting = Targeting.Filter)]
    public static async Task<BulkResult> DeleteWidgets(
        BulkDelete<Widget> request,
        IContext context,
        IWidgetStore store,
        TimeProvider time,
        bool permanent = false,
        CancellationToken cancellationToken = default)
    {
        var matched = store.Visible(context.OrganizationId).Matching(request.Filter!);

        var affected = permanent
            ? await store.RemoveAsync(matched, cancellationToken)
            : await store.MarkDeletedAsync(matched, time.GetUtcNow(), cancellationToken);

        return BulkResult.Affected(affected);
    }
}
```

The standard's own example, deleting unverified accounts created before a date, is a filter of
this kind. For widgets:

```http
POST /api/v1/widgets:bulk-delete
Content-Type: application/json

{"filter":{"==":[{"var":"name"},"Gear"]}}
```

```http
HTTP/1.1 200 OK
Content-Type: application/json; charset=utf-8

{"meta":{"affectedCount":1}}
```

`ids` is refused here, because the attribute accepts only a filter:

```http
HTTP/1.1 422 Unprocessable Entity
Content-Type: application/json; charset=utf-8

{"errors":[{"status":"422","code":"invalid-value","title":"A field has an invalid value.","detail":"This operation does not accept 'ids'. Describe the resources with 'filter'.","source":{"pointer":"/ids"}}]}
```

## What you decide

- **Soft or hard.** Follow the single delete: if it soft-deletes, so does the bulk delete, and
  `permanent=true` removes for good.
- **Targeting.** `ids` when each missing resource should be reported back as a `404`; `filter` when
  the caller describes a set. See [Targeting](./targeting.md) for an operation accepting both.
- **Side effects.** Each deleted resource produces what the single delete would (events, audit
  records, cleanup of dependent data). A set-based delete must still produce them.

## See also

- [Bulk operations](./index.md)
- [Targeting](./targeting.md)
- [All-or-nothing and best-effort](./all-or-nothing-and-best-effort.md)
- The standard:
  [Bulk deletes](https://contributing.bitwarden.com/architecture/server/apis/bulk-operations/bulk-deletes)
  and [Deleting resources](https://contributing.bitwarden.com/architecture/server/apis/deleting-resources)
