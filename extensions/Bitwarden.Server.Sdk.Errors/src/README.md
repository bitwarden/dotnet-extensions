# Bitwarden.Server.Sdk.Errors

One error document and one set of failures that every Bitwarden service and every caller of it agree on.

A Bitwarden service answers every unsuccessful request with the same body, whatever the status, and a caller
branches on a stable code in it rather than on prose. This package holds that body, the codes, and the exceptions a
service throws to produce them. It has no dependencies at all, so a program that only calls a service, or a library
that only throws, references it without taking on ASP.NET Core or the packages that host a service.

- **One error shape.** `ErrorResponse` holds one or more `Error`s, each with a status, a stable `code`, a `title`, a
  `detail`, and a `source` naming the field, query parameter or header at fault.
- **Codes a caller can rely on.** `ErrorCodes` are contract. `ErrorTitles` and `ErrorDetails` are the text that goes
  with them, and may be reworded.
- **Standard failures.** `NotFoundException`, `ConflictException`, `InvalidRequestException`, `ForbiddenException`,
  `UnauthenticatedException`, `PendingServiceException` and `ServiceUnavailableException` each mean one status.
- **Several problems at once.** An exception implementing `IProvideErrors` says exactly what the response holds.
- **Other libraries' failures.** An `IExceptionTranslator` says which standard failure a library's own exception
  means.

## What belongs here

Only what both a service and its callers need. The test is whether removing a type would force a caller to reference
the code that hosts the service. Nothing here touches `HttpContext`, MVC or `HttpClient`: turning an exception into a
response is a service host's job, and reading a response is the caller's.

## Getting started

A caller reads an error document with nothing but this package and `System.Net.Http.Json`:

```bash
dotnet new console -n WidgetCaller
cd WidgetCaller
dotnet add package Bitwarden.Server.Sdk.Errors --prerelease
```

```csharp
using System.Net.Http.Json;
using Bitwarden.Server.Sdk.Errors;

using var http = new HttpClient { BaseAddress = new Uri(args.Length > 0 ? args[0] : "http://localhost:5000/") };
using var response = await http.GetAsync("api/v1/widgets/9f3c1a52-6f0e-4d3b-8a51-2c7d9e4b1f60");

if (response.IsSuccessStatusCode)
{
    Console.WriteLine("Found it.");
    return;
}

var document = await response.Content.ReadFromJsonAsync<ErrorResponse>();
foreach (var error in document?.Errors ?? [])
{
    // Branch on the code. The title and the detail are prose, and may change.
    var what = error.Code switch
    {
        ErrorCodes.NotFound => "There is no such widget.",
        ErrorCodes.Unauthenticated => "Sign in first.",
        _ => error.Detail,
    };

    Console.WriteLine($"{error.Status} {error.Code}: {what}");
}
```

Run it against a Bitwarden service with `dotnet run -- https://widgets.example.com/`. When the service requires a token
and gets none, it answers `401` with an error document, and the program prints `401 unauthenticated: Sign in first.`

## Examples

### Report a failure the standard way

Throw one of the standard exceptions. A service host maps each to its status and answers with an error document:

```csharp
using Bitwarden.Server.Sdk.Errors;

public sealed class WidgetRules(IWidgetStore store)
{
    public async Task<Widget> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var widget = await store.FindAsync(id, cancellationToken)
            ?? throw new NotFoundException("widget", id.ToString());

        if (widget.IsLocked)
        {
            throw new ConflictException("The widget is locked.");
        }

        return widget;
    }

    public static void CheckName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidRequestException(
                "A name is required.", pointer: "/data/attributes/name", code: ErrorCodes.FieldRequired);
        }
    }
}
```

| Exception                     | Status                                               | Code                                            |
| ----------------------------- | ---------------------------------------------------- | ----------------------------------------------- |
| `InvalidRequestException`     | `422`                                                | The one it was given, `invalid-value` when none |
| `UnauthenticatedException`    | `401`                                                | `unauthenticated`                               |
| `ForbiddenException`          | `403`                                                | `forbidden`                                     |
| `NotFoundException`           | `404`                                                | `not-found`                                     |
| `ConflictException`           | `409`                                                | `conflict`                                      |
| `PendingServiceException`     | `501`                                                | `not-implemented`                               |
| `ServiceUnavailableException` | `503`                                                | `service-unavailable`                           |
| `IProvideErrors`              | Its own `StatusCode`, `422` unless it says otherwise | The codes of its errors                         |

- `NotFoundException` is also the answer when the caller may not know a resource exists, so existence never shows in
  the difference between `403` and `404`.
- `ServiceUnavailableException.Unreachable`, `TimedOut` and `Answered` name the service depended on and what
  happened, never its address. A dependency that answered `500` has a bug of its own: don't turn that into a `503`.
- `PendingServiceException` refuses a rule the service can't enforce yet, rather than letting the request through
  unchecked.

### Report several problems at once

An exception implementing `IProvideErrors` decides the whole response, so a check over a list reports every bad
entry, not only the first:

```csharp
using Bitwarden.Server.Sdk.Errors;

public sealed class InvalidWidgetsException(IReadOnlyList<int> positions) : Exception("Some widgets are invalid."), IProvideErrors
{
    public IReadOnlyList<Error> Errors { get; } =
    [
        .. positions.Select(position => new Error
        {
            Status = "422",
            Code = ErrorCodes.FieldRequired,
            Title = ErrorTitles.FieldRequired,
            Detail = ErrorDetails.FieldRequired("name"),
            Source = new ErrorSource { Pointer = $"/data/{position}/attributes/name" },
        }),
    ];
}
```

### Translate another library's exception

Register an `IExceptionTranslator` so a host answers a library's exception as the standard failure it means. A host asks
each translator in turn and the first to return one wins; return null for anything you don't recognize:

```csharp
using Bitwarden.Server.Sdk.Errors;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

public sealed class WidgetLockTranslator : IExceptionTranslator
{
    public Exception? Translate(Exception exception) =>
        exception is WidgetLockTimeoutException ? new ConflictException("The widget is being changed. Try again.") : null;
}

public static class WidgetLockTranslatorRegistration
{
    public static IServiceCollection AddWidgetLockTranslator(this IServiceCollection services)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IExceptionTranslator, WidgetLockTranslator>());
        return services;
    }
}
```

## Configuration

The package reads no settings.

## Requirements

- .NET 10.
- No other packages.

## Contributing

Every source file with logic has a matching test class in
[`tests/Bitwarden.Server.Sdk.Errors.Tests`](../tests/Bitwarden.Server.Sdk.Errors.Tests). Run them from the repository
root:

```bash
dotnet test --project extensions/Bitwarden.Server.Sdk.Errors/tests/Bitwarden.Server.Sdk.Errors.Tests
```

A change must keep:

- **Codes stable.** A value in `ErrorCodes` never changes: callers branch on it. Add a code; never rename one.
- **No dependencies.** A caller must be able to reference the package without anything else coming with it.
- **Nothing about the transport.** No `HttpContext`, MVC or `HttpClient`. A service host turns these types into
  responses.
- **Nothing leaked in a message.** A detail tells the caller what they can act on: never an address, a host name, an
  exception's text, or whether a resource they may not see exists.
