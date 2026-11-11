# Bitwarden.Server.Sdk.Context

Who and what a call between Bitwarden services concerns, and how the call carries it. See [PACKAGE.md](PACKAGE.md)
for how to use it.

## What belongs here

Only what both a service and the services that call it need.

- **No dependencies, and nothing about the transport.** No `HttpContext` or `HttpClient`: a service host fills
  `IContext` from the request, and a client writes the headers on the calls it sends.
- **Interfaces a host implements.** `IRequestNamedOwner`, `IOrganizationCreation` and `ICallerProof` are defined here
  for the hosting package to implement, so a client can ask about the request without referencing the code that hosts
  the service. With no implementation registered, nothing is named or allowed.

## How it works

`ContextScope` and `CallChain` each keep their current value in an `AsyncLocal`, as a stack of disposable entries.
Disposing an entry restores the one it was begun inside, unless that one was already disposed, in which case there is
none.

A change must keep:

- **The wire stable.** Header names, the `Bitwarden-Context` members and version, the token's type, claims and modes,
  and the `CallerCredential` names are read by services deployed apart from each other. `ContextHeader` reads only
  `ContextHeader.Version`, so a new member needs a new version that every reader accepts first.
- **Strict reading.** A malformed value is refused whole, never partly believed, and a refusal never echoes the value.
- **Nothing but service names in a chain.** `CallChain.IsServiceName` admits no GUID, with or without hyphens, so
  nothing identifying a person, an organization or any data reaches a log or metric tag through it.

## Testing

Every source file with logic has a matching test class in `tests/Bitwarden.Server.Sdk.Context.Tests`. Run them from
the repository root:

```bash
dotnet test --project extensions/Bitwarden.Server.Sdk.Context/tests/Bitwarden.Server.Sdk.Context.Tests
```
