# Errors

Every failure is a top-level `errors` array of error objects, each with a stable `code`, a `status`,
a `title`, a `detail` and, where one field is to blame, a `source`; a `500` says nothing about what
went wrong. The package writes that document for every failure, its own and the exceptions a handler
throws, so a handler never builds one. The standard:
[Errors](https://contributing.bitwarden.com/architecture/server/apis/errors).

## What the package does

The document is `ErrorResponse`, a list of `Error` (both in `Bitwarden.Server.Sdk.Common.Errors`).
Members without a value are left out.

| Field              | What the package puts there                                                                                                                         |
| ------------------ | --------------------------------------------------------------------------------------------------------------------------------------------------- |
| `status`           | The response's status, as a string.                                                                                                                 |
| `code`             | A stable code from `ErrorCodes`, such as `not-found`, `invalid-value`, `field-required`, `invalid-type`, `conflict`; or the one an exception gives. |
| `title`            | The same text for every error with that code (`ErrorTitles`).                                                                                       |
| `detail`           | What went wrong this time. For an exception the package recognises, its message.                                                                    |
| `source.pointer`   | A JSON Pointer into the body, on the wire's names: `/data/attributes/name`, `/data/attributes/part/code`, `/shine`.                                 |
| `source.parameter` | The query parameter, such as `filter[createdAt]` or `page[number]`.                                                                                 |
| `source.header`    | The caller-context header that was refused. Any other header parameter is reported with a pointer instead, `/{header-name}`.                        |
| `meta.resource`    | The `type` and `id` of a resource a bulk operation couldn't act on (`FailedResource`), when there's nothing in the request to point at.             |
| `id`               | On a `500`, `501` or `503` an exception produced: the request's trace identifier, which the log records with the exception. Other errors have none. |

A `500` carries a fixed title and detail and no `source`. The exception is logged at `Error` with
the same identifier the caller sees; nothing of it reaches the caller.

```http
HTTP/1.1 500 Internal Server Error
Content-Type: application/json; charset=utf-8

{"errors":[{"id":"0HNPPMRQG8EGA:00000001","status":"500","code":"internal-error","title":"The request could not be completed.","detail":"An unexpected error occurred. Quote the error id when reporting this."}]}
```

A handler throws one of these (`Bitwarden.Server.Sdk.Common`) and the exception handler answers:

| Throw                                             | Status                              | Code                       | What the caller is told                                       |
| ------------------------------------------------- | ----------------------------------- | -------------------------- | ------------------------------------------------------------- |
| `NotFoundException()`, `(type, id)` or `(detail)` | `404`                               | `not-found`                | The detail, such as "No widget with ID '...' exists."         |
| `ForbiddenException(detail)`                      | `403`                               | `forbidden`                | The detail.                                                   |
| `UnauthenticatedException(detail)`                | `401`                               | `unauthenticated`          | The detail.                                                   |
| `ConflictException()` or `(detail)`               | `409`                               | `conflict`                 | The detail, or that the resource changed since it was read.   |
| `InvalidRequestException(detail, pointer, code)`  | `422`                               | `code`, or `invalid-value` | The detail, at `source.pointer` when one is given.            |
| `PendingServiceException(service, requirement)`   | `501`                               | `not-implemented`          | Which service the rule waits on, and what it needs.           |
| `NotImplementedException`                         | `501`                               | `not-implemented`          | A fixed detail.                                               |
| `ServiceUnavailableException`                     | `503`                               | `service-unavailable`      | Which dependency, and what happened; never how it is reached. |
| An exception implementing `IProvideErrors`        | Its `StatusCode` (`422` by default) | Its own                    | Its `Errors`, as many as it has.                              |
| Anything else                                     | `500`                               | `internal-error`           | Nothing.                                                      |

`IExceptionTranslator` (also in `Bitwarden.Server.Sdk.Common`) lets a package say what a library's
own exception means: the first registered translator to return an exception wins, and the answer is
as if that one had been thrown. The log keeps the original.

## Example

Most handlers only throw. `NotFoundException("widget", id)` gives the standard detail:

```csharp
using Bitwarden.Server.Sdk.Common;
using Bitwarden.Server.Sdk.Service;
using Bitwarden.Server.Sdk.Service.Resources;
using static Bitwarden.Server.Sdk.Service.StandardResponses;

/// <summary>
/// Get a widget
/// </summary>
[StandardReadOne]
public static async Task<ResourceDocument<Widget>> GetWidget(
    string id, IWidgetStore store, CancellationToken cancellationToken) =>
    // 404, code not-found, detail "No widget with ID '...' exists."
    ReadOneResponse(await store.FindAsync(id, cancellationToken)
        ?? throw new NotFoundException("widget", id));
```

```http
HTTP/1.1 404 Not Found
Content-Type: application/json; charset=utf-8

{"errors":[{"status":"404","code":"not-found","title":"The resource does not exist.","detail":"No widget with ID 'd2c9a4c0-0000-4000-8000-000000000001' exists."}]}
```

### Report several errors at once

A rule that can fail in several places reports every failure with an exception that implements
`IProvideErrors`:

```csharp
using Bitwarden.Server.Sdk.Common;
using Bitwarden.Server.Sdk.Common.Errors;

/// <summary>
/// Parts a widget names that don't exist: one error for each.
/// </summary>
public sealed class UnknownPartsException(IReadOnlyList<Guid> sent, IReadOnlySet<Guid> missing)
    : Exception("A widget names parts that don't exist."), IProvideErrors
{
    // 422 unless a status is given.
    public IReadOnlyList<Error> Errors { get; } =
    [
        .. sent.Select((part, index) => (part, index))
            .Where(item => missing.Contains(item.part))
            .Select(item => new Error
            {
                Status = "422",
                Code = "resource-not-found",
                Title = "Referenced resource does not exist",
                Detail = $"'{item.part}' is not a widget part.",
                Source = new ErrorSource { Pointer = $"/data/attributes/partIds/{item.index}" },
            }),
    ];
}
```

The create handler checks every part the widget names, then throws once:

```csharp
/// <summary>
/// Create a widget
/// </summary>
[StandardCreate]
public static async Task<ResourceDocument<Widget>> CreateWidget(
    ResourceDocument<Widget> request, IWidgetStore store, CancellationToken cancellationToken)
{
    // Every part that doesn't exist is reported, not only the first.
    var missing = await store.MissingPartsAsync(request.Data.PartIds ?? [], cancellationToken);
    if (missing.Count > 0)
    {
        throw new UnknownPartsException(request.Data.PartIds ?? [], missing);
    }

    return CreateResponse(await store.AddAsync(request.Data, cancellationToken));
}
```

```json
{
    "errors": [
        {
            "status": "422",
            "code": "resource-not-found",
            "title": "Referenced resource does not exist",
            "detail": "'0b6d1a7e-5f2c-4c8a-9d3e-1f2a3b4c5d6e' is not a widget part.",
            "source": { "pointer": "/data/attributes/partIds/0" }
        },
        {
            "status": "422",
            "code": "resource-not-found",
            "title": "Referenced resource does not exist",
            "detail": "'0e3d2eb3-755c-41cd-86f0-0e0649043ef6' is not a widget part.",
            "source": { "pointer": "/data/attributes/partIds/2" }
        }
    ]
}
```

### Translate a library's exceptions

Register an `IExceptionTranslator` in `ConfigureServices`, and the library's exceptions are
answered as the platform's:

```csharp
using Bitwarden.Server.Sdk.Common;
using Bitwarden.Server.Sdk.Service;

/// <summary>
/// Says what the widget store library's own exceptions mean.
/// </summary>
public sealed class WidgetStoreExceptionTranslator : IExceptionTranslator
{
    public Exception? Translate(Exception exception) => exception switch
    {
        // Another write got there first: 409.
        WidgetStoreWriteConflictException => new ConflictException(),

        // The store didn't answer: 503, naming it and nothing of how it is reached.
        WidgetStoreTimeoutException timeout => ServiceUnavailableException.TimedOut("widget-store", timeout.Waited),

        // Not ours to say: the next translator, or a 500.
        _ => null,
    };
}

internal sealed class WidgetService(string[] args) : Service("widget-service", "Widget Service", args)
{
    protected override void ConfigureServices(IServiceCollection services) =>
        services.AddSingleton<IExceptionTranslator, WidgetStoreExceptionTranslator>();
}
```

A timeout in the store now reaches the caller as:

```http
HTTP/1.1 503 Service Unavailable
Content-Type: application/json; charset=utf-8

{"errors":[{"id":"0HNPPMUMBT4AM:00000001","status":"503","code":"service-unavailable","title":"A service this depends on is unavailable.","detail":"The widget-store service did not answer within 5 seconds."}]}
```

## What you decide

- **Codes.** Use an `ErrorCodes` constant where one fits. A code of your own is part of your
  contract: callers branch on it, so never change it. Titles and details may be reworded.
- **Details.** Write them for the caller, and never put Vault Data, ciphertext, a secret or an
  internal address in one. The detail of a recognised exception is sent as written.
- **`403` or `404`.** See [Standard responses](standard-responses.md#what-you-decide).
- **An `id` on other errors.** The package sets one only on server faults. An `IProvideErrors`
  exception may set `Id` on the errors it returns.
- **A header parameter's errors.** They point at `/{header-name}` rather than naming the header in
  `source.header`. Validate a header in the handler and throw `IProvideErrors` with
  `Source = new ErrorSource { Header = "..." }` if a caller needs it named.

## See also

- [Standard responses](standard-responses.md): which status fits which failure.
- [Request validation](request-validation.md): the errors binding and validation produce.
- [Bulk responses](../bulk-and-asynchronous-work/bulk-operations/bulk-responses.md): errors inside a
  best-effort `200`, and `meta.resource`.
- The standard: [Errors](https://contributing.bitwarden.com/architecture/server/apis/errors).
