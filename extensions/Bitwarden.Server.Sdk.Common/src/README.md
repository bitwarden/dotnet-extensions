# Bitwarden.Server.Sdk.Common

Types that a Bitwarden service and the code that calls it both need to agree on. See
[PACKAGE.md](PACKAGE.md) for how to use them.

## What belongs here

Only what both a service and a caller of that service need. The test is whether removing a type
would force a caller to reference the package that hosts the service.

Things that look shared but are not:

- Resource declarations a service authors. A client consumes generated types for them instead.
- Anything touching `HttpContext`, MVC or `HttpClient`.

## Why it exists

The hosting package must be free to change without considering every caller. That is only true if
callers do not reference it, which is only true if the types they share live somewhere neither
owns.

## Configuration

The package reads no setting itself. It names the ones a host reads, so a service and its callers
agree on them.

Every setting the package names is a public constant on the type it belongs to, named `Key` or
`Section` or ending in one of them (for example `InternalTls.Section`). Its XML documentation says what the
setting does and what happens when it is unset. Look there for the full list; this file does not
repeat it.

## Testing

Every source file has a matching test class in
`tests/Bitwarden.Server.Sdk.Common.Tests`, in a folder that mirrors `src`.

```bash
dotnet test --project extensions/Bitwarden.Server.Sdk.Common/tests/Bitwarden.Server.Sdk.Common.Tests
```
