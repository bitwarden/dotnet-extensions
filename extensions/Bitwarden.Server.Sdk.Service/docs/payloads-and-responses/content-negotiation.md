# Content negotiation

A service answers in the media type the caller's `Accept` header asks for, or refuses `406`, and
refuses `415` a body in a media type it can't read. The package checks the request's `Content-Type`
for you; it does not check `Accept`, so a service that must refuse `406` adds the check. The
standard:
[Content negotiation](https://contributing.bitwarden.com/architecture/server/apis/content-negotiation).

## What the package does

| The standard says                                                    | The package                                                                                                                                                                         |
| -------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Services `MUST` respect `Accept`, and answer `406` when they can't   | Not done. `Accept` is ignored and every answer is `application/json; charset=utf-8`. A `406` that something else sets is written as the standard error document (`not-acceptable`). |
| Services `MUST` answer `415` for a `Content-Type` they can't process | Done, for every kind of body below, with the standard error document (`unsupported-media-type`).                                                                                    |

What each kind of body accepts:

| The handler takes                           | Accepted `Content-Type`                                                  | Otherwise                                                                                   |
| ------------------------------------------- | ------------------------------------------------------------------------ | ------------------------------------------------------------------------------------------- |
| A resource document (`ResourceDocument<T>`) | `application/json`, or a `+json` type such as `application/vnd.api+json` | `415`, decided by routing, so a caller without a valid token is told `401` first.           |
| A plain JSON body                           | `application/json`, `text/json`, or any `application/*+json`             | `415`, after authorization. An optional body may be left out with no `Content-Type` at all. |
| `HttpRequest` with `[Consumes("...")]`      | The media types `[Consumes]` names                                       | `415`, decided by routing.                                                                  |

A refusal looks like this:

```http
HTTP/1.1 415 Unsupported Media Type
Content-Type: application/json; charset=utf-8

{"errors":[{"status":"415","code":"unsupported-media-type","title":"The request body's media type is not supported.","detail":"The request body must be sent as application/json."}]}
```

## Example

### Refuse what you can't answer with `406`

An `IStandardEndpointFilterProvider` adds a filter to every standard endpoint. This one lets a
request through when it has no `Accept` header, or one that admits JSON, and otherwise answers a
bare `406`, which the platform writes as the standard error document:

```csharp
using System.Reflection;
using Bitwarden.Server.Sdk.Service.Endpoints;
using Microsoft.Net.Http.Headers;

/// <summary>
/// Refuses a standard endpoint's request with 406 when its Accept header admits no JSON.
/// </summary>
public sealed class JsonOnlyResponses : IStandardEndpointFilterProvider
{
    private static readonly IEndpointFilter Filter = new AcceptCheck();

    public IEndpointFilter? For(MethodInfo method, ICollection<string> problems) => Filter;

    private sealed class AcceptCheck : IEndpointFilter
    {
        private static readonly MediaTypeHeaderValue Json = new("application/json");

        public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
        {
            var accepted = context.HttpContext.Request.GetTypedHeaders().Accept;

            // No Accept header accepts anything. Otherwise one range must admit JSON.
            if (accepted.Count == 0 || accepted.Any(range => range.Quality != 0 && Json.IsSubsetOf(range)))
            {
                return next(context);
            }

            // A bare status: the platform writes the standard error document for it.
            return ValueTask.FromResult<object?>(Results.StatusCode(StatusCodes.Status406NotAcceptable));
        }
    }
}
```

Register it in `ConfigureServices`:

```csharp
using Bitwarden.Server.Sdk.Service;
using Bitwarden.Server.Sdk.Service.Endpoints;

internal sealed class WidgetService(string[] args) : Service("widget-service", "Widget Service", args)
{
    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IWidgetStore, WidgetStore>();
        services.AddSingleton<IStandardEndpointFilterProvider, JsonOnlyResponses>();
    }

    protected override void MapEndpoints(IEndpointRouteBuilder endpoints) =>
        WidgetsEndpoints.Map(endpoints);
}
```

A caller that asks for XML now gets:

```http
HTTP/1.1 406 Not Acceptable
Content-Type: application/json; charset=utf-8

{"errors":[{"status":"406","code":"not-acceptable","title":"No acceptable representation is available.","detail":"This API produces application/json only."}]}
```

The filter runs after the platform's own checks, so a request that is also invalid is answered
`422`, not `406`. It covers standard endpoints only: the health probes, the OpenAPI document and the
documentation UI are left alone.

### Take a body that isn't JSON

A raw upload takes `HttpRequest` and names its media type with `[Consumes]`
(`Microsoft.AspNetCore.Mvc`). Here the body is an attachment the person encrypted on their device,
stored without being read:

```csharp
using Microsoft.AspNetCore.Mvc;

/// <summary>
/// Upload a widget's attachment
/// </summary>
/// <param name="id">The widget.</param>
/// <param name="request">The request, whose body is the attachment, encrypted on the person's device.</param>
/// <param name="store">Where attachments are kept.</param>
/// <param name="cancellationToken">Cancels the upload.</param>
[Consumes("application/octet-stream")]
public static Task UploadAttachment(
    string id, HttpRequest request, IWidgetStore store, CancellationToken cancellationToken) =>
    // Stored as sent: the service never reads the ciphertext.
    store.SaveAttachmentAsync(id, request.Body, cancellationToken);
```

It is mapped like any other handler, `widgets.MapPut("{id}/attachment", UploadAttachment)`. A body
sent as anything but `application/octet-stream` is refused `415`.

## What you decide

- **Whether to check `Accept`.** The package answers JSON whatever is asked. To meet the standard's
  `406`, register a filter like `JsonOnlyResponses`.
- **Media types of raw bodies.** Name them with `[Consumes]`; the package can't know them.

## See also

- [Standard responses](standard-responses.md): every status the package answers with.
- [Errors](errors.md): the error document.
- [Caching and extensions](../beyond-the-standards/caching-and-extensions.md):
  `IStandardEndpointFilterProvider`.
- The standard:
  [Content negotiation](https://contributing.bitwarden.com/architecture/server/apis/content-negotiation).
