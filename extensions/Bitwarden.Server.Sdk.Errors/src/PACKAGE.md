# Bitwarden.Server.Sdk.Errors

## About

The error document every Bitwarden service answers an unsuccessful request with, the stable codes a caller branches
on, and the exceptions a service throws to produce them: "not found", "conflict", "invalid request" and the rest.

It has no dependencies, so a program that only calls a service, or a library that only throws, doesn't depend on the
packages that host a service.

## Installing

```bash
dotnet add package Bitwarden.Server.Sdk.Errors
```

## Usage

### Refuse a request

Throw a standard exception. A service host maps it to its status and answers with an error document:

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
  the difference between `403` and `404`. A detail you pass it must not disclose anything the caller may not know.
- `ServiceUnavailableException.Unreachable`, `TimedOut` and `Answered` name the service depended on and what
  happened, never its address, host name or exception text. A dependency that answered `500` has a bug of its own:
  don't turn that into a `503`.
- `PendingServiceException` refuses a rule the service can't enforce yet, rather than letting the request through
  unchecked. Its message, naming the service waited on and what is needed of it, reaches the caller.

### Report several problems at once

An exception implementing `IProvideErrors` decides the whole response, so a check over a list reports every bad
entry, not only the first:

```csharp
using Bitwarden.Server.Sdk.Errors;

public sealed class InvalidWidgetsException(IReadOnlyList<int> positions)
    : Exception("Some widgets are invalid."), IProvideErrors
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

Register an `IExceptionTranslator` so a host answers a library's exception as the standard failure it means. A host
asks each translator in turn and the first to return one wins; return null for anything you don't recognize:

```csharp
using Bitwarden.Server.Sdk.Errors;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

public sealed class WidgetLockTranslator : IExceptionTranslator
{
    public Exception? Translate(Exception exception) =>
        exception is WidgetLockTimeoutException
            ? new ConflictException("The widget is being changed. Try again.")
            : null;
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

### Read an error

A caller deserializes `ErrorResponse` and branches on each error's `Code`, never on its `Title` or `Detail`, which
are prose and may be reworded:

```csharp
using System.Net.Http.Json;
using Bitwarden.Server.Sdk.Errors;

public static class WidgetErrors
{
    public static async Task<bool> IsMissingAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return false;
        }

        var document = await response.Content.ReadFromJsonAsync<ErrorResponse>(cancellationToken);
        return document?.Errors.Any(error => error.Code == ErrorCodes.NotFound) == true;
    }
}
```

## Types

| Type                                                      | Purpose                                                                |
| --------------------------------------------------------- | ---------------------------------------------------------------------- |
| `ErrorResponse`, `Error`, `ErrorSource`, `FailedResource` | The body of every unsuccessful response                                |
| `ErrorCodes`                                              | The stable codes a caller branches on                                  |
| `ErrorTitles`, `ErrorDetails`                             | The text that goes with each code                                      |
| `InvalidRequestException`, `NotFoundException`, ...       | The standard failures, each answered with its own status               |
| `PendingServiceException`                                 | A rule the service can't enforce yet, refused rather than let through  |
| `ServiceUnavailableException`                             | A service depended on is unreachable, slow, or said it was unavailable |
| `IProvideErrors`                                          | An exception that says exactly what the response holds                 |
| `IExceptionTranslator`                                    | Says which standard failure a library's own exception means            |
