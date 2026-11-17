# Bitwarden.Server.Sdk.Idempotency.Abstractions

**Audience:** engineers changing this package.

The idempotency key header and the operations whose writes repeat the same keys. See [PACKAGE.md](PACKAGE.md) for how
to use it.

## What belongs here

Only what both the side that sends a write and the side that receives it need. Nothing here touches `HttpContext` or
`HttpClient`, and nothing here sends or honors the header. It depends on Bitwarden.Server.Sdk.CryptoPrimitives, whose
`CacheKey` derives every key and identifier, and on Microsoft.Extensions.Hosting.Abstractions, for the environment that
decides whether the secret is required. Derive with `CacheKey` alone: compose no digest of your own from
`System.Security.Cryptography`. The plain SHA-256 in `IdempotentOperation` stands for a write's body in its
derivation, so a large body is hashed once rather than encoded, and shortens a long name in `ForMessage` and `ForJob`;
neither is a key.

## How it works

The current operation is an `AsyncLocal` scope, which is why an operation begun inside an awaited method isn't seen by
its caller. Ending a scope marks it ended and, when it is the current scope, restores the nearest one it was begun inside
that hasn't ended. A scope ended out of order is never current again, and leaves alone a scope begun since it.

Each scope holds its key and the secret it was begun with, and counts the times it has been asked for each target (a
write's method, address, body and context, or an identifier's purpose), so identical writes in one run get distinct
keys in the same order on every run. A target is counted by its derivation without the count, so the scope holds no
body and no context value. Keys and identifiers are `CacheKey.DeriveCacheKey(secret, parameters)`: HMAC-SHA-256 keyed by
the secret over each parameter length-prefixed, so no parameter can pass for two. The first parameter names the use, so
a key and an identifier never share an input:

| Derived       | Parameters, in order                                                                              | Result                                       |
| ------------- | ------------------------------------------------------------------------------------------------- | -------------------------------------------- |
| `KeyForWrite` | `idempotency-key`, operation key, method, address, body SHA-256 hex, count, each `{name}:{value}` | Lower-case hex of the 32 bytes               |
| `DeriveId`    | `derived-id`, operation key, purpose, count, subject if any                                       | First 16 bytes, with RFC 9562 version 8 bits |

A write's context headers come last, one parameter each, named in lower case and in order of name. A header's name has
no colon, so `{name}:{value}` reads back one way.

`ForMessage` and `ForJob` give `{kind}:{name}:{occurrence}`, the kind `msg` or `job`. A name may not hold a colon, and a
name too long or not visible ASCII is replaced by the hex of its UTF-8 SHA-256, so only the occurrence can hold one and
a key reads back one way.

The count is in invariant decimal digits. The identifier's version and variant bits are set in the big-endian byte
order RFC 9562 writes. `CacheKey` encodes text as strict UTF-8, so a lone surrogate throws `EncoderFallbackException`,
which `KeyForWrite` and `DeriveId` turn into an `ArgumentException` that doesn't quote the text.

`IdempotencySecret.ReadSecret` follows `OwnedAnswerKey.ReadSecret` in Resources.Caching.Abstractions: it compares the
environment name as `IHostEnvironment.IsDevelopment` does, and its messages name the setting, never the value. The
secret is not the cache's answer secret: a cache's secret is shared by every service that reads the cache, while only
the service that derived an idempotency key ever derives it again.

## Testing

Every source file has a matching test class in `tests/Bitwarden.Server.Sdk.Idempotency.Abstractions.Tests`.

```bash
dotnet test --project extensions/Bitwarden.Server.Sdk.Idempotency.Abstractions/tests/Bitwarden.Server.Sdk.Idempotency.Abstractions.Tests
```

A change must keep:

- **Keys stable.** A key derived today must be derived the same way by every instance and every later version, or a
  run after an upgrade does its writes twice. The tests' golden vectors for derived keys and identifiers were computed
  outside .NET, with a test secret and with `CacheSecret.Development`; a change that breaks one changes the keys.
- **No person's data in a key.** Keys and identifiers are keyed hashes of what the operation names, and neither an
  operation's key nor a purpose holds an email address or a name: a holder of the secret who knows the operation's key
  can confirm a guess at the rest.
