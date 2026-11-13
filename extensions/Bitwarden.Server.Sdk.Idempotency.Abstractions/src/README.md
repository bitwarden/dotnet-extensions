# Bitwarden.Server.Sdk.Idempotency.Abstractions

**Audience:** engineers changing this package.

The idempotency key header and the operations whose writes repeat the same keys. See [PACKAGE.md](PACKAGE.md) for how
to use it.

## What belongs here

Only what both the side that sends a write and the side that receives it need. Nothing here touches `HttpContext` or
`HttpClient`, and nothing here sends or honors the header. The package has no dependencies, and must keep none.

## How it works

The current operation is an `AsyncLocal` scope, which is why an operation begun inside an awaited method isn't seen by
its caller. Ending a scope restores the one it was begun inside and marks it ended, so a scope ended out of order is
never current again.

Each scope counts the times it has been asked for each target, so identical writes in one run get distinct keys in the
same order on every run. A write's key is the lower-case hex SHA-256 of the operation key, the target
(`write`, method, address and the body's SHA-256) and that count, joined by newlines. `DeriveId` hashes the same way
with an `id` target and sets the version 8 and variant bits of RFC 9562.

## Testing

Every source file has a matching test class in `tests/Bitwarden.Server.Sdk.Idempotency.Abstractions.Tests`.

```bash
dotnet test --project extensions/Bitwarden.Server.Sdk.Idempotency.Abstractions/tests/Bitwarden.Server.Sdk.Idempotency.Abstractions.Tests
```

A change must keep:

- **Keys stable.** A key derived today must be derived the same way by every instance and every later version, or a
  run after an upgrade does its writes twice. The tests' golden vectors for derived keys and identifiers were computed
  outside .NET; a change that breaks one changes the wire format.
- **No person's data in a key.** Keys and identifiers are hashes of what the operation names, and an operation's key
  never holds an email address or a name.
