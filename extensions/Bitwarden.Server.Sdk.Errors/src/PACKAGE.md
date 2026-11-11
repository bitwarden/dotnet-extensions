# Bitwarden.Server.Sdk.Errors

## About

The error document every Bitwarden service answers an unsuccessful request with, the stable codes a caller branches
on, and the exceptions a service throws to produce them: "not found", "conflict", "invalid request" and the rest.

It holds only what a service and its callers both need, and has no dependencies, so a program that only calls a
service, or a library that only throws, doesn't depend on the packages that host a service.

## Installing

```bash
dotnet add package Bitwarden.Server.Sdk.Errors
```

## Configuration

The package reads no settings.

## Getting started

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

See the [README](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Errors/src/README.md)
for a complete program that reads an error document.

## Documentation

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

### Answer with a status

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
  happened, never its address.
- An exception implementing `IProvideErrors` decides the whole response, so a check over a list can report every
  problem at once.
- An `IExceptionTranslator`, registered with `TryAddEnumerable`, says which of these a library's own exception means.
  A host asks each translator in turn, and the first to return one wins.

### Types

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
