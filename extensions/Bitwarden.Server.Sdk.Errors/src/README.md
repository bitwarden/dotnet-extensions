# Bitwarden.Server.Sdk.Errors

**Audience:** engineers changing this package.

The error document and standard failures that every Bitwarden service and every caller of it agree on. See
[PACKAGE.md](PACKAGE.md) for how to use it.

## What belongs here

Only what both a service and its callers need. The test is whether removing a type would force a caller to reference
the code that hosts the service.

- **No dependencies.** A caller must be able to reference the package without anything else coming with it.
- **Nothing about the transport.** No `HttpContext`, MVC or `HttpClient`. Turning an exception into a response is a
  service host's job, and reading a response is the caller's.

## How it works

The package only declares the failures. `ErrorResponseExceptionHandler` in Bitwarden.Server.Sdk.Resources maps each
to its status, code and title, and answers anything it doesn't recognize with a `500` that describes nothing. A new
exception here needs a case there, or it is answered as that `500`.

A change must keep:

- **Codes stable.** A value in `ErrorCodes` is contract: callers branch on it. Add a code; never rename one. Titles
  and details may be reworded.
- **Nothing leaked in a message.** A detail tells the caller what they can act on: never an address, a host name, an
  exception's text, or whether a resource they may not see exists. `ServiceUnavailableException` builds its detail
  from the service's name only, for this reason.

## Testing

Every source file with logic has a matching test class in `tests/Bitwarden.Server.Sdk.Errors.Tests`. Run them from the
repository root:

```bash
dotnet test --project extensions/Bitwarden.Server.Sdk.Errors/tests/Bitwarden.Server.Sdk.Errors.Tests
```
