# Bitwarden.Server.Sdk.Context

**Audience:** engineers changing this package.

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

`ContextScope` and `CallChain` keep their current value in one internal `AmbientStack`: an `AsyncLocal` holding a
stack of disposable entries. Disposing an entry restores the one it was begun inside, skipping any already disposed,
and does so only on the flow where that entry is current. Work forked inside an entry and not awaited shares the
entry, so it keeps the value it started with after its parent disposes it.

A change must keep:

- **The wire stable.** Header names, the `Bitwarden-Context` members and version, the token's type, claims and modes,
  and the `CallerCredential` names are read by services deployed apart from each other. `ContextHeader` reads only
  `ContextHeader.Version` and refuses a member it doesn't know, so a new member reaches every service's reader before
  any writer sends it: the package ships to every service together, or the member comes with a new version that every
  reader accepts first.
- **Strict reading.** A malformed value is refused whole, never partly believed, and a refusal never echoes the value.
- **Nothing but service-name shapes in a chain.** `CallChain.IsServiceName` admits a lower-case name and refuses a
  GUID written as 32 hexadecimal digits or in hyphenated form, alone or inside a name. It does not refuse identifiers
  in general. Checking each name against the services it knows, before writing a chain to a span, a metric tag or a
  log, is the host's responsibility; nothing in this package does it.

## Testing

Every source file with logic has a matching test class in `tests/Bitwarden.Server.Sdk.Context.Tests`. Run them from
the repository root:

```bash
dotnet test --project extensions/Bitwarden.Server.Sdk.Context/tests/Bitwarden.Server.Sdk.Context.Tests
```
