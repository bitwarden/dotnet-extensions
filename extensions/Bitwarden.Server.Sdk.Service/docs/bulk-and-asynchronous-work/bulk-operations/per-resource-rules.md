# Per-resource rules

Targeting decides which resources a bulk request reaches; per-resource rules decide what it may do
to each. The standard holds every targeted resource to the single-resource API's authorization,
validation and business rules, and requires the same side effects for each resource that changes.
The package checks the request as a whole; applying these rules to each resource is your handler's
job. The standard:
[Per-resource rules](https://contributing.bitwarden.com/architecture/server/apis/bulk-operations/per-resource-rules).

## What the package does

| The standard's rule                                                        | The package                                                                                                                                                                                                 |
| -------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Each resource held to the single-resource API's rules (`MUST`)             | Not enforced. The operation's own authorization (its scope and caller context, settled by the attribute) is checked once for the request. Each resource's rules are yours.                                  |
| Each change produces the same side effects (`MUST`)                        | Not enforced. Raise the same events, notifications and audit records as the single-resource API.                                                                                                            |
| The outcome is indistinguishable from one resource at a time (`MAY` batch) | `BulkFailure.At(exception, pointer)` describes a failed resource with the status, code and detail the single-resource API's exception would have produced, using the same mapping as the exception handler. |

## Example

Put the rules in one place that both the single-resource action and its bulk form call. Here the
endpoints are thin:

```csharp
using Bitwarden.Server.Sdk.Common;
using Bitwarden.Server.Sdk.Service;
using Bitwarden.Server.Sdk.Service.Querying;

[ApiTag(Description = "Widgets, which a person keeps.")]
public static class WidgetsEndpoints
{
    public const string Collection = "api/v1/widgets";

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var widgets = endpoints.MapGroup(Collection).WithStandards();
        widgets.MapPost("{id}/actions/archive", ArchiveWidget);

        endpoints.MapPost($"{Collection}:bulk-archive", ArchiveWidgets).WithStandards();
    }

    /// <summary>
    /// Archive a widget
    /// </summary>
    [StandardAction("archive")]
    public static Task ArchiveWidget(
        string id, IContext context, WidgetArchiving archiving, CancellationToken cancellationToken) =>
        archiving.ArchiveAsync(context, id, cancellationToken);

    /// <summary>
    /// Archive widgets
    /// </summary>
    [StandardBulkAction(Collection, "archive", Targeting = Targeting.Ids)]
    public static async Task<BulkResult> ArchiveWidgets(
        BulkAction<Widget> request, IContext context, WidgetArchiving archiving, CancellationToken cancellationToken)
    {
        var ids = request.Ids ?? [];
        BulkRequests.RequireAtMost(ids.Count, 100, "/ids");
        BulkRequests.RequireDistinct(ids);

        return await archiving.ArchiveAllAsync(context, ids, cancellationToken);
    }
}
```

and `WidgetArchiving` applies the same check, the same change and the same event whether it is
given one widget or many:

```csharp
/// <summary>
/// Archives widgets, one or many, under one set of rules.
/// </summary>
public sealed class WidgetArchiving(IWidgetStore store, IWidgetEvents events)
{
    public async Task ArchiveAsync(IContext context, string id, CancellationToken cancellationToken)
    {
        var widget = await store.FindVisibleAsync(context, id, cancellationToken)
            ?? throw new NotFoundException("widget", id);

        Check(widget);
        widget.Archived = true;
        await store.SaveAsync([widget], cancellationToken);
        await events.ArchivedAsync(widget.Id, cancellationToken);
    }

    public async Task<BulkResult> ArchiveAllAsync(
        IContext context, IReadOnlyList<string> ids, CancellationToken cancellationToken)
    {
        var failures = new List<Bitwarden.Server.Sdk.Common.Errors.Error>();
        var archived = new List<WidgetRow>();

        for (var index = 0; index < ids.Count; index++)
        {
            var widget = await store.FindVisibleAsync(context, ids[index], cancellationToken);

            try
            {
                // The same rules, in the same order, as the single-resource action.
                Check(widget ?? throw new NotFoundException("widget", ids[index]));
                widget.Archived = true;
                archived.Add(widget);
            }
            catch (Exception failure) when (failure is NotFoundException or InvalidRequestException)
            {
                failures.Add(BulkFailure.At(failure, $"/ids/{index}"));
            }
        }

        // All-or-nothing: report every failure, change nothing.
        if (failures.Count > 0)
        {
            throw new BulkRefusedException(failures);
        }

        await store.SaveAsync(archived, cancellationToken);

        // The same side effect for each widget that changed.
        foreach (var widget in archived)
        {
            await events.ArchivedAsync(widget.Id, cancellationToken);
        }

        return BulkResult.Affected(archived.Count);
    }

    private static void Check(WidgetRow widget)
    {
        if (widget.Archived)
        {
            throw new InvalidRequestException("The widget is already archived.", code: "already-archived");
        }
    }
}
```

Archiving a widget that is already archived fails as it would one at a time, against its position
in the request:

```http
HTTP/1.1 422 Unprocessable Entity
Content-Type: application/json; charset=utf-8

{"errors":[{"status":"422","code":"already-archived","title":"A field has an invalid value.","detail":"The widget is already archived.","source":{"pointer":"/ids/1"}}]}
```

## What you decide

- **How to implement it.** A single set-based statement is allowed, provided the outcome, including
  which resources fail and why, matches applying the rules one resource at a time. Check the rules
  against the loaded rows first, then write the changes together.
- **Rules that depend on state,** such as which transitions are allowed, are checked against each
  resource as it is. For a bulk update, check it both as it is and as it would be after the change.
- **Side effects at scale.** One event per changed resource is what the single-resource API would
  produce. Where that is costly, consider whether the operation should be smaller.

## See also

- [Bulk operations](./index.md)
- [Targeting](./targeting.md)
- [All-or-nothing and best-effort](./all-or-nothing-and-best-effort.md)
- [Bulk actions](./bulk-actions.md)
- The standard:
  [Per-resource rules](https://contributing.bitwarden.com/architecture/server/apis/bulk-operations/per-resource-rules)
