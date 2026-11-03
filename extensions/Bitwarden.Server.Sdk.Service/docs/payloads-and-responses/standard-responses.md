# Standard responses

Every API can answer the same set of failure statuses, so an API doesn't document them one by one,
and the standard says which status fits which failure. The package answers most of them itself,
answers the rest from the exceptions a handler throws, and builds each operation's success document
with the `StandardResponses` helpers. The standard:
[Standard responses](https://contributing.bitwarden.com/architecture/server/apis/standard-responses).

## What the package does

**Documentation.** An operation's OpenAPI entry lists its success response and one `default`
response, the error document, for every failure. Standard responses are never listed one by one,
as the standard asks.

**Success.** The status comes from the operation's attribute, applied when the result is written.
The helpers (`using static Bitwarden.Server.Sdk.Service.StandardResponses;`) build the document:

| Operation            | Return                                                           | Status                                      |
| -------------------- | ---------------------------------------------------------------- | ------------------------------------------- |
| `[StandardCreate]`   | `CreateResponse(resource)`                                       | `201`, with `Location` from the path and id |
| `[StandardReadOne]`  | `ReadOneResponse(resource)` or `(resource, fields)`              | `200`                                       |
| `[StandardReadMany]` | `ReadManyResponse(...)`: a list, an offset page or a cursor page | `200`, with paging `meta` and links         |
| `[StandardUpdate]`   | `UpdateResponse(resource)`                                       | `200`                                       |
| `[StandardAction]`   | `ActionResponse(resource)`, or your own value                    | `200`                                       |
| Any operation        | Nothing (`Task`)                                                 | `204`                                       |

Each helper takes an optional `meta`. A handler that returns an `IResult`, or sets the status
itself, keeps the status it chose.

**Failures.** Each is the standard error document (see [Errors](errors.md)):

| Status | When the package answers it                                                                                                                                                     |
| ------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `400`  | A resource document that isn't JSON (`malformed-json`); a body the client stopped sending; a value the framework couldn't bind for a reason it didn't name (`invalid-request`). |
| `401`  | No token, or one that isn't valid; `UnauthenticatedException`.                                                                                                                  |
| `403`  | The token lacks the scope, role or context the operation demands; `ForbiddenException`.                                                                                         |
| `404`  | `NotFoundException`; an address no route matches; an operation behind a feature flag that is off.                                                                               |
| `405`  | A route that exists, with a method it doesn't serve.                                                                                                                            |
| `406`  | Never on its own: `Accept` is not checked. See [Content negotiation](content-negotiation.md).                                                                                   |
| `409`  | `ConflictException`, which `Concurrency.Require` throws for a stale `version`.                                                                                                  |
| `413`  | A body over the server's size limit.                                                                                                                                            |
| `415`  | A body in a media type the operation doesn't read.                                                                                                                              |
| `422`  | Binding and validation failures; refused query parameters; `InvalidRequestException`; a `[SelfHostedOnly]` or `[NotSelfHostedOnly]` operation in the other kind of install.     |
| `429`  | Over a per-address rate limit. See [Network and request limits](../beyond-the-standards/network-and-request-limits.md).                                                         |
| `500`  | Any exception the package doesn't recognise. Nothing about it reaches the caller.                                                                                               |
| `501`  | `NotImplementedException`, and `PendingServiceException` for a rule that waits on a service that doesn't exist yet.                                                             |
| `503`  | `ServiceUnavailableException`, which the SDK's clients throw when a dependency is down or slow; the readiness probe when a health check fails.                                  |

Routing decides `404`, `405` and a resource document's `415` before authentication, so a caller
without a valid token is told `401` instead.

**Where the package differs from the standard.** A plain JSON body (an action's arguments, say)
that isn't valid JSON is answered `422`, with the serializer's message, rather than `400`. The
standard's `400` applies to resource documents only.

## Example

The handlers return the helpers' documents, and say `403`, `404` or `422` by throwing:

```csharp
using System.ComponentModel.DataAnnotations;
using Bitwarden.Server.Sdk.Common;
using Bitwarden.Server.Sdk.Service;
using Bitwarden.Server.Sdk.Service.Resources;
using static Bitwarden.Server.Sdk.Service.StandardResponses;

public sealed record Widget : Resource
{
    [StringLength(100)]
    public required string Name { get; init; }

    [Encrypted(MaxLength = 10_000)]
    public string? Notes { get; init; }

    /// <summary>
    /// The template the widget was made from, if any.
    /// </summary>
    public Guid? TemplateId { get; init; }
}

[ApiTag(Description = "Widgets, which a person keeps.")]
[Authorization(User = ContextRequirement.Required)]
public static class WidgetsEndpoints
{
    public const string Collection = "api/v1/widgets";

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var widgets = endpoints.MapGroup(Collection).WithStandards();
        widgets.MapPost("", CreateWidget);
        widgets.MapGet("{id}", GetWidget);
        widgets.MapDelete("{id}", DeleteWidget);
        widgets.MapPost("{id}/actions/polish", Polish);
    }

    /// <summary>
    /// Create a widget
    /// </summary>
    [StandardCreate]
    public static async Task<ResourceDocument<Widget>> CreateWidget(
        ResourceDocument<Widget> request, Person person, IWidgetStore store, CancellationToken cancellationToken)
    {
        // The path names the collection, which exists. A reference in the body that doesn't
        // resolve is the caller's to fix: 422, pointing at the field.
        if (request.Data.TemplateId is { } template && !await store.TemplateExistsAsync(template, cancellationToken))
        {
            throw new InvalidRequestException(
                $"'{template}' is not a widget template.", "/data/attributes/templateId", "resource-not-found");
        }

        // 201, with Location derived from the request path and the new widget's id.
        return CreateResponse(await store.AddAsync(person, request.Data, cancellationToken));
    }

    /// <summary>
    /// Get a widget
    /// </summary>
    [StandardReadOne]
    public static async Task<ResourceDocument<Widget>> GetWidget(
        string id, Person person, IWidgetStore store, CancellationToken cancellationToken) =>
        ReadOneResponse((await store.FindVisibleAsync(person, id, cancellationToken))?.Widget
            ?? throw new NotFoundException("widget", id));

    /// <summary>
    /// Delete a widget
    /// </summary>
    [StandardDelete]
    public static async Task DeleteWidget(
        string id, Person person, IWidgetStore store, CancellationToken cancellationToken)
    {
        await RequireOwner(person, id, store, cancellationToken);

        // Returns nothing: 204.
        await store.DeleteAsync(id, cancellationToken);
    }

    /// <summary>
    /// Polish a widget
    /// </summary>
    [StandardAction]
    public static async Task<ResourceDocument<Widget>> Polish(
        string id, Person person, IWidgetStore store, CancellationToken cancellationToken)
    {
        await RequireOwner(person, id, store, cancellationToken);
        return ActionResponse(await store.PolishAsync(id, cancellationToken));
    }

    private static async Task RequireOwner(Person person, string id, IWidgetStore store, CancellationToken cancellationToken)
    {
        // A widget nobody shared with the person is one they may not know exists: 404.
        var found = await store.FindVisibleAsync(person, id, cancellationToken)
            ?? throw new NotFoundException("widget", id);

        // One shared with them they know about, but only its owner may change it: 403.
        if (found.OwnerId != person.Id)
        {
            throw new ForbiddenException("Only the widget's owner can change it.");
        }
    }
}

/// <summary>
/// A widget the person can see, and whose it is.
/// </summary>
public sealed record VisibleWidget(Widget Widget, Guid OwnerId);
```

A widget shared with the person, which they don't own:

```http
HTTP/1.1 403 Forbidden
Content-Type: application/json; charset=utf-8

{"errors":[{"status":"403","code":"forbidden","title":"The caller is not permitted to do this.","detail":"Only the widget's owner can change it."}]}
```

A widget nobody shared with them, which they are not told exists:

```http
HTTP/1.1 404 Not Found
Content-Type: application/json; charset=utf-8

{"errors":[{"status":"404","code":"not-found","title":"The resource does not exist.","detail":"No widget with ID 'd2c9a4c0-0000-4000-8000-000000000001' exists."}]}
```

## What you decide

- **`403` or `404`.** The package can't know who may know a resource exists. Throw
  `NotFoundException` when the caller may not know, and `ForbiddenException` when they may know but
  not act, as `RequireOwner` does.
- **`404` or `422`.** A resource named in the path that isn't there is `NotFoundException`. One
  named in the body that isn't there is `InvalidRequestException`, with a pointer to the field.
- **`503` for your own dependencies.** The SDK's clients throw `ServiceUnavailableException`. For a
  database or another library, translate its timeout with an `IExceptionTranslator` (see
  [Errors](errors.md)), or it is a `500`.
- **`501` for a stub.** Throw `NotImplementedException` from an operation declared but not built.

## See also

- [Errors](errors.md): the error document, and every exception that maps to a status.
- [Request validation](request-validation.md): what is refused `400` and `422`.
- [Verbs](../operations/verbs.md): the standard attributes and their success statuses.
- [Paging](../querying/paging.md): `ReadManyResponse` for a page.
- The standard:
  [Standard responses](https://contributing.bitwarden.com/architecture/server/apis/standard-responses).
