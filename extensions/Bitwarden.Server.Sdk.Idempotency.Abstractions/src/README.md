# Bitwarden.Server.Sdk.Idempotency.Abstractions

**Audience:** engineers changing this package.

The idempotency key header and the operations whose writes repeat the same keys. See [PACKAGE.md](PACKAGE.md) for how
to use it.

## What belongs here

Only what both the side that sends a write and the side that receives it need. Nothing here touches `HttpContext` or
`HttpClient`, and nothing here sends or honors the header. Its one dependency is BouncyCastle.Cryptography, for
TupleHash; it must take on no other.

## How it works

The current operation is an `AsyncLocal` scope, which is why an operation begun inside an awaited method isn't seen by
its caller. Ending a scope restores the one it was begun inside and marks it ended, so a scope ended out of order is
never current again.

Each scope counts the times it has been asked for each target (a write's method, address and body, or an identifier's
purpose), so identical writes in one run get distinct keys in the same order on every run. Keys and identifiers are
TupleHash256 (NIST SP 800-185) with 256 bits of output, each field one element of the tuple, so no field can pass for
two:

| Derived       | Customization string           | Elements, in order                                | Result                                       |
| ------------- | ------------------------------ | ------------------------------------------------- | -------------------------------------------- |
| `KeyForWrite` | `Bitwarden idempotency key`    | operation key, method, address, body bytes, count | Lower-case hex of the 32 bytes               |
| `DeriveId`    | `Bitwarden derived identifier` | operation key, purpose, count                     | First 16 bytes, with RFC 9562 version 8 bits |

Text elements are UTF-8, and the count is in invariant decimal digits. The identifier's version and variant bits are
set in the big-endian byte order RFC 9562 writes.

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
